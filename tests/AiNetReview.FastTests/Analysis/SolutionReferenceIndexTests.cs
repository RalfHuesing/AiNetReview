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
        var productionWidget = await GetTypeSymbolAsync(fixture.Workspace.Solution.GetProject(fixture.ProductionProjectId)!, "Widget");
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
        var example = await GetTypeSymbolAsync(fixture.Workspace.Solution.GetProject(fixture.ProjectId)!, "Example");
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
        using var workspace = new FastTestWorkspace();
        var context = workspace.CreateReviewContext();

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
        var workspace = new FastTestWorkspace();
        var projectId = workspace.AddProject(projectName);
        workspace.AddDocument(projectId, "Source.cs", source);
        return new ProjectFixture(workspace, workspace.CreateReviewContext(), projectId);
    }

    private static CrossProjectFixture CreateCrossProjectFixture()
    {
        var workspace = new FastTestWorkspace();
        var productionProjectId = ProjectId.CreateNewId();
        var testProjectId = ProjectId.CreateNewId();
        var generatedProjectId = ProjectId.CreateNewId();
        workspace.AddProject("Production", projectId: productionProjectId);
        workspace.AddProject("Product.Tests", projectId: testProjectId);
        workspace.AddProject("GeneratedConsumer", projectId: generatedProjectId);
        workspace.AddProjectReference(testProjectId, new ProjectReference(productionProjectId));
        workspace.AddProjectReference(generatedProjectId, new ProjectReference(productionProjectId));
        workspace.AddDocument(productionProjectId, "Production.cs", """
            namespace Product;
            public class Widget
            {
                public static void Target() { }
                public static void CallsTarget() { Target(); }
                public static void Recursive() { Recursive(); }
                public static void Orphan() { }
            }
            """);
        workspace.AddDocument(testProjectId, "WidgetTests.cs", """
            using System;
            using Product;
            public class WidgetTests { private readonly Action callback = Widget.Target; }
            """);
        workspace.AddDocument(generatedProjectId, "Widget.g.cs", """
            using Product;
            public class GeneratedConsumer { public void Use() { Widget.Target(); } }
            """);

        return new CrossProjectFixture(
            workspace,
            workspace.CreateReviewContext(),
            productionProjectId);
    }

    private static async Task<INamedTypeSymbol> GetTypeSymbolAsync(Project project, string name)
    {
        var compilation = await project.GetCompilationAsync();
        return compilation!.GetTypeByMetadataName(name == "Widget" ? "Product.Widget" : name)!;
    }

    private sealed class ProjectFixture : IDisposable
    {
        private readonly FastTestWorkspace workspace;

        public ProjectFixture(FastTestWorkspace workspace, ReviewContext context, ProjectId projectId)
        {
            this.workspace = workspace;
            Context = context;
            ProjectId = projectId;
        }

        public FastTestWorkspace Workspace => workspace;

        public ReviewContext Context { get; }

        public ProjectId ProjectId { get; }

        public void Dispose()
        {
            workspace.Dispose();
        }
    }

    private sealed class CrossProjectFixture : IDisposable
    {
        private readonly FastTestWorkspace workspace;

        public CrossProjectFixture(FastTestWorkspace workspace, ReviewContext context, ProjectId productionProjectId)
        {
            this.workspace = workspace;
            Context = context;
            ProductionProjectId = productionProjectId;
        }

        public FastTestWorkspace Workspace => workspace;

        public ReviewContext Context { get; }

        public ProjectId ProductionProjectId { get; }

        public void Dispose()
        {
            workspace.Dispose();
        }
    }
}
