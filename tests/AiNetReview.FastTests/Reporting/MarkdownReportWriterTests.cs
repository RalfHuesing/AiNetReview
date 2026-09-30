namespace AiNetReview.FastTests.Reporting;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;

public sealed class MarkdownReportWriterTests
{
    [Fact]
    public async Task WriteAsync_RendersEveryStructuralOccurrenceWithFramedPathsAndExclusiveCoordinates()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string projectPath = "Product; One/[β]`special.csproj";
        const string sourcePath = "src/file ; [x]`β.cs";
        const string owner = "M:Product.Sample.Run(System.Int32)";
        var finding = new FindingDraft(
            projectPath,
            sourcePath,
            owner,
            "structural-duplicate:12:80",
            3,
            "This group contains repeated fragments.",
            new Dictionary<string, double>
            {
                ["memberCount"] = 3,
                ["executableCount"] = 2,
                ["statementCount"] = 3,
                ["tokenCount"] = 60,
            },
            [
                new FindingEvidence(sourcePath, 3, owner,
                    $"project={JsonSerializer.Serialize(projectPath)};start=3:5;end=7:10", "int x = value;"),
                new FindingEvidence(sourcePath, 4, owner,
                    $"project={JsonSerializer.Serialize(projectPath)};start=4:1;end=8:2", "int x = value;"),
                new FindingEvidence(sourcePath, 9, "M:Product.Other.Run(System.Int32)",
                    $"project={JsonSerializer.Serialize(projectPath)};start=9:2;end=12:1", "int y = input;"),
            ],
            [new FindingSymbol(projectPath, sourcePath, owner, 3),
                new FindingSymbol(projectPath, sourcePath, "M:Product.Other.Run(System.Int32)", 9)]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId,
            "all-findings", "structural-duplication-candidates.md"));

        Assert.Contains("Structural duplicate: 3 occurrences in 2 executable members; 3 statements / 60 tokens; identical after local/parameter normalization.", markdown, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: ", markdown, StringComparison.Ordinal);
        Assert.Contains("(1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains(projectPath, markdown, StringComparison.Ordinal);
        Assert.Contains(sourcePath, markdown, StringComparison.Ordinal);
        Assert.Contains("``Product; One/[β]`special.csproj``", markdown, StringComparison.Ordinal);
        Assert.Contains("start `3:5`; end-exclusive `7:10`", markdown, StringComparison.Ordinal);
        Assert.Contains("start `4:1`; end-exclusive `8:2`", markdown, StringComparison.Ordinal);
        Assert.Contains("start `9:2`; end-exclusive `12:1`", markdown, StringComparison.Ordinal);
        Assert.Equal(1, markdown.Split("### Project: ", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("#### File: ", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("Statement and control\\-flow shape", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("Is this repeated structure intentional", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task WriteAsync_FormatsNonAsciiIdentifiersSignalAndReport()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new NonAsciiIdentifiersAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var finding = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample.cs",
            "T:Sample.BestätigungsService",
            "type",
            10,
            "The type identifier 'BestätigungsService' contains non-ASCII characters (e.g. 'ä').",
            new Dictionary<string, double>(),
            [new FindingEvidence("Sample.cs", 10, "Type declaration", "detail", "BestätigungsService")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "all-findings", "non-ascii-identifiers.md"));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("- `T:Sample.BestätigungsService`", markdown, StringComparison.Ordinal);
        Assert.Contains("Signal: The type identifier 'BestätigungsService' contains non\\-ASCII characters (e.g. 'ä').", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_ReportsEmptyActiveAnalysesWithoutCreatingAnalysisFiles()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("alpha-analysis", "Alpha | Review analysis", "value|with `markdown`");
        var secondAnalysis = new ReportAnalysis("zeta-analysis", "Zeta Review analysis", "last");
        var config = CreateConfig(temp.DirectoryPath, analysis, secondAnalysis);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(secondAnalysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
        ]));

        var indexBytes = await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(config.ResolvedOutputDirectory, report.RunId), "*", SearchOption.AllDirectories).Length);
        Assert.False(indexBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', indexBytes);
        var index = Encoding.UTF8.GetString(indexBytes);
        Assert.Contains($"# AiNetReview – {report.RunId}", index, StringComparison.Ordinal);
        Assert.Contains("- Run ID:", index, StringComparison.Ordinal);
        var repositoryLine = Assert.Single(index.Split('\n').Where(static line => line.StartsWith("- Repository:", StringComparison.Ordinal)));
        var repositoryPath = repositoryLine["- Repository: `".Length..^1].Replace("\\\\", "\\", StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(repositoryPath));
        Assert.Contains("- Solution: `Sample.slnx`", index, StringComparison.Ordinal);
        Assert.Contains("No findings were found.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Started", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Detected", index, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(config.ResolvedOutputDirectory, report.RunId)).Length);
        Assert.Equal("reports/" + report.RunId + "/index.md", report.IndexPath);
    }

    [Fact]
    public async Task WriteAsync_DistinguishesWhenNoAnalysesAreActive()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("inactive-analysis", "Inactive Review analysis", "unused");
        var config = CreateConfig(temp.DirectoryPath, false, analysis);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("No review was performed because all analyses are disabled.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("No findings were found", index, StringComparison.Ordinal);
        Assert.Equal(3, Directory.GetFiles(runDirectory, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task WriteAsync_LinksOnlyAnalysesWithFindingsAndWritesOneListItemPerFinding()
    {
        using var temp = TestTempDirectory.Create();
        var withFindings = new ReportAnalysis("has-findings", "Has Findings", "active");
        var withoutFindings = new ReportAnalysis("empty-analysis", "Empty Review analysis", "active");
        var config = CreateConfig(temp.DirectoryPath, withFindings, withoutFindings);
        var finding = Finding("Sample.cs", 2, "C:Sample", "A concise signal", "class Sample", "type-candidate");
        var samePathInAnotherProject = new FindingDraft(
            "Other/Sample.csproj", "Sample.cs", "C:Other", "type-candidate", 3, "A concise signal",
            new Dictionary<string, double>(),
            [new FindingEvidence("Sample.cs", 3, "Type declaration", "A candidate declaration.", "class Other")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(withFindings.Descriptor.AnalysisId, new ReviewAnalysisResult([finding, samePathInAnotherProject])),
            new ReviewAnalysisRunResult(withoutFindings.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var analysisReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "has-findings.md"));
        var changedReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "changed-files", "has-findings.md"));

        Assert.Contains("[Open the changed files view](changed-files/index.md)", index, StringComparison.Ordinal);
        Assert.Contains("[Open the complete findings view](all-findings/index.md)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Empty Review analysis", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "all-findings", "index.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "changed-files", "index.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "all-findings", "empty-analysis.md")));
        Assert.Contains("## Findings", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2 across 2 projects and 2 source files.", analysisReport, StringComparison.Ordinal);
        Assert.Contains("### Project: Other/Sample.csproj (1 files, 1 findings)", analysisReport, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample.cs (1 findings)", analysisReport, StringComparison.Ordinal);
        Assert.Contains("### Project: Sample/Sample.csproj (1 files, 1 findings)", analysisReport, StringComparison.Ordinal);
        Assert.DoesNotContain("| Project | Source file | Findings |", analysisReport, StringComparison.Ordinal);
        Assert.Contains("- `C:Sample`", analysisReport, StringComparison.Ordinal);
        Assert.Contains("- `C:Other`", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Review policy: These potential signals do not require changes. Use `changed-files/` as the primary review set;", changedReport, StringComparison.Ordinal);
        Assert.Contains("[root index's Review guidance](../index.md#review-guidance)", changedReport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_GroupsFindingsDeterministicallyAndEscapesMarkdownTableAndHeadings()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string projectPath = "Project|One/β`name.csproj";
        const string sourcePath = "src|folder/line\nbreak`file.cs";
        var first = new FindingDraft(projectPath, sourcePath, "M:Zed.Run", "z", 4, "last|signal",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 4, "Member", "detail", "Run")]);
        var second = new FindingDraft(projectPath, sourcePath, "M:Alpha.Run", "a", 4, "first|signal",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 4, "Member", "detail", "Run")]);
        var sameSubject = new FindingDraft(projectPath, sourcePath, "M:Alpha.Run", "b", 4, "second|signal",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 4, "Member", "detail", "Run")]);
        var otherProject = new FindingDraft("Another.csproj", sourcePath, "M:Only.Run", "only", 1, "single",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 1, "Member", "detail", "Run")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([first, otherProject, sameSubject, second])),
        ]));
        var markdownPath = Path.Combine(config.ResolvedOutputDirectory, report.RunId, "all-findings", "fixture-analysis.md");
        var markdownBytes = await File.ReadAllBytesAsync(markdownPath);
        var markdown = Encoding.UTF8.GetString(markdownBytes);

        Assert.Contains("## Summary\n\nTotal findings: 4 across 2 projects and 2 source files.", markdown, StringComparison.Ordinal);
        Assert.Contains("### Project: Another.csproj (1 files, 1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("### Project: Project\\|One/β\\`name.csproj (1 files, 3 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: src\\|folder/line break\\`file.cs (1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: src\\|folder/line break\\`file.cs (3 findings)", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("first\\|signal", StringComparison.Ordinal) < markdown.IndexOf("second\\|signal", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("second\\|signal", StringComparison.Ordinal) < markdown.IndexOf("last\\|signal", StringComparison.Ordinal));
        Assert.DoesNotContain("`src|folder/line", markdown, StringComparison.Ordinal);
        Assert.Equal(4, markdown.Split("- Signal:", StringSplitOptions.None).Length - 1);
        Assert.False(markdownBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', markdownBytes);
    }

    [Fact]
    public async Task WriteAsync_SortsFindingsAndEscapesContentAndSourceLinks()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var z = Finding("z file#1.cs", 9, "Z", "last|rationale", "second`snippet", "zeta");
        var a = Finding("a file#1.cs", 3, "A", "first | rationale", "`snippet`", "alpha");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([z, a])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "all-findings", "fixture-analysis.md"));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("- `A`", StringComparison.Ordinal) < markdown.IndexOf("- `Z`", StringComparison.Ordinal));
        Assert.Contains("\\| rationale", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: a file\\#1.cs (1 findings)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("aMetric", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Metrics", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("detail", markdown, StringComparison.Ordinal);
        Assert.Contains("Effective options:", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("\"alpha\"", StringComparison.Ordinal) < markdown.IndexOf("\"scenario\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\\{", markdown, StringComparison.Ordinal);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        Assert.Contains("Review guidance", index, StringComparison.Ordinal);
        Assert.Contains("All findings may validly result in no changes.", index, StringComparison.Ordinal);
        Assert.Contains("An accurate signal can describe an acceptable design; distinguish that from a false positive.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("First remove only clear false positives", index, StringComparison.Ordinal);
        Assert.True(index.IndexOf("## Review guidance", StringComparison.Ordinal) < index.IndexOf("## Analysis reports", StringComparison.Ordinal));
        Assert.Contains("Set a new baseline", index, StringComparison.Ordinal);
        Assert.Contains($" baseline '{config.ProjectRoot}'", index, StringComparison.Ordinal);
        Assert.DoesNotContain("--cmd", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_LinksEveryClusterMemberToItsSourceLocationAndAddsRootReportLinks()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("duplicate-code-candidates", "Duplicate code", "active");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "First.cs"), "class First { }");
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "Second.cs"), "class Second { }");
        var first = new FindingSymbol("Sample/Sample.csproj", "First.cs", "M:First.Run", 2);
        var second = new FindingSymbol("Other/Other.csproj", "Second.cs", "M:Second.Run", 5);
        var finding = new FindingDraft("Sample/Sample.csproj", "First.cs", "M:First.Run", "duplicate-cluster", 2, "similar methods",
            new Dictionary<string, double>(), [new FindingEvidence("First.cs", 2, "Member", "member source", "Run")], [first, second]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var markdown = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "duplicate-code-candidates.md"));
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("Total findings: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: First.cs (1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", markdown, StringComparison.Ordinal);
        Assert.Contains("`First.cs`: `M:First.Run` (line 2)", markdown, StringComparison.Ordinal);
        Assert.Contains("`Second.cs`: `M:Second.Run` (line 5)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Project | Source file | Findings |", markdown, StringComparison.Ordinal);
        Assert.Contains("(changed-files/duplicate-code-candidates.md)", index, StringComparison.Ordinal);
        Assert.Contains("(all-findings/index.md)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("(all-findings/duplicate-code-candidates.md)", index, StringComparison.Ordinal);
        Assert.Contains("Agent instruction:** Do not inspect", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "all-findings", "duplicate-code-candidates.md")));
        var allFindingsIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "index.md"));
        Assert.Contains("Notice for AI agents:**", allFindingsIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RendersForwardingPathInEvidenceOrderWithStructuralMetrics()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new IndirectionDriftCandidatesAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var finding = new FindingDraft(
            "Sample/Sample.csproj",
            "ZApi.cs",
            "M:ZApi.Run(System.Int32)",
            "transparent-forwarding-path",
            1,
            "Current statically declared path.",
            new Dictionary<string, double>
            {
                ["forwardingEdgeCount"] = 2,
                ["distinctTypeCount"] = 3,
                ["distinctFileCount"] = 3,
            },
            [
                new FindingEvidence("ZApi.cs", 1, "M:ZApi.Run(System.Int32)", "Forwards to service.", "return BService.Run(value);"),
                new FindingEvidence("BService.cs", 1, "M:BService.Run(System.Int32)", "Forwards to repository.", "return ARepository.Run(value);"),
                new FindingEvidence("ARepository.cs", 1, "M:ARepository.Run(System.Int32)", "Ends the path.", "return value;"),
            ],
            [
                new FindingSymbol("Sample/Sample.csproj", "ARepository.cs", "M:ARepository.Run(System.Int32)", 1),
                new FindingSymbol("Sample/Sample.csproj", "BService.cs", "M:BService.Run(System.Int32)", 1),
                new FindingSymbol("Sample/Sample.csproj", "ZApi.cs", "M:ZApi.Run(System.Int32)", 1),
            ]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var allFindings = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "indirection-drift-candidates.md"));
        var changedFiles = await File.ReadAllTextAsync(Path.Combine(runDirectory, "changed-files", "indirection-drift-candidates.md"));

        Assert.Contains("This is the reference-only `all-findings/` view; inspect or report it only when the user explicitly requests a full repository audit.", allFindings, StringComparison.Ordinal);
        Assert.Contains("Use `changed-files/` as the primary review set;", changedFiles, StringComparison.Ordinal);
        Assert.Equal(allFindings.Replace(
            "Review policy: These potential signals do not require changes. This is the reference-only `all-findings/` view; inspect or report it only when the user explicitly requests a full repository audit. See the [root index's Review guidance](../index.md#review-guidance).",
            "Review policy: These potential signals do not require changes. Use `changed-files/` as the primary review set; see the [root index's Review guidance](../index.md#review-guidance).",
            StringComparison.Ordinal), changedFiles);
        Assert.Contains("Total findings: 1", allFindings, StringComparison.Ordinal);
        Assert.Contains("#### File: ZApi.cs (1 findings)", allFindings, StringComparison.Ordinal);
        Assert.Contains("- Forwarding path: 2 forwarding edges across 3 types and 3 files", allFindings, StringComparison.Ordinal);
        Assert.True(allFindings.IndexOf("`ZApi.cs`:1: `M:ZApi.Run(System.Int32)`", StringComparison.Ordinal)
            < allFindings.IndexOf("`BService.cs`:1: `M:BService.Run(System.Int32)`", StringComparison.Ordinal));
        Assert.True(allFindings.IndexOf("`BService.cs`:1: `M:BService.Run(System.Int32)`", StringComparison.Ordinal)
            < allFindings.IndexOf("`ARepository.cs`:1: `M:ARepository.Run(System.Int32)`", StringComparison.Ordinal));
        Assert.DoesNotContain("Cluster:", allFindings, StringComparison.Ordinal);
        Assert.DoesNotContain("return BService.Run(value)", allFindings, StringComparison.Ordinal);
        Assert.DoesNotContain("#L1", allFindings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RendersMissingTestEvidenceCategoriesPathsLimitsAndSnapshotSelection()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new MissingTestEvidenceCandidatesAnalysis();
        var otherAnalysis = new ReportAnalysis("other-analysis", "Other Analysis", "file based selection");
        var config = CreateConfig(temp.DirectoryPath, analysis, otherAnalysis);
        var noPath = new FindingDraft("Sample/Sample.csproj", "Sample/NoPath.cs", "M:Sample.NoPath.Run", "no-static-test-path", 2,
            "no static test path", new Dictionary<string, double>
            {
                ["decisionCount"] = 3, ["maxDecisionNesting"] = 2, ["attributionUncertain"] = 1,
            },
            [new FindingEvidence("Sample/NoPath.cs", 2, "Declaration", "Eligible candidate", "Run")]);
        var indirect = new FindingDraft("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", "indirect-test-path-only", 4,
            "indirect test path only", new Dictionary<string, double>
            {
                ["decisionCount"] = 5, ["maxDecisionNesting"] = 3, ["attributionUncertain"] = 0,
            },
            [
                new FindingEvidence("Sample/Indirect.cs", 4, "M:Sample.Indirect.Run", "Eligible candidate", "Run"),
                new FindingEvidence("Sample.Tests/Tests.cs", 3, "M:Sample.Tests.Tests.CallsApi", "Test root", "CallsApi"),
                new FindingEvidence("Sample/Generated/Worker.g.cs", 2, "M:Sample.GeneratedIntermediate.Run", "Generated intermediate", "Run"),
                new FindingEvidence("Sample/Api.cs", 8, "M:Sample.Api.Run", "Intermediate", "Run"),
                new FindingEvidence("Sample/Indirect.cs", 4, "M:Sample.Indirect.Run", "Candidate", "Run"),
            ],
            [new FindingSymbol("Sample/Sample.csproj", "Sample.Tests/Tests.cs", "M:Sample.Tests.Tests.CallsApi", 3),
                new FindingSymbol("Sample/Sample.csproj", "Sample/Api.cs", "M:Sample.Api.Run", 8),
                new FindingSymbol("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", 4)]);
        var otherFinding = Finding("Sample/Other.cs", 1, "M:Sample.Other.Run", "unchanged analysis finding", "Run", "case");
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([noPath, indirect])),
            new ReviewAnalysisRunResult(otherAnalysis.Descriptor.AnalysisId, new ReviewAnalysisResult([otherFinding])),
        ])
        {
            Findings = [
                new ReviewFinding(analysis.Descriptor.AnalysisId, noPath, ["Sample/NoPath.cs"], [], []),
                new ReviewFinding(analysis.Descriptor.AnalysisId, indirect, ["Sample/Indirect.cs", "Sample/Api.cs", "Sample.Tests/Tests.cs"], [], []),
                new ReviewFinding(otherAnalysis.Descriptor.AnalysisId, otherFinding, ["Sample/Other.cs"], [], []),
            ],
            HasCSharpSnapshotChanges = false,
        };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var allFindings = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "missing-test-evidence-candidates.md"));
        var changedIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "changed-files", "index.md"));
        var rootIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("no static test path; 3 decisions, nesting 2; attribution uncertain", allFindings, StringComparison.Ordinal);
        Assert.Contains("indirect test path only; 5 decisions, nesting 3", allFindings, StringComparison.Ordinal);
        Assert.Contains("Shortest resolved test path:", allFindings, StringComparison.Ordinal);
        Assert.Contains("M:Sample.Tests.Tests.CallsApi (Sample.Tests/Tests.cs:3)", allFindings, StringComparison.Ordinal);
        Assert.Contains("M:Sample.GeneratedIntermediate.Run (Sample/Generated/Worker.g.cs:2)", allFindings, StringComparison.Ordinal);
        Assert.Contains("M:Sample.Api.Run (Sample/Api.cs:8)", allFindings, StringComparison.Ordinal);
        Assert.Contains("M:Sample.Indirect.Run (Sample/Indirect.cs:4)", allFindings, StringComparison.Ordinal);
        Assert.Contains("Reflection, dependency injection, external test projects, dynamic dispatch, branch execution, and custom test discovery", allFindings, StringComparison.Ordinal);
        Assert.Contains("No findings in this view.", changedIndex, StringComparison.Ordinal);
        Assert.Contains("shows every current finding when any C# path was added, changed, or deleted", rootIndex, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "all-findings", "index.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "changed-files", "missing-test-evidence-candidates.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "all-findings", "other-analysis.md")));

        var changedSnapshotReport = await new MarkdownReportWriter().WriteAsync(config, result with { HasCSharpSnapshotChanges = true });
        var changedSnapshotDirectory = Path.Combine(config.ResolvedOutputDirectory, changedSnapshotReport.RunId);
        var selected = await File.ReadAllTextAsync(Path.Combine(changedSnapshotDirectory, "changed-files", "missing-test-evidence-candidates.md"));
        var selectedIndex = await File.ReadAllTextAsync(Path.Combine(changedSnapshotDirectory, "changed-files", "index.md"));
        Assert.Contains("no static test path", selected, StringComparison.Ordinal);
        Assert.Contains("indirect test path only", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Analysis", selectedIndex, StringComparison.Ordinal);

        var withoutBaselineReport = await new MarkdownReportWriter().WriteAsync(config, result with { HasCSharpSnapshotChanges = null });
        var withoutBaselineDirectory = Path.Combine(config.ResolvedOutputDirectory, withoutBaselineReport.RunId);
        Assert.True(File.Exists(Path.Combine(withoutBaselineDirectory, "changed-files", "missing-test-evidence-candidates.md")));
    }

    [Fact]
    public async Task WriteAsync_UsesTheCentralAuditBaselineScriptWhenSupplied()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("central-analysis", "Central", "active");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var scriptPath = Path.Combine(temp.DirectoryPath, "scripts", "test-audit.ps1");
        var context = new BaselineCommandContext(scriptPath, "sample-target");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
        ]), baselineCommandContext: context);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));

        Assert.Contains($"& '{scriptPath}' -Target 'sample-target' -BaselineOnly", index, StringComparison.Ordinal);
        Assert.DoesNotContain($" baseline '{config.ProjectRoot}'", index, StringComparison.Ordinal);
        Assert.Contains("No analysis report files were created.", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SeparatesChangedViewAndLinksRelatedFindingsAcrossAnalyses()
    {
        using var temp = TestTempDirectory.Create();
        var changedAnalysis = new ReportAnalysis("alpha-analysis", "Alpha", "active");
        var changedRelatedAnalysis = new ReportAnalysis("beta-analysis", "Beta", "active");
        var unchangedRelatedAnalysis = new ReportAnalysis("gamma-analysis", "Gamma", "active");
        var config = CreateConfig(temp.DirectoryPath, changedAnalysis, changedRelatedAnalysis, unchangedRelatedAnalysis);
        var symbol = new FindingSymbol("Sample/Sample.csproj", "Sample.cs", "M:Sample.Run", 3);
        var changedDraft = new FindingDraft("Sample/Sample.csproj", "Sample.cs", "M:Sample.Run", "complexity", 3, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Sample.cs", 3, "Source", "detail", "Run")], [symbol]);
        var unchangedDraft = new FindingDraft("Sample/Sample.csproj", "Other.cs", "M:Sample.Run", "dead-code", 8, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Other.cs", 8, "Source", "detail", "Run")], [symbol]);
        var outsideDraft = new FindingDraft("Sample/Sample.csproj", "Outside.cs", "M:Sample.Run", "outside", 11, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Outside.cs", 11, "Source", "detail", "Run")], [symbol]);
        var changedFinding = new ReviewFinding("alpha-analysis", changedDraft, ["Sample.cs"],
            [
                new ReviewFindingReference("beta-analysis", unchangedDraft.ProjectPath, unchangedDraft.SourcePath, unchangedDraft.SubjectId,
                    unchangedDraft.Discriminator, "Sample.cs", "M:Sample.Run", 3),
                new ReviewFindingReference("gamma-analysis", outsideDraft.ProjectPath, outsideDraft.SourcePath, outsideDraft.SubjectId,
                    outsideDraft.Discriminator, "Sample.cs", "M:Sample.Run", 3),
            ], ["Sample.cs"]);
        var changedRelatedFinding = new ReviewFinding("beta-analysis", unchangedDraft, ["Other.cs"], [], ["Sample.cs"]);
        var outsideFinding = new ReviewFinding("gamma-analysis", outsideDraft, ["Outside.cs"], [], []);
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult("alpha-analysis", new ReviewAnalysisResult([changedDraft])),
            new ReviewAnalysisRunResult("beta-analysis", new ReviewAnalysisResult([unchangedDraft])),
            new ReviewAnalysisRunResult("gamma-analysis", new ReviewAnalysisResult([outsideDraft])),
        ]) { Findings = [changedFinding, changedRelatedFinding, outsideFinding] };

        var report = await new MarkdownReportWriter().WriteAsync(config, result,
            configurationPath: Path.Combine(temp.DirectoryPath, "target project", "ainetreview.json"));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var changedIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "changed-files", "index.md"));
        var completeIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "index.md"));
        var changedReportPath = Path.Combine(runDirectory, "changed-files", "alpha-analysis.md");
        var changedReport = await File.ReadAllTextAsync(changedReportPath);
        var completeReportPath = Path.Combine(runDirectory, "all-findings", "beta-analysis.md");

        Assert.Contains("Alpha (1)", changedIndex, StringComparison.Ordinal);
        Assert.Contains("Beta (1)", changedIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("Gamma", changedIndex, StringComparison.Ordinal);
        Assert.Contains("Alpha (1)", completeIndex, StringComparison.Ordinal);
        Assert.Contains("Beta (1)", completeIndex, StringComparison.Ordinal);
        Assert.Contains("Gamma (1)", completeIndex, StringComparison.Ordinal);
        Assert.True(File.Exists(completeReportPath));
        Assert.Contains("M:Sample.Run", changedReport, StringComparison.Ordinal);
        Assert.Contains("Related: beta-analysis, gamma-analysis (all-findings)", changedReport, StringComparison.Ordinal);
        var completeReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "alpha-analysis.md"));
        Assert.Contains("Related: beta-analysis, gamma-analysis", completeReport, StringComparison.Ordinal);
        Assert.DoesNotContain("(all-findings)", completeReport, StringComparison.Ordinal);
        Assert.Contains("`M:Sample.Run` (line 3)", changedReport, StringComparison.Ordinal);
        var completeBeforeEdit = await File.ReadAllTextAsync(completeReportPath);
        await File.WriteAllTextAsync(changedReportPath, changedReport.Replace("M:Sample.Run", "handled", StringComparison.Ordinal));
        Assert.Equal(completeBeforeEdit, await File.ReadAllTextAsync(completeReportPath));

        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        Assert.Contains($" baseline '{config.ProjectRoot}'", index, StringComparison.Ordinal);
    }

    private static ReviewConfig CreateConfig(string root, params IReviewAnalysis[] analyses) => CreateConfig(root, true, analyses);

    private static ReviewConfig CreateConfig(string root, bool enabled, params IReviewAnalysis[] analyses)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var registry = new ReviewAnalysisRegistry(analyses);
        var entries = string.Join(',', analyses.Select(analysis => "\"" + analysis.Descriptor.AnalysisId + "\":{" + (enabled ? string.Empty : "\"enabled\":false") + "}"));
        var config = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{" + entries + "}}";
        return new ReviewConfigValidator(registry).Validate(root, config);
    }

    private static FindingDraft Finding(string path, int line, string subject, string rationale, string snippet, string discriminator) => new(
        "Sample/Sample.csproj", path, subject, discriminator, line, rationale,
        new Dictionary<string, double> { ["zMetric"] = 2, ["aMetric"] = 1 },
        [
            new FindingEvidence(path, line, "B | label", "detail `text`", snippet),
            new FindingEvidence(path, line, "A | label", "earlier evidence", snippet),
        ]);

    private sealed class ReportAnalysis : IReviewAnalysis
    {
        internal ReportAnalysis(string id, string title, string optionValue)
        {
            Descriptor = new ReviewAnalysisDescriptor(id, title, 1, "A test purpose.", "A test measurement.", ["Review this result?"],
                [
                    ReviewAnalysisOptionDescriptor.String("scenario", "Scenario", optionValue),
                    ReviewAnalysisOptionDescriptor.String("zeta", "Last option", "z"),
                    ReviewAnalysisOptionDescriptor.String("alpha", "First option", "a"),
                ]);
        }

        public ReviewAnalysisDescriptor Descriptor { get; }

        public Task<ReviewAnalysisResult> ExecuteAsync(AiNetReview.Core.Analysis.ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(ReviewAnalysisResult.Empty);
    }
}
