namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class MissingTestEvidenceCandidatesAnalysisTests
{
    [Fact]
    public void Descriptor_UsesExactIdentityDefaultsAndPositiveInt32Options()
    {
        var descriptor = new MissingTestEvidenceCandidatesAnalysis().Descriptor;

        Assert.Equal("missing-test-evidence-candidates", descriptor.AnalysisId);
        Assert.Equal(1, descriptor.BehaviorVersion);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Equal(
            new[] { "minDecisionCount", "minDecisionNesting", "minIndirectDecisionCount", "minIndirectDecisionNesting" },
            descriptor.Options.Select(static option => option.Name));
        Assert.Equal(new[] { 3, 2, 5, 3 }, descriptor.Options.Select(static option => option.DefaultValue.GetInt32()));
        Assert.Equal(new[] { 3, 2, 5, 3 }, descriptor.ResolveOptions().Values.Values.Select(static value => value.GetInt32()));

        foreach (var option in descriptor.Options)
        {
            Assert.True(option.IsValidValue(JsonSerializer.SerializeToElement(1)));
            Assert.True(option.IsValidValue(JsonSerializer.SerializeToElement(int.MaxValue)));
            Assert.False(option.IsValidValue(JsonSerializer.SerializeToElement(0)));
            Assert.False(option.IsValidValue(JsonSerializer.SerializeToElement(-1)));
            Assert.False(option.IsValidValue(JsonSerializer.SerializeToElement(1.5)));
            Assert.False(option.IsValidValue(JsonSerializer.SerializeToElement("3")));
            Assert.False(option.IsValidValue(JsonSerializer.SerializeToElement(true)));
            Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new(option.Name, JsonSerializer.SerializeToElement(0))]));
            Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new(option.Name, JsonSerializer.SerializeToElement(1.5))]));
            Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new(option.Name, JsonSerializer.SerializeToElement("3"))]));
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReportsNoPathAndIndirectOnlyWithEvidenceMetricsAndStableOrder()
    {
        using var fixture = CreateFixture(
            productionSource: """
                namespace Product;
                public sealed class Worker
                {
                    public void IndirectTarget(int value) { if (value > 0) { if (value > 1) { if (value > 2) { if (value > 3) { if (value > 4) { } } } } } }
                    public void Uncovered(int value) { if (value > 0) { } if (value > 1) { } if (value > 2) { } }
                    public void Entry() { IndirectTarget(1); }
                    public void DirectTarget(int value) { if (value > 0) { } if (value > 1) { } if (value > 2) { } if (value > 3) { } if (value > 4) { } }
                }
                """,
            testSource: """
                using Xunit;
                using Product;
                public sealed class Tests
                {
                    [Fact] public void ActiveRoot() { new Worker().Entry(); new Worker().DirectTarget(1); }
                }
                """);
        var analysis = new MissingTestEvidenceCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var noPath = Assert.Single(result.Findings, static finding => finding.SubjectId.Contains("Uncovered", StringComparison.Ordinal));
        Assert.Equal("no-static-test-path", noPath.Discriminator);
        Assert.Contains("no static test path", noPath.Rationale, StringComparison.Ordinal);
        Assert.False(noPath.Rationale.Contains("Attribution uncertain", StringComparison.Ordinal));
        Assert.Equal(3, noPath.Metrics["decisionCount"]);
        Assert.Equal(3, noPath.Metrics["minDecisionCount"]);
        Assert.Equal(2, noPath.Metrics["minDecisionNesting"]);
        Assert.Equal(5, noPath.Metrics["minIndirectDecisionCount"]);
        Assert.Equal(3, noPath.Metrics["minIndirectDecisionNesting"]);
        Assert.Equal(noPath.SubjectId, Assert.Single(noPath.RelatedSymbols).SymbolId);
        Assert.Contains(noPath.Evidence, evidence => evidence.Label == noPath.SubjectId && evidence.SourcePath == noPath.SourcePath);

        var indirect = Assert.Single(result.Findings, static finding => finding.SubjectId.Contains("IndirectTarget", StringComparison.Ordinal));
        Assert.Equal("indirect-test-path-only", indirect.Discriminator);
        Assert.Contains("indirect test path only", indirect.Rationale, StringComparison.Ordinal);
        Assert.Contains("Shortest resolved test path:", indirect.Rationale, StringComparison.Ordinal);
        Assert.Contains("Tests", indirect.Rationale, StringComparison.Ordinal);
        Assert.Contains("Entry", indirect.Rationale, StringComparison.Ordinal);
        Assert.Contains("IndirectTarget", indirect.Rationale, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("DirectTarget", StringComparison.Ordinal));
        Assert.Equal(result.Findings.OrderBy(static finding => finding.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.SourcePath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.StartLine)
            .ThenBy(static finding => finding.SubjectId, StringComparer.Ordinal).Select(static finding => finding.SubjectId),
            result.Findings.Select(static finding => finding.SubjectId));

        var repeated = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        Assert.Equal(result.Findings.Select(static finding => (finding.ProjectPath, finding.SourcePath, finding.SubjectId, finding.Discriminator, finding.Rationale)),
            repeated.Findings.Select(static finding => (finding.ProjectPath, finding.SourcePath, finding.SubjectId, finding.Discriminator, finding.Rationale)));
        var validated = await new CurrentFindingValidator().ValidateAndSortAsync(
            analysis.Descriptor.AnalysisId, fixture.Context, result.Findings, CancellationToken.None);
        Assert.Equal(result.Findings.Count, validated.Count);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesBothGatesAndMarksUnresolvedAttributionUncertain()
    {
        using var fixture = CreateFixture(
            productionSource: """
                public sealed class Worker
                {
                    public void CountCandidate(int value) { if (value > 0) { } if (value > 1) { } if (value > 2) { } }
                    public void NestingCandidate(int value) { if (value > 0) { if (value > 1) { if (value > 2) { } } } }
                    public void BelowIndirect(int value) { if (value > 0) { } if (value > 1) { } if (value > 2) { } }
                    public void MaybeCovered() { if (true) { } if (true) { } if (true) { } }
                    public void IncludedIndirect(int value) { if (value > 0) { } if (value > 1) { } if (value > 2) { } if (value > 3) { } if (value > 4) { } }
                    public void Entry(int value) { CountCandidate(value); NestingCandidate(value); BelowIndirect(value); MaybeCovered(); IncludedIndirect(value); }
                }
                """,
            testSource: """
                using Xunit;
                public sealed class Tests
                {
                    [Fact] public void ActiveRoot() { new Worker().Entry(1); DoesNotExist(); }
                }
                """);
        var analysis = new MissingTestEvidenceCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions(
        [
            new("minDecisionCount", JsonSerializer.SerializeToElement(3)),
            new("minDecisionNesting", JsonSerializer.SerializeToElement(3)),
            new("minIndirectDecisionCount", JsonSerializer.SerializeToElement(5)),
            new("minIndirectDecisionNesting", JsonSerializer.SerializeToElement(4)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("BelowIndirect", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("IncludedIndirect", StringComparison.Ordinal));
        Assert.All(result.Findings, finding => Assert.Contains("Attribution uncertain", finding.Rationale, StringComparison.Ordinal));
        Assert.All(result.Findings, finding => Assert.Equal(1, finding.Metrics["attributionUncertain"]));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmptyForNoCandidatesAndHonorsCancellation()
    {
        using var fixture = CreateFixture("public sealed class Worker { public void Trivial() { } }", null);
        var analysis = new MissingTestEvidenceCandidatesAnalysis();

        var empty = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(empty.Findings);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), cancellation.Token));
    }

    private static Fixture CreateFixture(string productionSource, string? testSource)
    {
#pragma warning disable CA2000 // Fixture takes ownership and disposes the workspace returned by this helper.
        var workspace = new AdhocWorkspace();
#pragma warning restore CA2000
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-MissingTestEvidenceAnalysis", Guid.NewGuid().ToString("N"));
        var productionId = ProjectId.CreateNewId();
        var references = PlatformReferences().ToArray();
        workspace.AddProject(ProjectInfo.Create(productionId, VersionStamp.Create(), "Example.Core", "Example.Core", LanguageNames.CSharp,
            filePath: Path.Combine(root, "Example.Core.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references));
        AddDocument(workspace, productionId, "Worker.cs", productionSource, Path.Combine(root, "Worker.cs"));

        if (testSource is not null)
        {
            var testId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(testId, VersionStamp.Create(), "Example.Tests", "Example.Tests", LanguageNames.CSharp,
                filePath: Path.Combine(root, "Example.Tests.csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references.Append(TestFrameworkReference.Reference),
                projectReferences: [new ProjectReference(productionId)]));
            AddDocument(workspace, testId, "Tests.cs", testSource, Path.Combine(root, "Tests.cs"));
        }

        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static void AddDocument(AdhocWorkspace workspace, ProjectId projectId, string name, string source, string path) =>
        workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(projectId), name, filePath: path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));

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
                && !name.StartsWith("microsoft.visualstudio.testtools.unittesting", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("microsoft.testing", StringComparison.OrdinalIgnoreCase);
        })
        .Select(static path => MetadataReference.CreateFromFile(path));

    private sealed class Fixture(AdhocWorkspace workspace, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose() => workspace.Dispose();
    }

    private static class TestFrameworkReference
    {
        private const string Source = "using System; namespace Xunit { [AttributeUsage(AttributeTargets.Method)] public sealed class FactAttribute : Attribute { } }";

        public static MetadataReference Reference { get; } = CreateReference();

        private static MetadataReference CreateReference()
        {
            var compilation = CSharpCompilation.Create("xunit.analysis.contracts", [CSharpSyntaxTree.ParseText(Source)], PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var assembly = new MemoryStream();
            var result = compilation.Emit(assembly);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
            }

            return MetadataReference.CreateFromImage(assembly.ToArray(), filePath: "xunit.analysis.contracts.dll");
        }
    }
}
