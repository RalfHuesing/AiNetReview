namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class IndirectionDriftCandidatesAnalysisTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsMaximalPathsInCallOrderWithMetricsAndAllSymbols()
    {
        using var fixture = CreateFixture(
            ("ZRoot.cs", "namespace Sample; public static class ZRoot { public static void Run() => Middle.Run(); }"),
            ("Middle.cs", "namespace Sample; public static class Middle { public static void Run() => Endpoint.Run(); }"),
            ("AEndpoint.cs", "namespace Sample; public static class Endpoint { public static void Run() { } }"));
        var analysis = new IndirectionDriftCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("M:Sample.ZRoot.Run", finding.SubjectId);
        Assert.Equal("transparent-forwarding-path", finding.Discriminator);
        Assert.Equal("ZRoot.cs", finding.SourcePath);
        Assert.Equal(2, finding.Metrics["forwardingEdgeCount"]);
        Assert.Equal(3, finding.Metrics["distinctTypeCount"]);
        Assert.Equal(3, finding.Metrics["distinctFileCount"]);
        Assert.Equal(new[] { "ZRoot.cs", "Middle.cs", "AEndpoint.cs" }, finding.Evidence.Select(static item => item.SourcePath));
        Assert.Equal(new[] { finding.SubjectId, "M:Sample.Middle.Run", "M:Sample.Endpoint.Run" }, finding.Evidence.Select(static item => item.Label));
        Assert.All(finding.Evidence, static item => Assert.False(string.IsNullOrWhiteSpace(item.Snippet)));
        Assert.Equal(new[] { "M:Sample.Endpoint.Run", "M:Sample.Middle.Run", "M:Sample.ZRoot.Run" }, finding.RelatedSymbols.Select(static symbol => symbol.SymbolId));
        Assert.Contains("Ends the statically declared forwarding path", finding.Evidence[^1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsOneFindingPerRootWhenPathsConvergeAndIsStableAcrossRuns()
    {
        using var fixture = CreateFixture(
            ("ZRoot.cs", "namespace Sample; public static class ZRoot { public static void Run() => Shared.Run(); }"),
            ("ARoot.cs", "namespace Sample; public static class ARoot { public static void Run() => Shared.Run(); }"),
            ("Shared.cs", "namespace Sample; public static class Shared { public static void Run() => Middle.Run(); }"),
            ("Middle.cs", "namespace Sample; public static class Middle { public static void Run() => Endpoint.Run(); }"),
            ("Endpoint.cs", "namespace Sample; public static class Endpoint { public static void Run() { } }"));
        var analysis = new IndirectionDriftCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions();

        var first = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);
        var second = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.Equal(new[] { "M:Sample.ARoot.Run", "M:Sample.ZRoot.Run" }, first.Findings.Select(static finding => finding.SubjectId));
        Assert.Equal(first.Findings.Select(Describe), second.Findings.Select(Describe));
        Assert.All(first.Findings, static finding => Assert.Equal(3, finding.Metrics["forwardingEdgeCount"]));
    }

    [Fact]
    public async Task ExecuteAsync_RequiresTwoEdgesThreeTypesAndThreeFiles()
    {
        using var twoEdgesTwoTypes = CreateFixture(
            ("Root.cs", "namespace Sample; public static class Root { public static void Run() => End.Run(); }"),
            ("End.cs", "namespace Sample; public static class End { public static void Run() { } }"));
        using var sameFile = CreateFixture(("All.cs", """
            namespace Sample;
            public static class Root { public static void Run() => Middle.Run(); }
            public static class Middle { public static void Run() => End.Run(); }
            public static class End { public static void Run() { } }
            """));
        using var oneEdge = CreateFixture(
            ("Root.cs", "namespace Sample; public static class Root { public static void Run() => Middle.Run(); }"),
            ("Middle.cs", "namespace Sample; public static class Middle { public static void Run() { } }"));
        var analysis = new IndirectionDriftCandidatesAnalysis();

        Assert.Empty((await analysis.ExecuteAsync(twoEdgesTwoTypes.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
        Assert.Empty((await analysis.ExecuteAsync(sameFile.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
        Assert.Empty((await analysis.ExecuteAsync(oneEdge.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesTestProjectsWhileKeepingForwardingPathsProjectLocal()
    {
        using var fixture = CreateFixture("Product.Tests",
            ("Root.cs", "namespace Xunit { [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class FactAttribute : System.Attribute { public string? Skip { get; set; } } } namespace Sample { public static class Root { [Xunit.Fact(Skip = \"deliberately skipped\")] public static void Run() => Middle.Run(); } }"),
            ("Middle.cs", "namespace Sample; public static class Middle { public static void Run() => Endpoint.Run(); }"),
            ("Endpoint.cs", "namespace Sample; public static class Endpoint { public static void Run() { } }"));
        var analysis = new IndirectionDriftCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal("Product.Tests.csproj", finding.ProjectPath);
        Assert.Equal(2, finding.Metrics["forwardingEdgeCount"]);
        Assert.Equal(new[] { "Root.cs", "Middle.cs", "Endpoint.cs" }, finding.Evidence.Select(static evidence => evidence.SourcePath));
    }

    [Fact]
    public async Task ExecuteAsync_DiscardsCyclesIncludingRootPathsThatEnterACycle()
    {
        using var fixture = CreateFixture(
            ("Root.cs", "namespace Sample; public static class Root { public static void Run() => A.Run(); }"),
            ("A.cs", "namespace Sample; public static class A { public static void Run() => B.Run(); }"),
            ("B.cs", "namespace Sample; public static class B { public static void Run() => A.Run(); }"));
        var analysis = new IndirectionDriftCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_HasNoFindingCap()
    {
        const int pathCount = 128;
        var documents = new List<(string File, string Source)>();
        for (var index = 0; index < pathCount; index++)
        {
            documents.Add(($"A{index:D3}.cs", $"namespace Sample; public static class Root{index:D3} {{ public static void Run() => Middle{index:D3}.Run(); }}"));
            documents.Add(($"B{index:D3}.cs", $"namespace Sample; public static class Middle{index:D3} {{ public static void Run() => End{index:D3}.Run(); }}"));
            documents.Add(($"C{index:D3}.cs", $"namespace Sample; public static class End{index:D3} {{ public static void Run() {{ }} }}"));
        }
        using var fixture = CreateFixture(documents.ToArray());
        var analysis = new IndirectionDriftCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Equal(pathCount, result.Findings.Count);
    }

    [Fact]
    public void Descriptor_HasStableIdentityAndNoOptions()
    {
        var descriptor = new IndirectionDriftCandidatesAnalysis().Descriptor;

        Assert.Equal("indirection-drift-candidates", descriptor.AnalysisId);
        Assert.Equal(2, descriptor.BehaviorVersion);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Empty(descriptor.Options);
        Assert.Contains("statically", descriptor.Measurement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historical", descriptor.Measurement, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(FindingDraft finding) =>
        $"{finding.ProjectPath}|{finding.SourcePath}|{finding.SubjectId}|{finding.Discriminator}|{string.Join(',', finding.Evidence.Select(static item => item.SourcePath + ':' + item.Line))}";

    private static AnalysisFixture CreateFixture(params (string File, string Source)[] documents)
        => CreateFixture("Product", documents);

    private static AnalysisFixture CreateFixture(string projectName, params (string File, string Source)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-Indirection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp,
            filePath: Path.Combine(root, projectName + ".csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            metadataReferences: PlatformReferences()));
        foreach (var document in documents)
        {
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                document.File,
                filePath: Path.Combine(root, document.File),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(document.Source), VersionStamp.Create()))));
        }

        if (!workspace.TryApplyChanges(workspace.CurrentSolution))
        {
            workspace.Dispose();
            Directory.Delete(root, recursive: true);
            throw new InvalidOperationException("Could not initialize Roslyn test workspace.");
        }

        return new AnalysisFixture(workspace, new ReviewContext(workspace.CurrentSolution, root), root);
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

    private sealed class AnalysisFixture(AdhocWorkspace workspace, ReviewContext context, string root) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose()
        {
            workspace.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
