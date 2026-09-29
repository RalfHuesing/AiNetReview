namespace AiNetReview.IntegrationTests.Analysis;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.IntegrationTests.FixtureAnalyses;

public sealed class ReviewRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsCurrentReviewAnalysisResultsWithoutRetainingState()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp);
        var analysis = new FixtureFindingAnalysis();
        var config = CreateConfig(root, [analysis], "base");
        using var loaded = await new SolutionLoader().LoadAsync(config);
        var runner = new ReviewRunner();

        var firstRun = await runner.RunAsync(config, loaded);
        var repeatedRun = await runner.RunAsync(config, loaded);
        var emptyAnalysisRun = await runner.RunAsync(CreateConfig(root, [new TestAnalysis("empty-analysis", [])]), loaded);

        var firstResult = Assert.Single(firstRun.Analyses).Result;
        var repeatedResult = Assert.Single(repeatedRun.Analyses).Result;
        Assert.Equal(2, firstRun.DetectedCount);
        Assert.Equal(2, Assert.Single(firstRun.Analyses).DetectedCount);
        Assert.Equal(2, firstResult.Findings.Count);
        Assert.Equal(2, repeatedResult.Findings.Count);
        Assert.Equal(
            firstResult.Findings.Select(static finding => finding.SubjectId),
            repeatedResult.Findings.Select(static finding => finding.SubjectId));
        Assert.All(firstResult.Findings, finding => Assert.Single(finding.Evidence));
        Assert.Contains("FixtureCaseA", firstResult.Findings[0].SubjectId, StringComparison.Ordinal);
        Assert.Contains("FixtureCaseB", firstResult.Findings[1].SubjectId, StringComparison.Ordinal);

        var emptyConfig = CreateConfig(root, [new FixtureFindingAnalysis()], "none");
        var emptyRun = await runner.RunAsync(emptyConfig, loaded);
        Assert.Empty(Assert.Single(emptyRun.Analyses).Result.Findings);
        Assert.Equal(0, emptyRun.DetectedCount);
        Assert.Equal(0, emptyAnalysisRun.DetectedCount);
        Assert.Empty(Assert.Single(emptyAnalysisRun.Analyses).Result.Findings);
    }

    [Fact]
    public async Task RunAsync_ValidatesUniqueFindingsAndSortsThemDeterministically()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp);
        var analysis = new TestFindingAnalysis("ordered-analysis", [
            CreateFinding("Sample/FixtureCases.cs", "B", 5, "FixtureCaseB"),
            CreateFinding("Sample/FixtureCases.cs", "A", 4, "FixtureCaseA"),
        ]);
        var config = CreateConfig(root, [analysis]);
        using var loaded = await new SolutionLoader().LoadAsync(config);

        var result = await new ReviewRunner().RunAsync(config, loaded);

        var findings = Assert.Single(result.Analyses).Result.Findings;
        Assert.Equal(new[] { "A", "B" }, findings.Select(static finding => finding.SubjectId));
    }

    [Fact]
    public async Task RunAsync_RejectsDuplicateAndInvalidFindingDrafts()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp);
        var finding = CreateFinding("Sample/FixtureCases.cs", "A", 4, "FixtureCaseA");
        using var loaded = await new SolutionLoader().LoadAsync(CreateConfig(root, [new TestFindingAnalysis("duplicate-analysis", [finding, finding])]));

        await Assert.ThrowsAsync<AnalysisFailedException>(() => new ReviewRunner().RunAsync(
            CreateConfig(root, [new TestFindingAnalysis("duplicate-analysis", [finding, finding])]), loaded));

        var invalidEvidence = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample/FixtureCases.cs",
            "invalid",
            "invalid",
            4,
            "Invalid fixture finding.",
            new Dictionary<string, double> { ["metric"] = 1 },
            [new FindingEvidence("Sample/FixtureCases.cs", 4, "Fixture", "Invalid source evidence", "not in loaded source")]);
        await Assert.ThrowsAsync<AnalysisFailedException>(() => new ReviewRunner().RunAsync(
            CreateConfig(root, [new TestFindingAnalysis("invalid-analysis", [invalidEvidence])]), loaded));

        var invalidMetric = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample/FixtureCases.cs",
            "invalid-metric",
            "invalid",
            4,
            "Invalid fixture finding.",
            new Dictionary<string, double> { ["metric"] = double.NaN },
            [new FindingEvidence("Sample/FixtureCases.cs", 4, "Fixture", "Valid source evidence", "FixtureCaseA")]);
        await Assert.ThrowsAsync<AnalysisFailedException>(() => new ReviewRunner().RunAsync(
            CreateConfig(root, [new TestFindingAnalysis("invalid-metric-analysis", [invalidMetric])]), loaded));
    }

    [Fact]
    public async Task RunAsync_AcceptsEvidenceFromAnotherLoadedProject()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, includeSecondProject: true);
        var finding = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample/FixtureCases.cs",
            "cross-project-evidence",
            "case",
            4,
            "Review the related implementation.",
            new Dictionary<string, double> { ["count"] = 1 },
            [new FindingEvidence("Other/Other.cs", 1, "Related implementation", "Evidence in another project", "LoadedOtherValue")]);
        using var loaded = await new SolutionLoader().LoadAsync(CreateConfig(root, [new TestFindingAnalysis("cross-project-analysis", [finding])]));

        var result = await new ReviewRunner().RunAsync(
            CreateConfig(root, [new TestFindingAnalysis("cross-project-analysis", [finding])]), loaded);

        Assert.Same(finding, Assert.Single(Assert.Single(result.Analyses).Result.Findings));
    }

    [Theory]
    [InlineData("Unknown/Unknown.cs", 1, "Missing")]
    [InlineData("Sample/../Sample/FixtureCases.cs", 4, "FixtureCaseA")]
    [InlineData("Sample\\FixtureCases.cs", 4, "FixtureCaseA")]
    [InlineData("Sample/FixtureCases.cs", 0, "FixtureCaseA")]
    [InlineData("Sample/FixtureCases.cs", 99, "FixtureCaseA")]
    [InlineData("Sample/FixtureCases.cs", 4, "not present")]
    public async Task RunAsync_RejectsUnknownNoncanonicalOrInvalidEvidence(string evidencePath, int line, string snippet)
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, includeSecondProject: true);
        var finding = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample/FixtureCases.cs",
            "invalid-evidence",
            "case",
            4,
            "Invalid evidence fixture.",
            new Dictionary<string, double> { ["count"] = 1 },
            [new FindingEvidence(evidencePath, line, "Fixture", "Invalid evidence", snippet)]);
        var config = CreateConfig(root, [new TestFindingAnalysis("invalid-evidence-analysis", [finding])]);
        using var loaded = await new SolutionLoader().LoadAsync(config);

        await Assert.ThrowsAsync<AnalysisFailedException>(() => new ReviewRunner().RunAsync(config, loaded));
    }

    [Fact]
    public async Task RunAsync_ValidatesEvidenceAgainstLoadedSnapshotInsteadOfChangedDiskFile()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, includeSecondProject: true);
        var config = CreateConfig(root, [new TestAnalysis("snapshot-analysis", [])]);
        using var loaded = await new SolutionLoader().LoadAsync(config);
        await File.WriteAllTextAsync(Path.Combine(root, "Other", "Other.cs"),
            """namespace Other; public sealed class Other { public int Value => "ChangedDiskOnly".Length; }""");

        var changedDiskEvidence = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample/FixtureCases.cs",
            "changed-disk-evidence",
            "case",
            4,
            "This text exists only after loading.",
            new Dictionary<string, double> { ["count"] = 1 },
            [new FindingEvidence("Other/Other.cs", 1, "Changed file", "Not in snapshot", "ChangedDiskOnly")]);
        var invalidConfig = CreateConfig(root, [new TestFindingAnalysis("snapshot-analysis", [changedDiskEvidence])]);

        await Assert.ThrowsAsync<AnalysisFailedException>(() => new ReviewRunner().RunAsync(invalidConfig, loaded));

        var loadedSnapshotEvidence = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample/FixtureCases.cs",
            "loaded-snapshot-evidence",
            "case",
            4,
            "This text remains in the loaded snapshot.",
            new Dictionary<string, double> { ["count"] = 1 },
            [new FindingEvidence("Other/Other.cs", 1, "Loaded file", "Snapshot evidence", "LoadedOtherValue")]);
        var validConfig = CreateConfig(root, [new TestFindingAnalysis("snapshot-analysis", [loadedSnapshotEvidence])]);
        var result = await new ReviewRunner().RunAsync(validConfig, loaded);
        Assert.Same(loadedSnapshotEvidence, Assert.Single(Assert.Single(result.Analyses).Result.Findings));
    }

    [Fact]
    public async Task RunAsync_OrdersAnalysesAndPropagatesIncompleteRuns()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp);
        var calls = new List<string>();
        var alpha = new TestAnalysis("alpha-analysis", calls);
        var zeta = new TestAnalysis("zeta-analysis", calls);
        var config = CreateConfig(root, [zeta, alpha]);
        using var loaded = await new SolutionLoader().LoadAsync(config);
        var runner = new ReviewRunner();

        var result = await runner.RunAsync(config, loaded);

        Assert.Equal(new[] { "alpha-analysis", "zeta-analysis" }, calls);
        Assert.Equal(new[] { "alpha-analysis", "zeta-analysis" }, result.Analyses.Select(static analysis => analysis.AnalysisId));

        var failure = CreateConfig(root, [new TestAnalysis("failure-analysis", calls, throwOnRun: true)]);
        await Assert.ThrowsAsync<AnalysisFailedException>(() => runner.RunAsync(failure, loaded));

        using var cancellation = new CancellationTokenSource();
        var cancelling = CreateConfig(root, [new TestAnalysis("cancelling-analysis", calls, cancel: cancellation)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancelling, loaded, cancellation.Token));
    }

    private static ReviewConfig CreateConfig(string root, IEnumerable<IReviewAnalysis> analyses, string? scenario = null)
    {
        var registry = new ReviewAnalysisRegistry(analyses);
        var analysisJson = string.Join(",", registry.Analyses.Select(analysis => scenario is null
            ? $"\"{analysis.Descriptor.AnalysisId}\":{{}}"
            : $"\"{analysis.Descriptor.AnalysisId}\":{{\"scenario\":\"{scenario}\"}}"));
        var json = $$"""
            {
              "schemaVersion": 1,
              "solution": "Sample.slnx",
              "outputDirectory": "reports",
              "analyses": { {{analysisJson}} }
            }
            """;
        return new ReviewConfigValidator(registry).Validate(root, json);
    }

    private static async Task<string> CreateProjectAsync(TestTempDirectory temp, bool includeSecondProject = false)
    {
        var root = temp.GetPath("runner-project");
        var projectDirectory = Path.Combine(root, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "FixtureCases.cs"), """
            namespace Sample;
            public sealed class FixtureCases
            {
                public int FixtureCaseA() => 1;
                public int FixtureCaseB() => 2;
            }
            """);
        await RestoreAsync(projectFile, projectDirectory);
        var solutionProjects = "<Project Path=\"Sample/Sample.csproj\" />";
        if (includeSecondProject)
        {
            var otherDirectory = Path.Combine(root, "Other");
            Directory.CreateDirectory(otherDirectory);
            var otherProject = Path.Combine(otherDirectory, "Other.csproj");
            await File.WriteAllTextAsync(otherProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(otherDirectory, "Other.cs"),
                """namespace Other; public sealed class Other { public int Value => "LoadedOtherValue".Length; }""");
            await RestoreAsync(otherProject, otherDirectory);
            solutionProjects += "<Project Path=\"Other/Other.csproj\" />";
        }

        await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"), $"<Solution>{solutionProjects}</Solution>");
        return root;
    }

    private static FindingDraft CreateFinding(string sourcePath, string id, int line, string snippet) => new(
        "Sample/Sample.csproj",
        sourcePath,
        id,
        "case",
        line,
        $"Fixture finding {id}.",
        new Dictionary<string, double> { ["count"] = 1 },
        [new FindingEvidence(sourcePath, line, "Fixture", $"Evidence for {id}", snippet)]);

    private static async Task RestoreAsync(string projectFile, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {await stdout}{await stderr}");
    }

    private sealed class TestAnalysis : IReviewAnalysis
    {
        private readonly List<string> calls;
        private readonly bool throwOnRun;
        private readonly CancellationTokenSource? cancel;

        internal TestAnalysis(string analysisId, List<string> calls, bool throwOnRun = false, CancellationTokenSource? cancel = null)
        {
            this.calls = calls;
            this.throwOnRun = throwOnRun;
            this.cancel = cancel;
            Descriptor = new ReviewAnalysisDescriptor(analysisId, "Test Review analysis", 1, "Test purpose.", "Test measurement.", ["Is this test analysis registered?"]);
        }

        public ReviewAnalysisDescriptor Descriptor { get; }

        public async Task<ReviewAnalysisResult> ExecuteAsync(ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken)
        {
            calls.Add(Descriptor.AnalysisId);
            if (throwOnRun)
            {
                throw new InvalidOperationException("fixture analysis failed");
            }

            if (cancel is not null)
            {
                await cancel.CancelAsync();
            }

            return ReviewAnalysisResult.Empty;
        }
    }

    private sealed class TestFindingAnalysis : IReviewAnalysis
    {
        private readonly IReadOnlyList<FindingDraft> findings;

        internal TestFindingAnalysis(string analysisId, IReadOnlyList<FindingDraft> findings)
        {
            this.findings = findings;
            Descriptor = new ReviewAnalysisDescriptor(analysisId, "Finding Review analysis", 1, "Produces test findings.", "Counts fixture cases.", ["Are findings current?"]);
        }

        public ReviewAnalysisDescriptor Descriptor { get; }

        public Task<ReviewAnalysisResult> ExecuteAsync(ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ReviewAnalysisResult(findings));
        }
    }
}
