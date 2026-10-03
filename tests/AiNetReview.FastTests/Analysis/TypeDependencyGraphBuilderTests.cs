namespace AiNetReview.FastTests.Analysis;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class TypeDependencyGraphBuilderTests
{
    [Fact]
    public async Task BuildAsync_CollectsBoundTypeAndMemberDependenciesAndKeepsOnlyStableWitnesses()
    {
        using var fixture = CreateFixture();

        var graph = await TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None);
        var service = Node(graph, "T:App.Service", "Example.Core");
        var item = Node(graph, "T:App.Item", "Example.Core");
        var parent = Node(graph, "T:App.Parent", "Example.Core");
        var contract = Node(graph, "T:App.IContract", "Example.Core");
        var box = Node(graph, "T:App.Box`1", "Example.Core");
        var nested = Assert.Single(graph.Nodes.Where(node => node.ProjectName == "Example.Core"
            && node.Symbol.Name == "Inner" && node.Symbol.ContainingType?.Name == "Outer"));

        Assert.Contains(graph.ProductionEdges, edge => edge.From == service && edge.To == item);
        Assert.Contains(graph.ProductionEdges.Single(edge => edge.From == service && edge.To == parent).Witnesses,
            witness => witness.Kind == TypeDependencyEvidenceKind.Inheritance);
        Assert.Contains(graph.ProductionEdges.Single(edge => edge.From == service && edge.To == contract).Witnesses,
            witness => witness.Kind == TypeDependencyEvidenceKind.Inheritance);
        var genericBaseUse = Node(graph, "T:App.GenericBaseUse", "Example.Core");
        var genericBaseEdge = graph.ProductionEdges.Single(edge => edge.From == genericBaseUse && edge.To.Symbol.Name == "GenericBase");
        Assert.Contains(genericBaseEdge.Witnesses, witness => witness.Kind == TypeDependencyEvidenceKind.Inheritance);
        Assert.Contains(graph.ProductionEdges.Single(edge => edge.From == genericBaseUse && edge.To == item).Witnesses,
            witness => witness.Kind == TypeDependencyEvidenceKind.ExplicitTypeUse);
        Assert.Contains(graph.ProductionEdges, edge => edge.From == service && edge.To == box);
        Assert.Contains(graph.ProductionEdges, edge => edge.From == service && edge.To == nested);
        Assert.Contains(graph.ProductionEdges, edge => edge.From == service && edge.To.Symbol.Name == "ExtensionMethods");
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From.Symbol.Name == "AttributedOnly" && edge.To == item);
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From.Symbol.Name == "NameOnly" && edge.To == item);
        Assert.DoesNotContain(graph.Nodes, node => node.Symbol.Name == "List");
        Assert.Contains(graph.Nodes, node => node.Symbol.Name == "Inner");

        var propertyConsumer = Node(graph, "T:App.PropertyConsumer", "Example.Core");
        var provider = Node(graph, "T:App.ValueProvider", "Example.Core");
        var returnedValue = Node(graph, "T:App.ReturnValue", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == propertyConsumer && edge.To == provider);
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From == propertyConsumer && edge.To == returnedValue);

        var implicitConsumer = Node(graph, "T:App.ImplicitConsumer", "Example.Core");
        var implicitOnly = Node(graph, "T:App.ImplicitOnly", "Example.Core");
        Assert.Contains(graph.ProductionEdges.Single(edge => edge.From == implicitConsumer && edge.To == implicitOnly).Witnesses,
            witness => witness.Kind == TypeDependencyEvidenceKind.MemberUse);

        var partial = Node(graph, "T:App.PartialConsumer", "Example.Core");
        Assert.Equal(2, partial.Declarations.Count);
        Assert.Contains(graph.ProductionEdges, edge => edge.From == partial && edge.To.Symbol.Name == "PartialTarget");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == partial && edge.To.Symbol.Name == "PartialOther");

        var nestedConsumer = Assert.Single(graph.Nodes.Where(node => node.Symbol.Name == "NestedConsumer"));
        var outer = Assert.Single(graph.Nodes.Where(node => node.Symbol.Name == "Container"));
        Assert.Contains(graph.ProductionEdges, edge => edge.From == nestedConsumer && edge.To.Symbol.Name == "NestedTarget");
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From == outer && edge.To.Symbol.Name == "NestedTarget");

        var shape = Node(graph, "T:App.ShapeOwner", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == shape && edge.To.Symbol.Name == "Item");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == shape && edge.To.Symbol.Name == "Native");
        var delegateNode = Node(graph, "T:App.Transform", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == delegateNode && edge.To.Symbol.Name == "Item");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == delegateNode && edge.To.Symbol.Name == "ReturnValue");
        var constraint = Node(graph, "T:App.ConstraintOwner`1", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == constraint && edge.To.Symbol.Name == "Parent");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == constraint && edge.To.Symbol.Name == "IContract");
        Assert.Contains(graph.Nodes, node => node.Symbol.Name == "Code" && node.Symbol.TypeKind == TypeKind.Enum);

        var indexerUse = Node(graph, "T:App.IndexerConsumer", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == indexerUse && edge.To.Symbol.Name == "IndexerProvider");
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From == indexerUse && edge.To == returnedValue);
        var memberConsumers = graph.ProductionEdges.Where(edge => edge.Witnesses.Any(static witness => witness.Kind == TypeDependencyEvidenceKind.MemberUse)).ToArray();
        Assert.Contains(memberConsumers, edge => edge.To.Symbol.Name == "OperatorNumber");
        Assert.Contains(memberConsumers, edge => edge.To.Symbol.Name == "EventProvider");
        Assert.Contains(memberConsumers, edge => edge.To.Symbol.Name == "MethodGroupProvider");
        Assert.Contains(memberConsumers, edge => edge.To.Symbol.Name == "IBehavior");
        var record = Node(graph, "T:App.DerivedRecord", "Example.Core");
        Assert.Contains(graph.ProductionEdges.Single(edge => edge.From == record && edge.To.Symbol.Name == "RecordBase").Witnesses,
            witness => witness.Kind == TypeDependencyEvidenceKind.MemberUse);
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From == edge.To);
        var tupleUse = Node(graph, "T:App.TupleUse", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == tupleUse && edge.To.Symbol.Name == "Item");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == tupleUse && edge.To.Symbol.Name == "Native");
        var explicitGeneric = Node(graph, "T:App.ExplicitGenericUse", "Example.Core");
        Assert.Contains(graph.ProductionEdges, edge => edge.From == explicitGeneric && edge.To.Symbol.Name == "Item");

        var serviceToItem = graph.ProductionEdges.Single(edge => edge.From == service && edge.To == item);
        Assert.Equal(serviceToItem.Witnesses.Select(static witness => witness.Kind).Distinct().Count(), serviceToItem.Witnesses.Count);
        Assert.All(serviceToItem.Witnesses, witness => Assert.False(string.IsNullOrWhiteSpace(witness.SourcePath)));

        var repeated = await TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None);
        Assert.Equal(
            graph.ProductionEdges.Select(EdgeFingerprint),
            repeated.ProductionEdges.Select(EdgeFingerprint));
    }

    [Fact]
    public async Task BuildAsync_ResolvesAcrossProjectsAndKeepsTestAndGeneratedSourcesOutOfProductionGraph()
    {
        using var fixture = CreateFixture();

        var graph = await TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None);
        var consumer = Node(graph, "T:App.Consumer", "Example.OtherCore");
        var shared = Node(graph, "T:App.SharedType", "Example.Core");
        var test = Node(graph, "T:Example.Tests.ServiceTests", "Example.Tests");

        Assert.Contains(graph.ProductionEdges, edge => edge.From == consumer && edge.To == shared);
        Assert.Contains(graph.TestContextEdges, edge => edge.From == test && edge.To == shared);
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From.IsTestProject || edge.To.IsTestProject);
        Assert.DoesNotContain(graph.Nodes, node => node.Symbol.Name == "GeneratedConsumer");
        Assert.DoesNotContain(graph.Nodes, node => node.Symbol.Name == "AttributedGenerated");
        Assert.Equal(2, graph.Nodes.Where(node => node.Symbol.Name == "LinkedType").Count());

        var linked = graph.Nodes.Where(node => node.Symbol.Name == "LinkedType").ToArray();
        Assert.All(linked, node => Assert.Equal("SharedLinked.cs", Assert.Single(node.Declarations).SourcePath));
        Assert.NotEqual(linked[0].ProjectId, linked[1].ProjectId);
    }

    [Fact]
    public async Task BuildAsync_FailsAtRequiredUnresolvedTypeWithProjectSourceAndPosition()
    {
        using var fixture = CreateBrokenFixture();

        var exception = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None));

        Assert.Contains("Example.Core", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Broken.cs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("position", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_FailsAtRequiredAmbiguousMemberBinding()
    {
        using var fixture = CreateAmbiguousFixture();

        var exception = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None));

        Assert.Contains("Ambiguous.cs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("position", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_FailsAtRequiredUnresolvedMemberBindingWithoutReturningGraph()
    {
        using var fixture = CreateUnresolvedMemberFixture();

        var exception = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None));

        Assert.Contains("MissingCall", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UnresolvedMember.cs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("position", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_FailsAtRequiredUnresolvedMethodGroupBinding()
    {
        using var fixture = CreateUnresolvedMethodGroupFixture();

        var exception = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None));

        Assert.Contains("MissingMethod", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MethodGroup.cs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ExcludesImplicitTopLevelProgramTypes()
    {
        using var fixture = CreateTopLevelProgramFixture();

        var graph = await TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None);

        Assert.Contains(graph.Nodes, node => node.Symbol.Name == "ProgramConsumer");
        Assert.DoesNotContain(graph.Nodes, node => node.Symbol.Name == "Program");
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.To.Symbol.Name == "Program");
    }

    [Fact]
    public async Task BuildAsync_PropagatesCancellationAndIgnoresDynamicTargets()
    {
        using var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TypeDependencyGraphBuilder.BuildAsync(fixture.Context, cancellation.Token));

        var graph = await TypeDependencyGraphBuilder.BuildAsync(fixture.Context, CancellationToken.None);
        var dynamicConsumer = Node(graph, "T:App.DynamicConsumer", "Example.Core");
        Assert.DoesNotContain(graph.ProductionEdges, edge => edge.From == dynamicConsumer);
    }

    private static TypeDependencyNode Node(TypeDependencyGraph graph, string declarationId, string projectName) =>
        Assert.Single(graph.Nodes.Where(node => node.ProjectName == projectName
            && DocumentationCommentId.CreateDeclarationId(node.Symbol) == declarationId));

    private static string EdgeFingerprint(TypeDependencyEdge edge) =>
        $"{edge.From.ProjectName}:{edge.From.StableId}->{edge.To.ProjectName}:{edge.To.StableId}:"
        + string.Join(",", edge.Witnesses.Select(static witness => $"{witness.Kind}@{witness.ProjectPath}/{witness.SourcePath}:{witness.Span.Start}"));

    private static Fixture CreateFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyGraph", Guid.NewGuid().ToString("N"));
        var production = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var test = ProjectId.CreateNewId();
        var refs = FastTestReferences.CreatePlatformReferences().ToArray();
        AddProject(workspace, production, "Example.Core", root, refs);
        AddProject(workspace, other, "Example.OtherCore", root, refs);
        AddProject(workspace, test, "Example.Tests", root, refs);
        var solution = workspace.CurrentSolution
            .AddProjectReference(other, new ProjectReference(production))
            .AddProjectReference(test, new ProjectReference(production))
            .AddProjectReference(test, new ProjectReference(other));
        Assert.True(workspace.TryApplyChanges(solution));

        AddDocument(workspace, production, "Core.cs", """
            using System;
            using System.Collections.Generic;
            namespace App;
            public interface IContract { }
            public class Parent { public void Inherited() { } }
            public class GenericBase<T> { }
            public class GenericBaseUse : GenericBase<Item> { }
            public class Item { }
            public class Box<T> { }
            public static class ExtensionMethods { public static void Extend(this Item item) { } }
            public class Outer { public class Inner { } }
            public class Service : Parent, IContract
            {
                private List<Item> items = new();
                public Box<Outer.Inner> Convert(Item input)
                {
                    Inherited();
                    input.Extend();
                    input.Extend();
                    var inferred = new Item();
                    _ = inferred;
                    _ = new Item();
                    return new Box<Outer.Inner>();
                }
                public Item Property { get; set; } = new();
            }
            [Marker(typeof(Item))]
            public class AttributedOnly { }
            public sealed class MarkerAttribute : Attribute { public MarkerAttribute(Type type) { } }
            public class NameOnly { public string Read() { // Item is a comment, too.
                return nameof(Item) + "Item";
            } }
            public class SharedType { }
            public class ReturnValue { }
            public class ValueProvider { public ReturnValue Value => new(); }
            public class PropertyConsumer { public ValueProvider Provider { get; } = new(); public void Read() { var result = Provider.Value; _ = result; } }
            public class Factory { public static ImplicitOnly Create(ImplicitOnly value) => value; }
            public class ImplicitOnly { public void Work() { } }
            public class ImplicitConsumer { public void Run() { var inferred = Factory.Create(new()); inferred.Work(); } }
            public partial class PartialConsumer { public void One() { _ = new PartialTarget(); } }
            public class PartialTarget { }
            public class PartialOther { }
            public class Container { public class NestedConsumer { public void Run() { _ = new NestedTarget(); } } }
            public class NestedTarget { }
            public unsafe struct Native { public int Value; }
            public unsafe class ShapeOwner { public Native* Pointer; public delegate*<Item, void> Callback; public Item?[]? Items; }
            public delegate ReturnValue Transform(Item input);
            public class ConstraintOwner<T> where T : Parent, IContract { }
            public enum Code : short { A }
            public class IndexerProvider { public ReturnValue this[int index] => new(); }
            public class IndexerConsumer { public void Read(IndexerProvider provider) { var value = provider[0]; _ = value; } }
            public struct OperatorNumber { public static OperatorNumber operator +(OperatorNumber a, int b) => a; public static implicit operator OperatorNumber(int value) => default; }
            public class OperatorConsumer { public void Add(OperatorNumber value) { var sum = value + 1; OperatorNumber converted = 1; _ = sum; _ = converted; } }
            public class EventProvider { public event EventHandler? Changed; }
            public class EventConsumer { public void Listen(EventProvider provider, EventHandler handler) { provider.Changed += handler; } }
            public interface IBehavior { void Apply(); }
            public class InterfaceConsumer { public void Run(IBehavior behavior) { behavior.Apply(); } }
            public class MethodGroupProvider { public void Handle() { } }
            public class MethodGroupConsumer { public Action Callback = new MethodGroupProvider().Handle; }
            public static class GenericMethods { public static T Create<T>() => default!; }
            public class ExplicitGenericUse { public void Run() { _ = GenericMethods.Create<Item>(); } }
            public class TupleUse { public (Item Value, Native Native) Pair; }
            public class DynamicConsumer { public void Run(dynamic value) { value.ExternalCall(); } }
            public class RecordDependency { }
            public class RecordBase { public RecordBase(RecordDependency dependency) { } }
            public record DerivedRecord(RecordDependency Value) : RecordBase(Value);
            [System.CodeDom.Compiler.GeneratedCode("generator", "1")]
            public class AttributedGenerated { public class NestedGenerated { } }
            """, Path.Combine(root, "Core.cs"));
        AddDocument(workspace, production, "PartialConsumer.cs", "namespace App; public partial class PartialConsumer { public PartialOther More { get; } = new(); }", Path.Combine(root, "PartialConsumer.cs"));
        AddDocument(workspace, other, "Consumer.cs", """
            using App;
            namespace App;
            public class Consumer { private SharedType dependency = new(); }
            """, Path.Combine(root, "Consumer.cs"));
        AddDocument(workspace, test, "Tests.cs", """
            using App;
            namespace Example.Tests;
            public class ServiceTests { public void Run() { _ = new Service(); _ = new SharedType(); } }
            """, Path.Combine(root, "Tests.cs"));
        AddDocument(workspace, production, "Generated.g.cs", """
            namespace App;
            public class GeneratedConsumer { public Service Value = new(); }
            """, Path.Combine(root, "Generated.g.cs"));
        AddDocument(workspace, production, "SharedLinked.cs", "namespace Link; public class LinkedType { }", Path.Combine(root, "SharedLinked.cs"));
        AddDocument(workspace, other, "SharedLinked.cs", "namespace Link; public class LinkedType { }", Path.Combine(root, "SharedLinked.cs"));

        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static Fixture CreateBrokenFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyGraph", Guid.NewGuid().ToString("N"));
        var projectId = ProjectId.CreateNewId();
        AddProject(workspace, projectId, "Example.Core", root, FastTestReferences.CreatePlatformReferences());
        AddDocument(workspace, projectId, "Broken.cs", "namespace App; public class Broken { Missing dependency; }", Path.Combine(root, "Broken.cs"));
        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static Fixture CreateAmbiguousFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyGraph", Guid.NewGuid().ToString("N"));
        var projectId = ProjectId.CreateNewId();
        AddProject(workspace, projectId, "Example.Core", root, FastTestReferences.CreatePlatformReferences());
        AddDocument(workspace, projectId, "Ambiguous.cs", "namespace App; public class Target { public Target(int value) { } public Target(string value) { } } public class Caller { public void Run() { _ = new Target(default); } }", Path.Combine(root, "Ambiguous.cs"));
        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static Fixture CreateUnresolvedMemberFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyGraph", Guid.NewGuid().ToString("N"));
        var projectId = ProjectId.CreateNewId();
        AddProject(workspace, projectId, "Example.Core", root, FastTestReferences.CreatePlatformReferences());
        AddDocument(workspace, projectId, "UnresolvedMember.cs", "namespace App; public class Caller { public void Run() { MissingCall(); } }", Path.Combine(root, "UnresolvedMember.cs"));
        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static Fixture CreateUnresolvedMethodGroupFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyGraph", Guid.NewGuid().ToString("N"));
        var projectId = ProjectId.CreateNewId();
        AddProject(workspace, projectId, "Example.Core", root, FastTestReferences.CreatePlatformReferences());
        AddDocument(workspace, projectId, "MethodGroup.cs", "using System; namespace App; public class Caller { public Action Callback = MissingMethod; }", Path.Combine(root, "MethodGroup.cs"));
        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static Fixture CreateTopLevelProgramFixture()
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-TypeDependencyGraph", Guid.NewGuid().ToString("N"));
        var projectId = ProjectId.CreateNewId();
        AddProject(workspace, projectId, "Example.App", root, FastTestReferences.CreatePlatformReferences(), OutputKind.ConsoleApplication);
        AddDocument(workspace, projectId, "Program.cs", "using System; Console.WriteLine(typeof(Program)); public class ProgramConsumer { public Type GetProgramType() => typeof(Program); }", Path.Combine(root, "Program.cs"));
        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static void AddProject(AdhocWorkspace workspace, ProjectId projectId, string name, string root, IEnumerable<MetadataReference> references,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary) =>
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: Path.Combine(root, name + ".csproj"),
            compilationOptions: new CSharpCompilationOptions(outputKind, allowUnsafe: true),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            metadataReferences: references));

    private static void AddDocument(AdhocWorkspace workspace, ProjectId projectId, string name, string source, string path) =>
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name,
            filePath: path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));

    private sealed class Fixture(AdhocWorkspace workspace, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;
        public void Dispose() => workspace.Dispose();
    }
}
