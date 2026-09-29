namespace AiNetReview.FastTests.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class SolutionReferenceIndexTests
{
    [Fact]
    public async Task CreateAsync_IndexesProductionTestGeneratedAndMethodGroupReferencesWithProvenance()
    {
        using var fixture = CreateCrossProjectFixture();
        var index = await SolutionReferenceIndex.CreateAsync(fixture.Context);
        var productionWidget = await GetTypeSymbolAsync(fixture.Workspace.CurrentSolution.GetProject(fixture.ProductionProjectId)!, "Widget");
        var targetMethod = productionWidget.GetMembers("Target").OfType<IMethodSymbol>().Single();

        var coverage = index.GetCoverage(targetMethod);
        var methodGroup = Assert.Single(coverage.References.Where(reference =>
            reference.Kind == SolutionSymbolReferenceKind.MethodGroup
            && reference.ProjectRole == SolutionReferenceProjectRole.Test));
        Assert.False(methodGroup.IsGeneratedCode);
        Assert.False(methodGroup.IsSelfReference, $"{methodGroup.DocumentName} in {methodGroup.EnclosingType?.ToDisplayString()}");
        Assert.Equal("Product.Tests", methodGroup.ProjectName);
        Assert.True(methodGroup.SourceSpan.Length > 0);

        var generatedReference = Assert.Single(coverage.References.Where(reference =>
            reference.Kind == SolutionSymbolReferenceKind.Direct && reference.IsGeneratedCode));
        Assert.Equal(SolutionReferenceProjectRole.Production, generatedReference.ProjectRole);
        Assert.Equal("GeneratedConsumer", generatedReference.ProjectName);

        var productionReference = Assert.Single(coverage.References.Where(reference =>
            reference.Kind == SolutionSymbolReferenceKind.Direct
            && reference.ProjectRole == SolutionReferenceProjectRole.Production
            && !reference.IsGeneratedCode));
        Assert.False(productionReference.IsSelfReference);

        var typeCoverage = index.GetCoverage(productionWidget);
        Assert.Contains(typeCoverage.References, reference =>
            reference.Kind == SolutionSymbolReferenceKind.ContainingType
            && reference.IsSelfReference);
        Assert.Contains(typeCoverage.References, reference =>
            reference.Kind == SolutionSymbolReferenceKind.ContainingType
            && !reference.IsSelfReference
            && reference.ProjectRole == SolutionReferenceProjectRole.Test);

        var recursiveMethod = productionWidget.GetMembers("Recursive").OfType<IMethodSymbol>().Single();
        Assert.Contains(index.GetCoverage(recursiveMethod).References, reference =>
            reference.Kind == SolutionSymbolReferenceKind.Direct && reference.IsSelfReference);

        var orphanMethod = productionWidget.GetMembers("Orphan").OfType<IMethodSymbol>().Single();
        var orphanCoverage = index.GetCoverage(orphanMethod);
        Assert.Empty(orphanCoverage.References);
        Assert.False(orphanCoverage.HasUnresolvedBindings);
    }

    [Fact]
    public async Task CreateAsync_ReportsSymbolLocalBindingUncertaintySeparatelyFromKnownReferences()
    {
        using var fixture = CreateProjectFixture("Uncertain", """
            public class Example
            {
                public void Candidate(string value) { }
                public void Use() { Candidate(42); }
            }
            """);

        var index = await SolutionReferenceIndex.CreateAsync(fixture.Context);
        var example = await GetTypeSymbolAsync(fixture.Workspace.CurrentSolution.GetProject(fixture.ProjectId)!, "Example");
        var candidate = example.GetMembers("Candidate").OfType<IMethodSymbol>().Single();
        var coverage = index.GetCoverage(candidate);

        Assert.True(coverage.HasUnresolvedBindings);
        Assert.Empty(coverage.References);
        var useCoverage = index.GetCoverage(example.GetMembers("Use").OfType<IMethodSymbol>().Single());
        Assert.False(useCoverage.HasUnresolvedBindings);
    }

    [Fact]
    public async Task CreateAsync_FailsWhenSolutionHasNoCSharpReferenceCoverage()
    {
        using var workspace = new AdhocWorkspace();
        using var root = TestTempDirectory.Create();
        var context = new ReviewContext(workspace.CurrentSolution, root.DirectoryPath);

        var exception = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => SolutionReferenceIndex.CreateAsync(context));

        Assert.Contains("no C# projects", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_DoesNotChangeExistingReviewAnalysisResults()
    {
        using var fixture = CreateProjectFixture("Ordinary", """
            public class Example
            {
                public int Decide(int value)
                {
                    if (value > 0) { value++; }
                    if (value > 1) { value++; }
                    if (value > 2) { value++; }
                    if (value > 3) { value++; }
                    if (value > 4) { value++; }
                    if (value > 5) { value++; }
                    if (value > 6) { value++; }
                    if (value > 7) { value++; }
                    return value;
                }
            }
            """);
        var analysis = new MethodControlFlowOutliersAnalysis();
        var options = analysis.Descriptor.ResolveOptions();
        var before = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        _ = await SolutionReferenceIndex.CreateAsync(fixture.Context);
        var after = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.Equal(before.Findings.Count, after.Findings.Count);
        for (var index = 0; index < before.Findings.Count; index++)
        {
            var beforeFinding = before.Findings[index];
            var afterFinding = after.Findings[index];
            Assert.Equal(beforeFinding.ProjectPath, afterFinding.ProjectPath);
            Assert.Equal(beforeFinding.SourcePath, afterFinding.SourcePath);
            Assert.Equal(beforeFinding.SubjectId, afterFinding.SubjectId);
            Assert.Equal(beforeFinding.Discriminator, afterFinding.Discriminator);
            Assert.Equal(beforeFinding.StartLine, afterFinding.StartLine);
            Assert.Equal(beforeFinding.Rationale, afterFinding.Rationale);
            Assert.Equal(beforeFinding.Evidence, afterFinding.Evidence);
            Assert.Equal(beforeFinding.Metrics.OrderBy(static pair => pair.Key), afterFinding.Metrics.OrderBy(static pair => pair.Key));
        }
    }

    private static ProjectFixture CreateProjectFixture(string projectName, string source)
    {
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
        var projectId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp,
            filePath: Path.Combine(root.DirectoryPath, projectName + ".csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            metadataReferences: PlatformReferences()));
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            "Source.cs",
            filePath: Path.Combine(root.DirectoryPath, "Source.cs"),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));
        return new ProjectFixture(workspace, new ReviewContext(workspace.CurrentSolution, root.DirectoryPath), projectId, root);
    }

    private static CrossProjectFixture CreateCrossProjectFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
        var productionProjectId = ProjectId.CreateNewId();
        var testProjectId = ProjectId.CreateNewId();
        var generatedProjectId = ProjectId.CreateNewId();
        workspace.AddProject(CreateProjectInfo(productionProjectId, "Production", root.DirectoryPath));
        workspace.AddProject(CreateProjectInfo(testProjectId, "Product.Tests", root.DirectoryPath));
        workspace.AddProject(CreateProjectInfo(generatedProjectId, "GeneratedConsumer", root.DirectoryPath));
        var solution = workspace.CurrentSolution
            .AddProjectReference(testProjectId, new ProjectReference(productionProjectId))
            .AddProjectReference(generatedProjectId, new ProjectReference(productionProjectId));
        Assert.True(workspace.TryApplyChanges(solution));
        AddDocument(workspace, productionProjectId, root.DirectoryPath, "Production.cs", """
            namespace Product;
            public class Widget
            {
                public static void Target() { }
                public static void CallsTarget() { Target(); }
                public static void Recursive() { Recursive(); }
                public static void Orphan() { }
            }
            """);
        AddDocument(workspace, testProjectId, root.DirectoryPath, "WidgetTests.cs", """
            using System;
            using Product;
            public class WidgetTests { private readonly Action callback = Widget.Target; }
            """);
        AddDocument(workspace, generatedProjectId, root.DirectoryPath, "Widget.g.cs", """
            using Product;
            public class GeneratedConsumer { public void Use() { Widget.Target(); } }
            """);

        return new CrossProjectFixture(
            workspace,
            new ReviewContext(workspace.CurrentSolution, root.DirectoryPath),
            productionProjectId,
            root);
    }

    private static ProjectInfo CreateProjectInfo(ProjectId projectId, string name, string root) => ProjectInfo.Create(
        projectId,
        VersionStamp.Create(),
        name,
        name,
        LanguageNames.CSharp,
        filePath: Path.Combine(root, name + ".csproj"),
        compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
        parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
        metadataReferences: PlatformReferences());

    private static void AddDocument(AdhocWorkspace workspace, ProjectId projectId, string root, string name, string source) =>
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name,
            filePath: Path.Combine(root, name),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));

    private static async Task<INamedTypeSymbol> GetTypeSymbolAsync(Project project, string name)
    {
        var compilation = await project.GetCompilationAsync();
        return compilation!.GetTypeByMetadataName(name == "Widget" ? "Product.Widget" : name)!;
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(static assemblyPath =>
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            return !assemblyName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("mstest", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("microsoft.testplatform", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("microsoft.visualstudio.testplatform", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("microsoft.visualstudio.testtools.unittesting", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("microsoft.testing", StringComparison.OrdinalIgnoreCase);
        })
        .Select(static assemblyPath => MetadataReference.CreateFromFile(assemblyPath));

    private sealed class ProjectFixture : IDisposable
    {
        private readonly AdhocWorkspace workspace;
        private readonly IDisposable root;

        public ProjectFixture(AdhocWorkspace workspace, ReviewContext context, ProjectId projectId, IDisposable root)
        {
            this.workspace = workspace;
            this.root = root;
            Context = context;
            ProjectId = projectId;
        }

        public AdhocWorkspace Workspace => workspace;

        public ReviewContext Context { get; }

        public ProjectId ProjectId { get; }

        public void Dispose()
        {
            workspace.Dispose();
            root.Dispose();
        }
    }

    private sealed class CrossProjectFixture : IDisposable
    {
        private readonly AdhocWorkspace workspace;
        private readonly IDisposable root;

        public CrossProjectFixture(AdhocWorkspace workspace, ReviewContext context, ProjectId productionProjectId, IDisposable root)
        {
            this.workspace = workspace;
            this.root = root;
            Context = context;
            ProductionProjectId = productionProjectId;
        }

        public AdhocWorkspace Workspace => workspace;

        public ReviewContext Context { get; }

        public ProjectId ProductionProjectId { get; }

        public void Dispose()
        {
            workspace.Dispose();
            root.Dispose();
        }
    }
}
