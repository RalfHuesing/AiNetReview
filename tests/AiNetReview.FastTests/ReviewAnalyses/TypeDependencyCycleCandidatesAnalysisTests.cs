namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.TypeDependencyCycleCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class TypeDependencyCycleCandidatesAnalysisTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsMaximalComponentWithCompleteEvidenceAndStableRealCycle()
    {
        using var fixture = CreateFixture(
            ("A.cs", "namespace Sample; public partial class A { public B? Value; }"),
            ("A.Part.cs", "namespace Sample; public partial class A { }"),
            ("B.cs", "namespace Sample; public class B { public A? Back; public C? Next; }"),
            ("C.cs", "namespace Sample; public class C { public A? Back; }"),
            ("D.cs", "namespace Sample; public class D { public E? Value; }"),
            ("E.cs", "namespace Sample; public class E { public D? Value; }"),
            ("X.cs", "namespace Sample; public class X { public Y? Value; }"),
            ("Y.cs", "namespace Sample; public class Y { public Z? Value; }"),
            ("Z.cs", "namespace Sample; public class Z { }"));
        var analysis = new TypeDependencyCycleCandidatesAnalysis();

        var first = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        var second = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(first.Findings);
        Assert.Equal("strongly-connected-production-type-group", finding.Discriminator);
        Assert.Equal(3, finding.Metrics["typeCount"]);
        Assert.Equal(4, finding.Metrics["declarationFileCount"]);
        Assert.Equal(4, finding.Metrics["internalEdgeCount"]);
        Assert.Equal(3, finding.SubjectSymbols.Select(static symbol => symbol.SymbolId).Distinct().Count());
        Assert.Equal(new[] { "A.Part.cs", "A.cs", "B.cs", "C.cs" }, finding.Evidence.Where(static item => item.Detail.StartsWith("Declaration", StringComparison.Ordinal))
            .Select(static item => item.SourcePath).Distinct().OrderBy(static path => path, StringComparer.Ordinal));
        Assert.Equal(4, finding.SubjectSymbols.Count);
        var edgeLabels = finding.Evidence.Where(static item => item.Detail.Contains("dependency in project", StringComparison.Ordinal))
            .Select(static item => item.Label).ToArray();
        Assert.Equal(4, edgeLabels.Length);
        Assert.Contains(edgeLabels, static label => label.Contains("T:Sample.A -> Product.csproj::T:Sample.B", StringComparison.Ordinal));
        Assert.Contains(edgeLabels, static label => label.Contains("T:Sample.B -> Product.csproj::T:Sample.C", StringComparison.Ordinal));
        Assert.Contains(edgeLabels, static label => label.Contains("T:Sample.C -> Product.csproj::T:Sample.A", StringComparison.Ordinal));
        Assert.Contains("Example: A -> B -> A", finding.Rationale, StringComparison.Ordinal);
        Assert.Equal(Describe(finding), Describe(Assert.Single(second.Findings)));
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesInclusiveTypeAndDistinctFileFloors()
    {
        using var twoTypes = CreateFixture(
            ("A.cs", "namespace Sample; public class A { public B? Value; }"),
            ("B.cs", "namespace Sample; public class B { public A? Value; }"));
        using var oneFile = CreateFixture(("All.cs", "namespace Sample; public class A { public B? Value; } public class B { public C? Value; } public class C { public A? Value; }"));
        using var qualifying = CreateFixture(
            ("A.cs", "namespace Sample; public class A { public B? Value; }"),
            ("B.cs", "namespace Sample; public class B { public C? Value; }"),
            ("C.cs", "namespace Sample; public class C { public A? Value; }"));
        var analysis = new TypeDependencyCycleCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions();

        Assert.Empty((await analysis.ExecuteAsync(twoTypes.Context, options, CancellationToken.None)).Findings);
        Assert.Empty((await analysis.ExecuteAsync(oneFile.Context, options, CancellationToken.None)).Findings);
        Assert.Single((await analysis.ExecuteAsync(qualifying.Context, options, CancellationToken.None)).Findings);
    }

    [Fact]
    public void Descriptor_IsEnabledByDefaultAndUsesOnlyStandardEnablement()
    {
        var descriptor = new TypeDependencyCycleCandidatesAnalysis().Descriptor;

        Assert.Equal("type-dependency-cycle-candidates", descriptor.AnalysisId);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Empty(descriptor.Options);
    }

    [Fact]
    public void Selector_CountsLinkedCanonicalPathsOnceAcrossProjectSpecificTypes()
    {
        var compilation = CSharpCompilation.Create("Linked", [CSharpSyntaxTree.ParseText("namespace Sample; public class A { } public class B { } public class C { }")],
            FastTestReferences.CreatePlatformReferences());
        var projectA = ProjectId.CreateNewId();
        var projectB = ProjectId.CreateNewId();
        var projectC = ProjectId.CreateNewId();
        var symbols = new[] { "A", "B", "C" }.Select(name => (INamedTypeSymbol)compilation.GetTypeByMetadataName("Sample." + name)!).ToArray();
        var a = new TypeDependencyNode(symbols[0], projectA, "A", "A.csproj", false,
            [new TypeDependencyDeclarationLocation("Shared.cs", default, projectA, "A")]);
        var b = new TypeDependencyNode(symbols[1], projectB, "B", "B.csproj", false,
            [new TypeDependencyDeclarationLocation("B.cs", default, projectB, "B")]);
        var c = new TypeDependencyNode(symbols[2], projectC, "C", "C.csproj", false,
            [new TypeDependencyDeclarationLocation("C.cs", default, projectC, "C")]);
        var linkedProject = ProjectId.CreateNewId();
        var linkedA = new TypeDependencyNode(symbols[0], linkedProject, "LinkedA", "LinkedA.csproj", false,
            [new TypeDependencyDeclarationLocation("Shared.cs", default, linkedProject, "LinkedA")]);
        var graph = new TypeDependencyGraph([a, b, c, linkedA], [Edge(a, b), Edge(b, c), Edge(c, linkedA), Edge(linkedA, a)]);

        var component = Assert.Single(TypeDependencyCycleSelector.Select(graph, CancellationToken.None));

        Assert.Equal(3, component.DeclarationFileCount);
        Assert.Equal(4, component.Nodes.Count);
        Assert.NotEqual(a.ProjectId, b.ProjectId);
        Assert.NotEqual(b.ProjectId, c.ProjectId);
    }

    [Fact]
    public async Task ExecuteAsync_ObservesCancellation()
    {
        using var fixture = CreateFixture(("A.cs", "namespace Sample; public class A { }"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var analysis = new TypeDependencyCycleCandidatesAnalysis();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis.ExecuteAsync(
            fixture.Context, analysis.Descriptor.ResolveOptions(), cancellation.Token));
    }

    private static string Describe(AiNetReview.Core.Findings.FindingDraft finding) =>
        $"{finding.ProjectPath}|{finding.SourcePath}|{finding.SubjectId}|{string.Join(';', finding.Evidence.Select(static item => item.SourcePath + ':' + item.Line + ':' + item.Label))}";

    private static TypeDependencyEdge Edge(TypeDependencyNode from, TypeDependencyNode to) => new(from, to,
        [new TypeDependencyWitness(TypeDependencyEvidenceKind.MemberUse, from.ProjectId, from.ProjectName,
            from.ProjectName + ".csproj", from.Declarations[0].SourcePath, default)]);

    private static AnalysisFixture CreateFixture(params (string File, string Source)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyCycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Product", "Product", LanguageNames.CSharp,
            filePath: Path.Combine(root, "Product.csproj"), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview), metadataReferences: FastTestReferences.CreatePlatformReferences()));
        foreach (var document in documents)
        {
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(projectId), document.File,
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

    private sealed class AnalysisFixture(AdhocWorkspace workspace, ReviewContext context, string root) : IDisposable
    {
        public ReviewContext Context { get; } = context;
        public void Dispose() { workspace.Dispose(); Directory.Delete(root, recursive: true); }
    }
}
