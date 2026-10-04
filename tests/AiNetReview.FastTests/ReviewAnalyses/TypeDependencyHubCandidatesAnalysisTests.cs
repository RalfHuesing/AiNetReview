namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.TypeDependencyHubCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class TypeDependencyHubCandidatesAnalysisTests
{
    [Fact]
    public async Task ExecuteAsync_UsesInclusiveDistinctProductionCountsAndSeparatesTestConsumers()
    {
        using var fixture = CreateFixture(10, 10, addTestConsumer: true);
        var analysis = new TypeDependencyHubCandidatesAnalysis();

        var first = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        var second = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(first.Findings);
        Assert.Equal("production-type-dependency-hub", finding.Discriminator);
        Assert.Equal(10, finding.Metrics["fanIn"]);
        Assert.Equal(10, finding.Metrics["fanOut"]);
        Assert.Equal(10, finding.Metrics["minFanIn"]);
        Assert.Equal(10, finding.Metrics["minFanOut"]);
        Assert.Equal(10, finding.Metrics["fanInNeighborFileCount"]);
        Assert.Equal(10, finding.Metrics["fanOutNeighborFileCount"]);
        Assert.Equal(1, finding.Metrics["fanInNeighborProjectCount"]);
        Assert.Equal(1, finding.Metrics["fanOutNeighborProjectCount"]);
        Assert.Equal(1, finding.Metrics["testConsumerCount"]);
        Assert.Equal(2, finding.Evidence.Count(static item => item.Detail.StartsWith("Production type declaration", StringComparison.Ordinal)));
        Assert.Equal(10, finding.Evidence.Count(static item => item.Detail.StartsWith("Production consumer;", StringComparison.Ordinal)));
        Assert.Equal(10, finding.Evidence.Count(static item => item.Detail.StartsWith("Production dependency;", StringComparison.Ordinal)));
        Assert.Single(finding.Evidence, static item => item.Detail.StartsWith("Direct test consumer;", StringComparison.Ordinal));
        Assert.Equal(1, finding.SubjectSymbols.Select(static symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, finding.SubjectSymbols.Count);
        Assert.All(finding.SubjectSymbols, static symbol => Assert.Contains("Hub", symbol.SymbolId, StringComparison.Ordinal));
        Assert.Contains(finding.RelatedSymbols, static symbol => symbol.SymbolId.Contains("Consumer0", StringComparison.Ordinal));
        Assert.Contains(finding.RelatedSymbols, static symbol => symbol.SymbolId.Contains("Dependency0", StringComparison.Ordinal));
        Assert.Contains(finding.RelatedSymbols, static symbol => symbol.SymbolId.Contains("Hub", StringComparison.Ordinal));
        Assert.DoesNotContain(finding.SubjectSymbols, static symbol => symbol.SymbolId.Contains("Tests", StringComparison.Ordinal));
        Assert.Equal(Describe(finding), Describe(Assert.Single(second.Findings)));
    }

    [Fact]
    public async Task ExecuteAsync_RequiresBothIndependentThresholdsAndRejectsNonPositiveOptions()
    {
        using var fixture = CreateFixture(11, 10);
        var analysis = new TypeDependencyHubCandidatesAnalysis();
        var equalOptions = Options(analysis, 11, 10);
        var fanInTooLow = Options(analysis, 12, 9);
        var fanOutTooLow = Options(analysis, 10, 11);

        Assert.Single((await analysis.ExecuteAsync(fixture.Context, equalOptions, CancellationToken.None)).Findings);
        Assert.Empty((await analysis.ExecuteAsync(fixture.Context, fanInTooLow, CancellationToken.None)).Findings);
        Assert.Empty((await analysis.ExecuteAsync(fixture.Context, fanOutTooLow, CancellationToken.None)).Findings);
        Assert.Throws<ArgumentException>(() => analysis.Descriptor.ResolveOptions(OptionValues(0, 10)));
        Assert.Throws<ArgumentException>(() => analysis.Descriptor.ResolveOptions(OptionValues(10, -1)));
        Assert.Throws<ArgumentException>(() => analysis.Descriptor.ResolveOptions(
            [new KeyValuePair<string, JsonElement>("minFanIn", JsonSerializer.SerializeToElement(1.5))]));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsIntentionalCheckoutCompositionFacadeAtApprovedCountsForReview()
    {
        using var fixture = CreateCheckoutFacadeFixture();
        var analysis = new TypeDependencyHubCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(
            fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal(10, finding.Metrics["fanIn"]);
        Assert.Equal(10, finding.Metrics["fanOut"]);
        Assert.Contains(finding.SubjectSymbols, static symbol => symbol.SymbolId.Contains("CheckoutCompositionRoot", StringComparison.Ordinal));
        Assert.Equal(10, finding.Evidence.Count(static item => item.Detail.StartsWith("Production consumer;", StringComparison.Ordinal)));
        Assert.Equal(10, finding.Evidence.Count(static item => item.Detail.StartsWith("Production dependency;", StringComparison.Ordinal)));
    }

    [Fact]
    public void Descriptor_DeclaresEnabledDefaultsWithoutTestOptions()
    {
        var descriptor = new TypeDependencyHubCandidatesAnalysis().Descriptor;

        Assert.Equal("type-dependency-hub-candidates", descriptor.AnalysisId);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Equal(new[] { "minFanIn", "minFanOut" }, descriptor.Options.Select(static option => option.Name));
        Assert.All(descriptor.Options, static option => Assert.Equal(10, option.DefaultValue.GetInt32()));
        Assert.Empty(descriptor.TestOptions);
    }

    [Fact]
    public void Selector_CountsLinkedNeighborFilesOnceAndKeepsProjectsAndPartialDeclarationsDistinct()
    {
        var source = "namespace Sample; public class Hub { } "
            + string.Join(" ", Enumerable.Range(0, 10).Select(index => $"public class Consumer{index} {{ }} public class Dependency{index} {{ }}"));
        var compilation = CSharpCompilation.Create("Linked", [CSharpSyntaxTree.ParseText(source)], FastTestReferences.CreatePlatformReferences());
        var hubProject = ProjectId.CreateNewId();
        var consumerProjects = new[] { ProjectId.CreateNewId(), ProjectId.CreateNewId() };
        var dependencyProjects = new[] { ProjectId.CreateNewId(), ProjectId.CreateNewId() };
        var hub = Node("Hub", hubProject, "Hub", "Hub.csproj", ["Hub.cs"]);
        var consumers = Enumerable.Range(0, 10).Select(index =>
        {
            var projectIndex = index % 2;
            var paths = index == 0 ? new[] { "SharedConsumers.cs", "SharedConsumers.Part.cs" } : ["SharedConsumers.cs"];
            return Node($"Consumer{index}", consumerProjects[projectIndex], $"Consumers{projectIndex}", $"Consumers{projectIndex}.csproj", paths);
        }).ToArray();
        var dependencies = Enumerable.Range(0, 10).Select(index =>
        {
            var projectIndex = index % 2;
            return Node($"Dependency{index}", dependencyProjects[projectIndex], $"Dependencies{projectIndex}", $"Dependencies{projectIndex}.csproj", ["SharedDependencies.cs"]);
        }).ToArray();
        var edges = consumers.Select(node => Edge(node, hub, node.Declarations[0].SourcePath))
            .Concat(dependencies.Select(node => Edge(hub, node, "Hub.cs"))).ToArray();
        var graph = new TypeDependencyGraph([hub, .. consumers, .. dependencies], edges);

        var candidate = Assert.Single(TypeDependencyHubCandidatesAnalysis.Select(graph, 10, 10, CancellationToken.None));

        Assert.Equal(10, candidate.ProductionConsumers.Count);
        Assert.Equal(10, candidate.ProductionDependencies.Count);
        Assert.Equal(2, candidate.FanInNeighborFileCount);
        Assert.Equal(1, candidate.FanOutNeighborFileCount);
        Assert.Equal(2, candidate.FanInNeighborProjectCount);
        Assert.Equal(2, candidate.FanOutNeighborProjectCount);
    }

    private static ReviewAnalysisOptions Options(TypeDependencyHubCandidatesAnalysis analysis, int minFanIn, int minFanOut) =>
        analysis.Descriptor.ResolveOptions(OptionValues(minFanIn, minFanOut));

    private static IEnumerable<KeyValuePair<string, JsonElement>> OptionValues(int minFanIn, int minFanOut) =>
    [
        new("minFanIn", JsonSerializer.SerializeToElement(minFanIn)),
        new("minFanOut", JsonSerializer.SerializeToElement(minFanOut)),
    ];

    private static string Describe(AiNetReview.Core.Findings.FindingDraft finding) =>
        $"{finding.ProjectPath}|{finding.SourcePath}|{finding.SubjectId}|{string.Join(';', finding.Evidence.Select(static item => item.SourcePath + ':' + item.Line + ':' + item.Label))}";

    private static TypeDependencyNode Node(string name, ProjectId projectId, string projectName, string projectPath, string[] sourcePaths)
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Sample; public class " + name + " { }");
        var compilation = CSharpCompilation.Create("LinkedNodes", [tree], FastTestReferences.CreatePlatformReferences());
        var symbol = compilation.GetTypeByMetadataName("Sample." + name)!;
        return new TypeDependencyNode(symbol, projectId, projectName, projectPath, false,
            sourcePaths.Select(path => new TypeDependencyDeclarationLocation(path, tree.GetRoot().Span, projectId, projectName)).ToList());
    }

    private static TypeDependencyEdge Edge(TypeDependencyNode from, TypeDependencyNode to, string sourcePath) =>
        new(from, to, [new TypeDependencyWitness(TypeDependencyEvidenceKind.MemberUse, from.ProjectId, from.ProjectName,
            from.ProjectName + ".csproj", sourcePath, default)]);

    private static AnalysisFixture CreateFixture(int consumerCount, int dependencyCount, bool addTestConsumer = false)
    {
        var workspace = new FastTestWorkspace();
        var productionId = workspace.AddProject("Product");
        workspace.AddDocument(productionId, "Hub.cs", "namespace Sample; public partial class Hub { "
            + string.Join(" ", Enumerable.Range(0, dependencyCount).Select(index => $"public Dependency{index}? D{index};")) + " }");
        workspace.AddDocument(productionId, "Hub.Partial.cs", "namespace Sample; public partial class Hub { }");
        foreach (var index in Enumerable.Range(0, consumerCount))
        {
            workspace.AddDocument(productionId, $"Consumer{index}.cs",
                $"namespace Sample; public class Consumer{index} {{ public Hub? Value; }}");
        }
        foreach (var index in Enumerable.Range(0, dependencyCount))
        {
            workspace.AddDocument(productionId, $"Dependency{index}.cs", $"namespace Sample; public class Dependency{index} {{ }}");
        }

        if (addTestConsumer)
        {
            var testId = workspace.AddProject("Product.Tests", projectReferences: [new ProjectReference(productionId)]);
            workspace.AddDocument(testId, "Tests.cs", "namespace Sample.Tests; public class HubTests { public Sample.Hub? Value; }");
        }

        return new AnalysisFixture(workspace, workspace.CreateReviewContext());
    }

    private static AnalysisFixture CreateCheckoutFacadeFixture()
    {
        var workspace = new FastTestWorkspace();
        var projectId = workspace.AddProject("Checkout");

        var dependencies = new[]
        {
            "CartRepository", "PriceCatalog", "DiscountPolicy", "TaxCalculator", "PaymentGateway",
            "InventoryService", "OrderWriter", "ReceiptPublisher", "FraudScreen", "CustomerDirectory",
        };
        workspace.AddDocument(projectId, "CheckoutCompositionRoot.cs",
            "namespace Sample; public sealed class CheckoutCompositionRoot { "
            + string.Join(" ", dependencies.Select((name, index) => $"public {name}? Dependency{index};")) + " }");
        foreach (var index in Enumerable.Range(0, 10))
        {
            workspace.AddDocument(projectId, $"CheckoutEndpoint{index}.cs",
                $"namespace Sample; public sealed class CheckoutEndpoint{index} {{ public CheckoutCompositionRoot? Root; }}");
        }
        foreach (var dependency in dependencies)
        {
            workspace.AddDocument(projectId, dependency + ".cs", $"namespace Sample; public sealed class {dependency} {{ }}");
        }

        return new AnalysisFixture(workspace, workspace.CreateReviewContext());
    }

    private sealed class AnalysisFixture(FastTestWorkspace workspace, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;
        public void Dispose() => workspace.Dispose();
    }
}
