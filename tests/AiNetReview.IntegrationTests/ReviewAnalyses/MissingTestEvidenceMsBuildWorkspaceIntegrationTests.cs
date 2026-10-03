namespace AiNetReview.IntegrationTests.ReviewAnalyses;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

public sealed class MissingTestEvidenceMsBuildWorkspaceIntegrationTests
{
    [Fact]
    public async Task BuildAsync_MapsFastTestInvocationToCoreMethodAcrossProjectCompilations()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        using var workspace = MSBuildWorkspace.Create();
        var solutionPath = FindRepositorySolution();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: CancellationToken.None);
        var coreProject = Assert.Single(solution.Projects, static project => project.Name == "AiNetReview.Core");
        var fastTestsProject = Assert.Single(solution.Projects, static project => project.Name == "AiNetReview.FastTests");
        solution = AddCrossCompilationBoundaryFixtures(solution, coreProject.Id, fastTestsProject.Id, coreProject.MetadataReferences);

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(solution, CancellationToken.None);
        foreach (var projectName in new[] { "AiNetReview.Core", "AiNetReview.FastTests", "ExternalMetadata.Tests" })
        {
            var compilation = await Assert.Single(solution.Projects, project => project.Name == projectName)
                .GetCompilationAsync(CancellationToken.None);
            Assert.NotNull(compilation);
            Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        var target = Assert.Single(graph.Nodes, static node => node.ProjectName == "AiNetReview.Core"
            && DocumentationCommentId.CreateDeclarationId(node.Method)
                == "M:AiNetReview.Core.Analysis.CodeLineMetrics.CountExecutableDeclaration(Microsoft.CodeAnalysis.SyntaxNode)~System.Int32");
        var testMethods = graph.Roots.Where(static root => root.ProjectName == "AiNetReview.FastTests"
                && root.Method.Name.StartsWith("CountExecutableDeclaration_", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(testMethods);
        Assert.All(testMethods, test => Assert.Contains(graph.Edges, edge =>
            SymbolEqualityComparer.Default.Equals(edge.From, test.Method)
            && SymbolEqualityComparer.Default.Equals(edge.To, target.Method)));
        Assert.Equal(MissingTestEvidencePathKind.Direct,
            MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None)[target.Method].Kind);

        var tokenCountOverloads = graph.Nodes.Where(static node => node.ProjectName == "AiNetReview.Core"
            && node.Method.Name == "CountTokenStartLines").ToArray();
        Assert.Equal(2, tokenCountOverloads.Length);
        Assert.NotEqual(
            DocumentationCommentId.CreateDeclarationId(tokenCountOverloads[0].Method),
            DocumentationCommentId.CreateDeclarationId(tokenCountOverloads[1].Method));
        var syntaxNodeOverload = Assert.Single(tokenCountOverloads, static node =>
            node.Method.Parameters.Single().Type.ToDisplayString() == "Microsoft.CodeAnalysis.SyntaxNode");
        var enumerableOverload = Assert.Single(tokenCountOverloads.Where(node =>
            !SymbolEqualityComparer.Default.Equals(node.Method, syntaxNodeOverload.Method)));
        Assert.Contains(graph.Edges, edge => testMethods.Any(test => SymbolEqualityComparer.Default.Equals(edge.From, test.Method))
            && SymbolEqualityComparer.Default.Equals(edge.To, syntaxNodeOverload.Method));
        Assert.DoesNotContain(graph.Edges, edge => testMethods.Any(test => SymbolEqualityComparer.Default.Equals(edge.From, test.Method))
            && SymbolEqualityComparer.Default.Equals(edge.To, enumerableOverload.Method));

        var genericTarget = Assert.Single(graph.Nodes, static node => node.ProjectName == "AiNetReview.Core"
            && node.Method.ContainingType.Name == "CrossCompilationGenericProbe"
            && node.Method.Name == "Transform"
            && node.Method.Arity == 1);
        var genericCall = await GetInvocationTargetAsync(
            Assert.Single(solution.Projects, project => project.Id == fastTestsProject.Id),
            "CrossCompilationGenericProbeTests.cs",
            "Transform");
        Assert.False(SymbolEqualityComparer.Default.Equals(genericCall, genericTarget.Method));
        Assert.Equal(genericTarget.Method.ContainingAssembly.Identity, genericCall.ContainingAssembly.Identity);
        Assert.Equal(
            DocumentationCommentId.CreateDeclarationId(genericTarget.Method),
            DocumentationCommentId.CreateDeclarationId(genericCall.OriginalDefinition));
        Assert.NotEmpty(genericCall.DeclaringSyntaxReferences);
        var genericRoot = Assert.Single(graph.Roots, static node => node.ProjectName == "AiNetReview.FastTests"
            && node.Method.Name == "GenericTargetIsReachable");
        Assert.Contains(graph.Edges, edge => SymbolEqualityComparer.Default.Equals(edge.From, genericRoot.Method)
            && SymbolEqualityComparer.Default.Equals(edge.To, genericTarget.Method));

        var metadataRoot = Assert.Single(graph.Nodes, static node => node.ProjectName == "ExternalMetadata.Tests"
            && node.Method.Name == "CallsMetadataTarget");
        var metadataProject = Assert.Single(solution.Projects, static project => project.Name == "ExternalMetadata.Tests");
        var metadataCall = await GetInvocationTargetAsync(metadataProject, "MetadataCalls.cs", "CountExecutableDeclaration");
        Assert.Empty(metadataCall.DeclaringSyntaxReferences);
        Assert.Contains(metadataCall.Locations, static location => location.Kind == LocationKind.MetadataFile);
        Assert.DoesNotContain(graph.Edges, edge => SymbolEqualityComparer.Default.Equals(edge.From, metadataRoot.Method)
            && SymbolEqualityComparer.Default.Equals(edge.To, target.Method));

        var targetDocument = Assert.Single(coreProject.Documents, static document => document.Name == "CodeLineMetrics.cs");
        var duplicateProjectId = ProjectId.CreateNewId();
        var duplicateProjectPath = Path.Combine(Path.GetTempPath(), "AiNetReview-F001-Ambiguous.Core.csproj");
        var coreVersion = (await coreProject.GetCompilationAsync(CancellationToken.None))!.Assembly.Identity.Version;
        var ambiguousSolution = solution.AddProject(ProjectInfo.Create(
            duplicateProjectId,
            VersionStamp.Create(),
            "AiNetReview.Core",
            "AiNetReview.Core",
            LanguageNames.CSharp,
            filePath: duplicateProjectPath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: coreProject.MetadataReferences));
        ambiguousSolution = ambiguousSolution.AddDocument(
            DocumentId.CreateNewId(duplicateProjectId),
            targetDocument.Name,
            await targetDocument.GetTextAsync(CancellationToken.None),
            filePath: targetDocument.FilePath);
        ambiguousSolution = ambiguousSolution.AddDocument(
            DocumentId.CreateNewId(duplicateProjectId),
            "AssemblyIdentity.cs",
            SourceText.From($"[assembly: System.Reflection.AssemblyVersion(\"{coreVersion}\") ]"),
            filePath: Path.Combine(Path.GetTempPath(), "AiNetReview-F001-Ambiguous-AssemblyIdentity.cs"));
        var ambiguousGraph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(ambiguousSolution, CancellationToken.None);
        var ambiguousTargets = ambiguousGraph.Nodes.Where(node => node.ProjectName == "AiNetReview.Core"
                && DocumentationCommentId.CreateDeclarationId(node.Method)
                    == "M:AiNetReview.Core.Analysis.CodeLineMetrics.CountExecutableDeclaration(Microsoft.CodeAnalysis.SyntaxNode)~System.Int32"
                && StringComparer.OrdinalIgnoreCase.Equals(node.FilePath, target.FilePath))
            .ToArray();
        Assert.Equal(2, ambiguousTargets.Length);
        Assert.Equal(1, ambiguousTargets.Select(static node => node.Method.ContainingAssembly.Identity).Distinct().Count());
        Assert.All(ambiguousTargets.Skip(1), other => Assert.False(
            SymbolEqualityComparer.Default.Equals(ambiguousTargets[0].Method, other.Method)));
        var ambiguousTestMethods = ambiguousGraph.Roots.Where(static root => root.ProjectName == "AiNetReview.FastTests"
                && root.Method.Name.StartsWith("CountExecutableDeclaration_", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(ambiguousTestMethods);
        Assert.DoesNotContain(ambiguousGraph.Edges, edge => ambiguousTestMethods.Any(test =>
                SymbolEqualityComparer.Default.Equals(edge.From, test.Method))
            && ambiguousTargets.Any(candidate => SymbolEqualityComparer.Default.Equals(edge.To, candidate.Method)));
    }

    private static async Task<IMethodSymbol> GetInvocationTargetAsync(Project project, string documentName, string methodName)
    {
        var document = Assert.Single(project.Documents, candidate => candidate.Name == documentName);
        var root = await document.GetSyntaxRootAsync(CancellationToken.None);
        var semanticModel = await document.GetSemanticModelAsync(CancellationToken.None);
        Assert.NotNull(root);
        Assert.NotNull(semanticModel);
        var invocation = Assert.Single(root.DescendantNodes().OfType<InvocationExpressionSyntax>(), syntax =>
            semanticModel.GetSymbolInfo(syntax, CancellationToken.None).Symbol is IMethodSymbol method
            && method.Name == methodName);
        return Assert.IsAssignableFrom<IMethodSymbol>(semanticModel.GetSymbolInfo(invocation, CancellationToken.None).Symbol);
    }

    private static Solution AddCrossCompilationBoundaryFixtures(
        Solution solution,
        ProjectId coreProjectId,
        ProjectId fastTestsProjectId,
        IReadOnlyList<MetadataReference> coreMetadataReferences)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "AiNetReview-F001-" + Guid.NewGuid().ToString("N"));
        solution = solution.AddDocument(
            DocumentId.CreateNewId(coreProjectId),
            "CrossCompilationGenericProbe.Part1.cs",
            SourceText.From("namespace AiNetReview.Core.Analysis; public static partial class CrossCompilationGenericProbe { public static partial T Transform<T>(T value); }"),
            filePath: Path.Combine(temporaryRoot, "Core", "CrossCompilationGenericProbe.Part1.cs"));
        solution = solution.AddDocument(
            DocumentId.CreateNewId(coreProjectId),
            "CrossCompilationGenericProbe.Part2.cs",
            SourceText.From("namespace AiNetReview.Core.Analysis; public static partial class CrossCompilationGenericProbe { public static partial T Transform<T>(T value) => value; }"),
            filePath: Path.Combine(temporaryRoot, "Core", "CrossCompilationGenericProbe.Part2.cs"));
        solution = solution.AddDocument(
            DocumentId.CreateNewId(fastTestsProjectId),
            "CrossCompilationGenericProbeTests.cs",
            SourceText.From("using Xunit; using AiNetReview.Core.Analysis; public sealed class CrossCompilationGenericProbeTests { [Fact] public void GenericTargetIsReachable() { _ = CrossCompilationGenericProbe.Transform<int>(42); } }"),
            filePath: Path.Combine(temporaryRoot, "FastTests", "CrossCompilationGenericProbeTests.cs"));

        var externalProjectId = ProjectId.CreateNewId();
        var coreAssemblyPath = Path.Combine(AppContext.BaseDirectory, "AiNetReview.Core.dll");
        if (!File.Exists(coreAssemblyPath))
        {
            throw new FileNotFoundException("The integration test output does not contain the Core assembly needed for the metadata-boundary case.", coreAssemblyPath);
        }

        solution = solution.AddProject(ProjectInfo.Create(
            externalProjectId,
            VersionStamp.Create(),
            "ExternalMetadata.Tests",
            "ExternalMetadata.Tests",
            LanguageNames.CSharp,
            filePath: Path.Combine(temporaryRoot, "ExternalMetadata.Tests.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: coreMetadataReferences.Append(MetadataReference.CreateFromFile(coreAssemblyPath))));
        return solution.AddDocument(
            DocumentId.CreateNewId(externalProjectId),
            "MetadataCalls.cs",
            SourceText.From("public sealed class MetadataCalls { public int CallsMetadataTarget() => AiNetReview.Core.Analysis.CodeLineMetrics.CountExecutableDeclaration(null!); }"),
            filePath: Path.Combine(temporaryRoot, "ExternalMetadata.Tests", "MetadataCalls.cs"));
    }

    private static string FindRepositorySolution()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var solutionPath = Path.Combine(directory.FullName, "AiNetReview.slnx");
            if (File.Exists(solutionPath)
                && Directory.Exists(Path.Combine(directory.FullName, "tests", "AiNetReview.FastTests")))
            {
                return solutionPath;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the AiNetReview.slnx repository solution from the test output directory.");
    }
}
