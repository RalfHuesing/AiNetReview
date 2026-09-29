namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class DuplicateCodeCandidatesAnalysisTests
{
    [Fact]
    public void Descriptor_UsesValidatedDefaultsAndRejectsInvalidValues()
    {
        var analysis = new DuplicateCodeCandidatesAnalysis();
        var defaults = analysis.Descriptor.ResolveOptions();

        Assert.Equal(30, defaults["minTokens"].GetInt32());
        Assert.Equal("exact", defaults["minimumSimilarity"].GetString());
        Assert.Equal(new[] { "minTokens", "minimumSimilarity" }, analysis.Descriptor.Options.Select(static option => option.Name));

        AssertInvalidOption(analysis, "minTokens", JsonSerializer.SerializeToElement(0));
        AssertInvalidOption(analysis, "minTokens", JsonSerializer.SerializeToElement(-1));
        AssertInvalidOption(analysis, "minTokens", JsonSerializer.SerializeToElement(1.5));
        AssertInvalidOption(analysis, "minTokens", JsonSerializer.SerializeToElement("30"));
        AssertInvalidOption(analysis, "minimumSimilarity", JsonSerializer.SerializeToElement("Exact"));
        AssertInvalidOption(analysis, "minimumSimilarity", JsonSerializer.SerializeToElement("loose"));
    }

    [Theory]
    [InlineData("exact", 0.95, 2)]
    [InlineData("near", 0.80, 3)]
    [InlineData("fuzzy", 0.65, 4)]
    public async Task ExecuteAsync_ReportsClustersAtSelectedThresholdWithAllMemberEvidence(
        string level,
        double threshold,
        int expectedMembers)
    {
        var exactBody = BuildBody();
        var nearBody = ReplaceStatements(exactBody, (8, "var i = a * 7;"));
        var fuzzyBody = ReplaceStatements(exactBody,
            (1, "var v2 = v1 * 9;"), (6, "var v7 = v6 * 10;"), (11, "var v12 = v11 * 11;"), (17, "var v18 = v17 * 12;"));
        using var fixture = CreateFixture(
            ("ProductA", "A.cs", Wrap("ExactOne", exactBody) + Wrap("ExactTwo", exactBody)),
            ("ProductB", "B.cs", Wrap("Near", nearBody)),
            ("ProductC", "C.cs", Wrap("Fuzzy", fuzzyBody)));
        var analysis = new DuplicateCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions(
        [
            KeyValuePair.Create("minimumSimilarity", JsonSerializer.SerializeToElement(level)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);
        var finding = Assert.Single(result.Findings);

        Assert.Equal(expectedMembers, finding.Metrics["memberCount"]);
        Assert.Equal(threshold, finding.Metrics["minimumSimilarityThreshold"]);
        Assert.InRange(finding.Metrics["similarityScore"], threshold, 1.0);
        Assert.Equal(expectedMembers, finding.Evidence.Count);
        Assert.Contains(level, finding.Rationale, StringComparison.Ordinal);
        Assert.Contains(threshold.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), finding.Rationale, StringComparison.Ordinal);
        Assert.Contains("does not prove", finding.Rationale, StringComparison.Ordinal);
        Assert.All(finding.Evidence, evidence =>
        {
            Assert.False(string.IsNullOrWhiteSpace(evidence.SourcePath));
            Assert.True(evidence.Line > 0);
            Assert.Contains("body tokens", evidence.Detail, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(evidence.Snippet));
        });

        var validated = await new CurrentFindingValidator().ValidateAndSortAsync(
            analysis.Descriptor.AnalysisId,
            fixture.Context,
            result.Findings,
            CancellationToken.None);
        Assert.Single(validated);
        Assert.Contains(finding.Evidence, static item => item.SourcePath == "A.cs");
        if (level is "near" or "fuzzy")
        {
            Assert.Contains(finding.Evidence, static item => item.SourcePath == "B.cs");
        }

        if (level == "fuzzy")
        {
            Assert.Contains(finding.Evidence, static item => item.SourcePath == "C.cs");
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmptyResultWhenNoClusterQualifies()
    {
        using var fixture = CreateFixture(("Product", "Methods.cs", Wrap("First", BuildBody())));
        var analysis = new DuplicateCodeCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Same(ReviewAnalysisResult.Empty, result);
    }

    [Fact]
    public async Task ExecuteAsync_ProducesStableFindingIdentityOrderAndRepresentativeAcrossRuns()
    {
        var body = BuildBody();
        var separateBody = BuildAlternateBody();
        using var fixture = CreateFixture(
            ("ProductA", "A.cs", Wrap("First", body) + Wrap("Second", body)),
            ("ProductB", "B.cs", Wrap("Third", separateBody) + Wrap("Fourth", separateBody)));
        var analysis = new DuplicateCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions();

        var first = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);
        var second = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.Equal(2, first.Findings.Count);
        Assert.Equal(first.Findings.Select(Describe), second.Findings.Select(Describe));
        Assert.Equal("A.cs", first.Findings[0].SourcePath);
        Assert.Equal("B.cs", first.Findings[1].SourcePath);
        Assert.All(first.Findings, static finding => Assert.Equal("duplicate-code-cluster", finding.Discriminator));
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredTokenMinimumAndPropagatesCancellation()
    {
        var shortBody = BuildBoundaryBody(29);
        var longBody = BuildBoundaryBody(30);
        using var fixture = CreateFixture(("Product", "Methods.cs",
            Wrap("ShortOne", shortBody) + Wrap("ShortTwo", shortBody) + Wrap("LongOne", longBody) + Wrap("LongTwo", longBody)));
        var analysis = new DuplicateCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions(
        [
            KeyValuePair.Create("minTokens", JsonSerializer.SerializeToElement(30)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);
        Assert.Equal(2, Assert.Single(result.Findings).Metrics["memberCount"]);
        Assert.All(Assert.Single(result.Findings).Evidence, static evidence => Assert.Contains("Long", evidence.Detail, StringComparison.Ordinal));

        var higherMinimum = analysis.Descriptor.ResolveOptions(
        [
            KeyValuePair.Create("minTokens", JsonSerializer.SerializeToElement(146)),
        ]);
        Assert.Same(ReviewAnalysisResult.Empty, await analysis.ExecuteAsync(fixture.Context, higherMinimum, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis.ExecuteAsync(fixture.Context, options, cancellation.Token));
    }

    private static void AssertInvalidOption(DuplicateCodeCandidatesAnalysis analysis, string name, JsonElement value) =>
        Assert.Throws<ArgumentException>(() => analysis.Descriptor.ResolveOptions([KeyValuePair.Create(name, value)]));

    private static string Describe(FindingDraft finding) =>
        $"{finding.ProjectPath}|{finding.SourcePath}|{finding.SubjectId}|{finding.Discriminator}|{string.Join(',', finding.Evidence.Select(static item => item.SourcePath + ':' + item.Line))}";

    private static string BuildBody() => string.Join(" ", Enumerable.Range(1, 20).Select(index =>
        $"var v{index} = {(index == 1 ? "value" : $"v{index - 1}")} + {index};")) + " return v20;";

    private static string BuildAlternateBody() => string.Join(" ", Enumerable.Range(1, 20).Select(index =>
        $"var w{index} = {(index == 1 ? "value" : $"w{index - 1}")} * {index};")) + " return w20;";

    private static string BuildBoundaryBody(int tokenCount)
    {
        var assignmentCount = tokenCount == 30 ? 5 : 4;
        var statements = string.Join(" ", Enumerable.Range(0, assignmentCount).Select(index => $"int value{index} = {index};"));
        var emptyStatements = tokenCount == 29 ? " ; ; ; ;" : string.Empty;
        return statements + emptyStatements + " return 1;";
    }

    private static string ReplaceStatements(string body, params (int Index, string Replacement)[] replacements)
    {
        var statements = body.Split(" ", StringSplitOptions.RemoveEmptyEntries)
            .Chunk(6)
            .Select(static part => string.Join(" ", part))
            .ToArray();
        foreach (var replacement in replacements)
        {
            statements[replacement.Index] = replacement.Replacement;
        }

        return string.Join(" ", statements);
    }

    private static string Wrap(string typeName, string body) =>
        $"public static class {typeName} {{ public static int Run(int value) {{ {body} }} }}\n";

    private static AnalysisFixture CreateFixture(params (string Project, string File, string Source)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
        var groups = documents.GroupBy(static document => document.Project, StringComparer.Ordinal).ToArray();
        var projectIds = groups.ToDictionary(static group => group.Key, static _ => ProjectId.CreateNewId(), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            workspace.AddProject(ProjectInfo.Create(
                projectIds[group.Key],
                VersionStamp.Create(),
                group.Key,
                group.Key,
                LanguageNames.CSharp,
                filePath: Path.Combine(root.DirectoryPath, group.Key + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: PlatformReferences()));
        }

        foreach (var document in documents)
        {
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectIds[document.Project]),
                document.File,
                filePath: Path.Combine(root.DirectoryPath, document.File),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(document.Source), VersionStamp.Create()))));
        }

        if (!workspace.TryApplyChanges(workspace.CurrentSolution))
        {
            throw new InvalidOperationException("Could not initialize Roslyn test workspace.");
        }

        return new AnalysisFixture(workspace, new ReviewContext(workspace.CurrentSolution, root.DirectoryPath), root);
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(static path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return !name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("mstest", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("microsoft.testplatform", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("microsoft.visualstudio.testplatform", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("microsoft.testing", StringComparison.OrdinalIgnoreCase);
            })
            .Select(static path => MetadataReference.CreateFromFile(path));

    private sealed class AnalysisFixture(AdhocWorkspace workspace, ReviewContext context, IDisposable root) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose()
        {
            workspace.Dispose();
            root.Dispose();
        }
    }
}
