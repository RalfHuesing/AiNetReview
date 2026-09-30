namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class MissingTestEvidenceSemanticGraphBuilderTests
{
    [Theory]
    [InlineData("Target()!", "Target", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("(Target()!)", "Target", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("(Target())!", "Target", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("(((Target()!)))", "Target", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("Value!", "get_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("(Value!)", "get_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("((Value!))", "get_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("(((Value!)))", "get_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    public async Task BuildAsync_PreservesEdgesAndTestPathsThroughNullForgivingAndParenthesizedBodies(
        string expression,
        string referencedMethod,
        string edgeKind)
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", $$"""
                    public static class Worker
                    {
                        public static string Target() => string.Empty!;
                        public static string Value => Target();
                        public static string Entry() => {{expression}};
                    }
                    """),
            ],
            testSource: """
                using Xunit;
                public sealed class Tests
                {
                    [Fact] public void Root() { _ = Helper(); }
                    private static string Helper() => Worker.Entry()!;
                }
                """);
        await AssertNoCompilerErrorsAsync(fixture.Workspace.CurrentSolution);

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);

        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Root" && edge.To.Name == "Helper");
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Helper" && edge.To.Name == "Entry");
        var reference = Assert.Single(graph.Edges, edge => edge.From.Name == "Entry" && edge.To.Name == referencedMethod);
        Assert.Equal(edgeKind, reference.Kind.ToString());
        Assert.True(reference.SourceSpan.Length > 0);
        Assert.EndsWith("Worker.cs", reference.SourceFilePath, StringComparison.Ordinal);
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "get_Value" && edge.To.Name == "Target");
        Assert.Empty(graph.UncertaintyInputs);

        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        var entry = Assert.Single(graph.Nodes, static node => node.Method.Name == "Entry");
        var target = Assert.Single(graph.Nodes, static node => node.Method.Name == "Target");
        Assert.Equal(MissingTestEvidencePathKind.Direct, paths[entry.Method].Kind);
        Assert.Equal(new[] { "Root", "Helper", "Entry" }, paths[entry.Method].Path.Select(static node => node.Method.Name));
        Assert.Equal(MissingTestEvidencePathKind.Indirect, paths[target.Method].Kind);
        var expectedPath = referencedMethod == "Target"
            ? new[] { "Root", "Helper", "Entry", "Target" }
            : new[] { "Root", "Helper", "Entry", "get_Value", "Target" };
        Assert.Equal(expectedPath, paths[target.Method].Path.Select(static node => node.Method.Name));
    }

    [Theory]
    [InlineData("public Worker Entry() => Target()!;", "_ = worker.Entry();", "Entry", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("public Worker Entry() { return Target()!; }", "_ = worker.Entry();", "Entry", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("public void Entry() => _ = Target()!;", "worker.Entry();", "Entry", nameof(MissingTestEvidenceGraphEdgeKind.Invocation))]
    [InlineData("public Worker(int value) : this() => _ = Target()!;", "_ = new Worker(1);", ".ctor", nameof(MissingTestEvidenceGraphEdgeKind.ObjectCreation))]
    [InlineData("public Worker Value => Target()!;", "_ = worker.Value;", "get_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("public Worker Value { get => Target()!; }", "_ = worker.Value;", "get_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("public Worker Value { set => _ = Target()!; }", "worker.Value = worker;", "set_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertySet))]
    [InlineData("public Worker Value { init => _ = Target()!; }", "_ = new Worker { Value = worker };", "set_Value", nameof(MissingTestEvidenceGraphEdgeKind.PropertySet))]
    [InlineData("public Worker this[int index] => Target()!;", "_ = worker[1];", "get_Item", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("public Worker this[int index] { get => Target()!; }", "_ = worker[1];", "get_Item", nameof(MissingTestEvidenceGraphEdgeKind.PropertyGet))]
    [InlineData("public Worker this[int index] { set => _ = Target()!; }", "worker[1] = worker;", "set_Item", nameof(MissingTestEvidenceGraphEdgeKind.PropertySet))]
    [InlineData("public event System.Action Changed { add => _ = Target()!; remove => _ = Target()!; }", "worker.Changed += () => { };", "add_Changed", nameof(MissingTestEvidenceGraphEdgeKind.EventAdd))]
    [InlineData("public event System.Action Changed { add => _ = Target()!; remove => _ = Target()!; }", "worker.Changed -= () => { };", "remove_Changed", nameof(MissingTestEvidenceGraphEdgeKind.EventRemove))]
    [InlineData("public static Worker operator +(Worker left, Worker right) => Target()!;", "_ = worker + worker;", "op_Addition", nameof(MissingTestEvidenceGraphEdgeKind.UserOperator))]
    [InlineData("public static implicit operator Worker(int value) => Target()!;", "Worker converted = 1;", "op_Implicit", nameof(MissingTestEvidenceGraphEdgeKind.UserConversion))]
    public async Task BuildAsync_PreservesCallsAndTestPathsForNullForgivingExecutableBodyVariants(
        string declaration,
        string testStatement,
        string entryName,
        string entryEdgeKind)
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", $$"""
                    public sealed class Worker
                    {
                        public Worker() { }
                        public static Worker Target() { return new Worker(); }
                        {{declaration}}
                    }
                    """),
            ],
            testSource: $$"""
                using Xunit;
                public sealed class Tests
                {
                    [Fact] public void Root() { var worker = new Worker(); {{testStatement}} }
                }
                """);
        await AssertNoCompilerErrorsAsync(fixture.Workspace.CurrentSolution);

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var entry = Assert.Single(graph.Nodes, node => node.Method.Name == entryName
            && (entryName != ".ctor" || node.Method.Parameters.Length == 1));
        var target = Assert.Single(graph.Nodes, static node => node.Method.Name == "Target");
        var incoming = Assert.Single(graph.Edges, edge => edge.From.Name == "Root"
            && SymbolEqualityComparer.Default.Equals(edge.To, entry.Method));
        Assert.Equal(entryEdgeKind, incoming.Kind.ToString());
        var outgoing = Assert.Single(graph.Edges, edge => SymbolEqualityComparer.Default.Equals(edge.From, entry.Method)
            && edge.To.Name == "Target");
        Assert.Equal(MissingTestEvidenceGraphEdgeKind.Invocation, outgoing.Kind);
        if (entryName == ".ctor")
        {
            Assert.Contains(graph.Edges, edge => SymbolEqualityComparer.Default.Equals(edge.From, entry.Method)
                && edge.To.MethodKind == MethodKind.Constructor && edge.To.Parameters.Length == 0);
        }

        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        Assert.Equal(MissingTestEvidencePathKind.Direct, paths[entry.Method].Kind);
        Assert.Equal(MissingTestEvidencePathKind.Indirect, paths[target.Method].Kind);
        Assert.Equal(new[] { "Root", entryName, "Target" }, paths[target.Method].Path.Select(static node => node.Method.Name));
        Assert.False(paths[target.Method].IsAttributionUncertain);
    }

    [Fact]
    public async Task BuildAsync_CollectsSemanticCallsAccessorsOperatorsConversionsAndContainedCallbacks()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", """
                    using System;
                    public partial class Worker
                    {
                        private static void PrivatePath() { }
                        public Worker(int value) { if (value > 0) { } }
                        public int Value { get { if (true) { } return 1; } set { if (value > 0) { } } }
                        public event Action Changed { add { if (value is not null) { } } remove { if (value is not null) { } } }
                        public static Worker operator +(Worker left, Worker right) => left;
                        public static implicit operator int(Worker value) => value is null ? 0 : 1;
                        public void Callback() { }
                    }
                    """),
                ("Worker.g.cs", """
                    public partial class Worker
                    {
                        public static void Entry() { PrivatePath(); }
                    }
                    """),
            ],
            testSource: """
                using System;
                using Xunit;
                public sealed class Tests
                {
                    [Fact]
                    public void ActiveRoot()
                    {
                        var worker = new Worker(1);
                        _ = worker.Value;
                        worker.Value = 2;
                        worker.Changed += Handler;
                        worker.Changed -= Handler;
                        _ = worker + worker;
                        int converted = worker;
                        worker.Callback();
                        Action callback = worker.Callback;
                        Action lambda = () => worker.Callback();
                        var callbackName = nameof(Worker.Callback);
                        void Local() { worker.Callback(); }
                        Local();
                        Worker.Entry();
                    }

                    [Fact(Skip = "excluded")]
                    public void SkippedRoot() { _ = new Worker(0); }

                    private static void Handler() { }
                }
                """);

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var root = Assert.Single(graph.Roots);
        Assert.Equal("ActiveRoot", root.Method.Name);
        Assert.DoesNotContain(graph.Roots, static node => node.Method.Name == "SkippedRoot");

        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.ObjectCreation && edge.To.MethodKind == MethodKind.Constructor);
        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.PropertyGet && edge.To.MethodKind == MethodKind.PropertyGet);
        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.PropertySet && edge.To.MethodKind == MethodKind.PropertySet);
        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.EventAdd && edge.To.MethodKind == MethodKind.EventAdd);
        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.EventRemove && edge.To.MethodKind == MethodKind.EventRemove);
        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.UserOperator && edge.To.MethodKind == MethodKind.UserDefinedOperator);
        Assert.Contains(graph.Edges, static edge => edge.Kind == MissingTestEvidenceGraphEdgeKind.UserConversion && edge.To.MethodKind == MethodKind.Conversion);

        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        var directlyCalledFunctions = graph.Nodes.Where(static node => node.Method.MethodKind is
            MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or
            MethodKind.EventRemove or MethodKind.UserDefinedOperator or MethodKind.Conversion
            || node.Method.Name == "Callback");
        Assert.All(directlyCalledFunctions, node => Assert.Equal(MissingTestEvidencePathKind.Direct, paths[node.Method].Kind));

        var callbackCalls = graph.Edges.Where(static edge => edge.To.Name == "Callback").ToArray();
        Assert.True(callbackCalls.Length >= 3, "Calls in the direct body, lambda, and local function are possible edges of the containing test method.");
        Assert.Single(graph.UncertaintyInputs.Where(static input => input.Kind == MissingTestEvidenceUncertaintyKind.MethodGroup && input.AffectedMethod?.Name == "Callback"));

        var generatedEntry = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "Entry"));
        var privateTarget = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "PrivatePath"));
        Assert.True(generatedEntry.IsGenerated);
        Assert.Equal(MissingTestEvidencePathKind.Direct, paths[generatedEntry.Method].Kind);
        Assert.Equal(MissingTestEvidencePathKind.Indirect, paths[privateTarget.Method].Kind);
        Assert.Contains(graph.Edges, edge => edge.From.Name == "Entry" && edge.To.Name == "PrivatePath");
        Assert.Contains(graph.Edges, static edge => edge.To.Name == "PrivatePath" && edge.SourceFilePath?.EndsWith("Worker.g.cs", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task BuildAsync_RecordsUnresolvedAndDispatchUncertaintyWithoutRuntimeEdges()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("IWorker.cs", """
                    public interface IWorker { void Execute(); }
                    public sealed class Worker : IWorker { public void Execute() { } }
                    """),
            ],
            testSource: """
                using Xunit;
                public sealed class Tests
                {
                    [Fact]
                    public void ActiveRoot(IWorker worker)
                    {
                        worker.Execute();
                        worker.Missing();
                    }
                }
                """);

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        Assert.Contains(graph.UncertaintyInputs, static input => input.Kind == MissingTestEvidenceUncertaintyKind.VirtualOrInterfaceDispatch && input.IsGlobal);
        Assert.Contains(graph.UncertaintyInputs, static input => input.Kind == MissingTestEvidenceUncertaintyKind.UnresolvedBinding && input.IsGlobal);
        Assert.DoesNotContain(graph.Edges, static edge => edge.To.Name == "Execute");
    }

    [Fact]
    public async Task BuildAsync_IncludesExpressionBodiedPropertyAndIndexerGettersAndTheirCalls()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", "public sealed class Worker { public int Compute(int value) => value > 0 ? value : 0; public int Value => Compute(1); public int this[int index] => Compute(index); }"),
            ],
            testSource: "using Xunit; public sealed class Tests { [Fact] public void Root(Worker worker) { _ = worker.Value; _ = worker[1]; } }");

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var getterNodes = graph.Nodes.Where(static node => node.Method.MethodKind == MethodKind.PropertyGet).ToArray();

        Assert.Contains(getterNodes, static node => node.Method.Name == "get_Value");
        Assert.Contains(getterNodes, static node => node.Method.Name == "get_Item");
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Root" && edge.To.Name == "get_Value");
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Root" && edge.To.Name == "get_Item");
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "get_Value" && edge.To.Name == "Compute");
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "get_Item" && edge.To.Name == "Compute");
    }

    [Fact]
    public async Task BuildAsync_RecordsDispatchUncertaintyForPropertyAndEventAccessors()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("IWorker.cs", "using System; public interface IWorker { int Value { get; } event Action Changed; }"),
            ],
            testSource: "using System; using Xunit; public sealed class Tests { [Fact] public void Root(IWorker worker) { _ = worker.Value; worker.Changed += Handler; worker.Changed -= Handler; } private static void Handler() { } }");

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var dispatch = graph.UncertaintyInputs.Where(static input => input.Kind == MissingTestEvidenceUncertaintyKind.VirtualOrInterfaceDispatch).ToArray();

        Assert.Contains(dispatch, static input => input.Source.Name == "Root" && input.AffectedMethod?.MethodKind == MethodKind.PropertyGet);
        Assert.Contains(dispatch, static input => input.Source.Name == "Root" && input.AffectedMethod?.MethodKind == MethodKind.EventAdd);
        Assert.Contains(dispatch, static input => input.Source.Name == "Root" && input.AffectedMethod?.MethodKind == MethodKind.EventRemove);
    }

    [Fact]
    public async Task BuildAsync_KeepsGeneratedIntermediatesAndCrossProjectPrivateTargetsInGraph()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Entry.cs", "public partial class Worker { private static void Hidden() { } }"),
                ("Entry.g.cs", "public partial class Worker { public static void Entry() { Hidden(); } }"),
            ],
            testSource: "using Xunit; public sealed class Tests { [Fact] public void Root() { Worker.Entry(); } }");

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        Assert.Equal("Root", Assert.Single(graph.Roots).Method.Name);
        Assert.Contains(graph.Nodes, static node => node.ProjectName == "Example.Core" && node.Method.Name == "Hidden" && !node.IsGenerated);
        Assert.Contains(graph.Nodes, static node => node.ProjectName == "Example.Core" && node.Method.Name == "Entry" && node.IsGenerated);
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Entry" && edge.To.Name == "Hidden");
        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Root" && edge.To.Name == "Entry");
    }

    [Fact]
    public async Task BuildAsync_DoesNotTreatNamesCommentsOrUncalledHelpersAsEdges()
    {
        using var fixture = CreateFixture(
            productionSources: [("Worker.cs", "public sealed class Worker { public static void Entry() { } }")],
            testSource: """
                using Xunit;
                public sealed class Tests
                {
                    [Fact]
                    public void Root()
                    {
                        var name = nameof(Helper);
                        var type = typeof(Worker);
                        // Helper(); Worker.Entry();
                    }

                    private static void Helper() { Worker.Entry(); }
                }
                """);

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var root = Assert.Single(graph.Roots);
        Assert.DoesNotContain(graph.Edges, edge => SymbolEqualityComparer.Default.Equals(edge.From, root.Method));
        Assert.DoesNotContain(graph.UncertaintyInputs, input => SymbolEqualityComparer.Default.Equals(input.Source, root.Method));

        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        var unreachable = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "Entry"));
        Assert.Equal(MissingTestEvidencePathKind.NoPath, paths[unreachable.Method].Kind);
        Assert.Empty(paths[unreachable.Method].Path);
    }

    [Fact]
    public async Task Classify_TracesDeepCyclicPathsThroughGeneratedAndPrivateFunctions()
    {
        var steps = Enumerable.Range(0, 13)
            .Select(index => index == 12
                ? "private static void Step12() { Step3(); }"
                : $"private static void Step{index}() {{ Step{index + 1}();{(index == 8 ? " Step3();" : string.Empty)} }}");
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", "public partial class Worker { public static void Entry() { GeneratedStep(); } " + string.Join(" ", steps) + " }"),
                ("Worker.g.cs", "public partial class Worker { public static void GeneratedStep() { Step0(); } }"),
            ],
            testSource: "using Xunit; public sealed class Tests { [Fact] public void Root() { Worker.Entry(); } }");

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        var entry = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "Entry"));
        var generated = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "GeneratedStep"));
        var target = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "Step12"));

        Assert.Contains(graph.Edges, static edge => edge.From.Name == "Root" && edge.To.Name == "Entry");
        var root = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "Root"));
        Assert.True(root.IsTestProject);
        Assert.False(entry.IsTestProject);
        Assert.Equal(MissingTestEvidencePathKind.NoPath, paths[root.Method].Kind);
        Assert.Equal(MissingTestEvidencePathKind.Direct, paths[entry.Method].Kind);
        Assert.Equal(MissingTestEvidencePathKind.Indirect, paths[generated.Method].Kind);
        Assert.Equal(MissingTestEvidencePathKind.Indirect, paths[target.Method].Kind);
        Assert.Equal(16, paths[target.Method].Path.Count);
        Assert.True(paths[target.Method].Path.Any(static node => node.IsGenerated));
        Assert.Equal("Step12", paths[target.Method].Path[^1].Method.Name);
    }

    [Fact]
    public async Task Classify_PrefersDirectPathsAndBreaksIndirectTiesBySymbolIdSequence()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", """
                    public sealed class Worker
                    {
                        public static void EntryA() { EntryB(); IndirectTarget(); }
                        public static void EntryB() { EntryA(); IndirectTarget(); }
                        public static void IndirectTarget() { }
                        public static void HelperTarget() { }
                        public static void DirectTarget() { }
                    }
                    """),
            ],
            testSource: "using Xunit; public sealed class Tests { [Fact] public void Root() { TestHelper(); Worker.EntryB(); Worker.EntryA(); Worker.DirectTarget(); } private static void TestHelper() { Worker.HelperTarget(); } }");

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        var indirectTarget = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "IndirectTarget"));
        var helperTarget = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "HelperTarget"));
        var directTarget = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "DirectTarget"));

        Assert.Equal(MissingTestEvidencePathKind.Indirect, paths[indirectTarget.Method].Kind);
        Assert.Equal(new[] { "Root", "EntryA", "IndirectTarget" }, paths[indirectTarget.Method].Path.Select(static node => node.Method.Name));
        Assert.Equal(MissingTestEvidencePathKind.Direct, paths[helperTarget.Method].Kind);
        Assert.Equal(new[] { "Root", "TestHelper", "HelperTarget" }, paths[helperTarget.Method].Path.Select(static node => node.Method.Name));
        Assert.Equal(MissingTestEvidencePathKind.Direct, paths[directTarget.Method].Kind);
        Assert.Equal(new[] { "Root", "DirectTarget" }, paths[directTarget.Method].Path.Select(static node => node.Method.Name));
    }

    [Fact]
    public async Task Classify_PropagatesAffectedAndGlobalUncertaintyWithoutAddingPaths()
    {
        using var affectedFixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", "public static class Worker { public static void ReferencedByMethodGroup() { } public static void Unreferenced() { } }"),
            ],
            testSource: "using System; using Xunit; public sealed class Tests { [Fact] public void Root() { Action callback = Worker.ReferencedByMethodGroup; } private static void Unreached() { Action callback = Worker.Unreferenced; } }");
        var affectedGraph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(affectedFixture.Workspace.CurrentSolution, CancellationToken.None);
        var affectedPaths = MissingTestEvidencePathClassifier.Classify(affectedGraph, CancellationToken.None);
        var referenced = Assert.Single(affectedGraph.Nodes.Where(static node => node.Method.Name == "ReferencedByMethodGroup"));
        var unreferenced = Assert.Single(affectedGraph.Nodes.Where(static node => node.Method.Name == "Unreferenced"));

        Assert.Equal(MissingTestEvidencePathKind.NoPath, affectedPaths[referenced.Method].Kind);
        Assert.True(affectedPaths[referenced.Method].IsAttributionUncertain);
        Assert.Equal(MissingTestEvidencePathKind.NoPath, affectedPaths[unreferenced.Method].Kind);
        Assert.False(affectedPaths[unreferenced.Method].IsAttributionUncertain);

        using var globalFixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", "public static class Worker { public static void First() { } public static void Second() { } }"),
            ],
            testSource: "using Xunit; public sealed class Tests { [Fact] public void Root() { Worker.First(); Missing(); } }");
        var globalGraph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(globalFixture.Workspace.CurrentSolution, CancellationToken.None);
        var globalPaths = MissingTestEvidencePathClassifier.Classify(globalGraph, CancellationToken.None);
        var first = Assert.Single(globalGraph.Nodes.Where(static node => node.Method.Name == "First"));
        var second = Assert.Single(globalGraph.Nodes.Where(static node => node.Method.Name == "Second"));

        Assert.Equal(MissingTestEvidencePathKind.Direct, globalPaths[first.Method].Kind);
        Assert.Equal(MissingTestEvidencePathKind.NoPath, globalPaths[second.Method].Kind);
        Assert.True(globalPaths[first.Method].IsAttributionUncertain);
        Assert.True(globalPaths[second.Method].IsAttributionUncertain);
    }

    [Fact]
    public async Task Classify_PropagatesAffectedUncertaintyThroughResolvedDownstreamCalls()
    {
        using var fixture = CreateFixture(
            productionSources:
            [
                ("Worker.cs", "public static class Worker { public static void MethodGroupTarget() { Downstream(); } public static void Downstream() { } }"),
            ],
            testSource: "using System; using Xunit; public sealed class Tests { [Fact] public void Root() { Action callback = Worker.MethodGroupTarget; } }");

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(fixture.Workspace.CurrentSolution, CancellationToken.None);
        var paths = MissingTestEvidencePathClassifier.Classify(graph, CancellationToken.None);
        var target = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "MethodGroupTarget"));
        var downstream = Assert.Single(graph.Nodes.Where(static node => node.Method.Name == "Downstream"));

        Assert.Equal(MissingTestEvidencePathKind.NoPath, paths[target.Method].Kind);
        Assert.True(paths[target.Method].IsAttributionUncertain);
        Assert.Equal(MissingTestEvidencePathKind.NoPath, paths[downstream.Method].Kind);
        Assert.True(paths[downstream.Method].IsAttributionUncertain);
        Assert.Empty(paths[downstream.Method].Path);
    }

    [Fact]
    public async Task BuildAsync_RejectsSolutionsWithoutRequiredCSharpCompilation()
    {
        using var workspace = new AdhocWorkspace();
        workspace.AddProject("MarkupOnly", "Visual Basic");

        var exception = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => MissingTestEvidenceSemanticGraphBuilder.BuildAsync(workspace.CurrentSolution, CancellationToken.None));

        Assert.Contains("no C# projects", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredCompilationAndSemanticModelGuardsFailInsteadOfReturningPartialGraphInputs()
    {
        var compilationFailure = Assert.Throws<AnalysisFailedException>(
            () => MissingTestEvidenceSemanticGraphBuilder.RequireCompilation(null, "Unavailable"));
        Assert.Contains("Compilation could not be created", compilationFailure.Message, StringComparison.Ordinal);

        var semanticModelFailure = Assert.Throws<AnalysisFailedException>(
            () => MissingTestEvidenceSemanticGraphBuilder.RequireSemanticModel(null, "Unavailable.cs"));
        Assert.Contains("Semantic model could not be created", semanticModelFailure.Message, StringComparison.Ordinal);
    }

    private static async Task AssertNoCompilerErrorsAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(CancellationToken.None);
            Assert.NotNull(compilation);
            Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    private static Fixture CreateFixture(
        IReadOnlyList<(string Name, string Source)> productionSources,
        string testSource)
    {
#pragma warning disable CA2000 // Fixture takes ownership and disposes the workspace returned by this helper.
        var workspace = new AdhocWorkspace();
#pragma warning restore CA2000
        var productionId = ProjectId.CreateNewId();
        var testId = ProjectId.CreateNewId();
        var productionPath = Path.Combine(Path.GetTempPath(), "AiNetReview-MissingTestEvidenceGraph", "Example.Core.csproj");
        var testPath = Path.Combine(Path.GetTempPath(), "AiNetReview-MissingTestEvidenceGraph", "Example.Tests.csproj");
        var platformReferences = PlatformReferences().ToArray();

        workspace.AddProject(ProjectInfo.Create(
            productionId,
            VersionStamp.Create(),
            "Example.Core",
            "Example.Core",
            LanguageNames.CSharp,
            filePath: productionPath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: platformReferences));
        foreach (var (name, source) in productionSources)
        {
            AddDocument(workspace, productionId, name, source, Path.Combine(Path.GetDirectoryName(productionPath)!, name));
        }

        workspace.AddProject(ProjectInfo.Create(
            testId,
            VersionStamp.Create(),
            "Example.Tests",
            "Example.Tests",
            LanguageNames.CSharp,
            filePath: testPath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: platformReferences.Append(TestFrameworkReference.Reference),
            projectReferences: [new ProjectReference(productionId)]));
        AddDocument(workspace, testId, "Tests.cs", testSource, Path.Combine(Path.GetDirectoryName(testPath)!, "Tests.cs"));

        return new Fixture(workspace);
    }

    private static void AddDocument(AdhocWorkspace workspace, ProjectId projectId, string name, string source, string path) =>
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name,
            filePath: path,
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

    private sealed class Fixture(AdhocWorkspace workspace) : IDisposable
    {
        public AdhocWorkspace Workspace { get; } = workspace;

        public void Dispose() => Workspace.Dispose();
    }

    private static class TestFrameworkReference
    {
        private const string Source = "using System; namespace Xunit { [AttributeUsage(AttributeTargets.Method)] public sealed class FactAttribute : Attribute { public string? Skip { get; set; } } }";

        public static MetadataReference Reference { get; } = CreateReference();

        private static MetadataReference CreateReference()
        {
            var compilation = CSharpCompilation.Create(
                "xunit.graph.contracts",
                [CSharpSyntaxTree.ParseText(Source)],
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var assembly = new MemoryStream();
            var result = compilation.Emit(assembly);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
            }

            return MetadataReference.CreateFromImage(assembly.ToArray(), filePath: "xunit.graph.contracts.dll");
        }
    }
}
