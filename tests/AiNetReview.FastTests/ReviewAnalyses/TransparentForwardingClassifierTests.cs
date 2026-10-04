namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class TransparentForwardingClassifierTests
{
    [Fact]
    public async Task ClassifyProjectAsync_AcceptsEachBodyFormAndSupportedReceivers()
    {
        using var fixture = CreateFixture(("Product", "Forwarders.cs", """
            public sealed class Sink
            {
                public int Pass(int value) => value;
                public int Pass(Sink target, int value) => value;
                public long LongPass(int value) => value;
                public void Ping() { }
                public static int StaticPass(int value) => value;
            }
            public sealed class Caller
            {
                private readonly Sink sink = new();
                public int Expression(int value) => this.sink.Pass(value);
                public int Block(int value) { return (sink.Pass(value)); }
                public void VoidBlock() { this.sink.Ping(); }
                public int StaticReceiver(int value) => Sink.StaticPass(value);
                public int ParameterReceiver(Sink target, int value) => target.Pass(target, value);
            }
            """));

        var declarations = await TransparentForwardingClassifier.ClassifyProjectAsync(
            fixture.Context,
            fixture.Context.Solution.Projects.Single(),
            CancellationToken.None);

        var forwarders = declarations.Where(static declaration => declaration.Target is not null).ToArray();
        Assert.Equal("Block,Expression,ParameterReceiver,StaticReceiver,VoidBlock", string.Join(',', forwarders.Select(static declaration => declaration.Symbol.Name).Order(StringComparer.Ordinal)));
        Assert.All(forwarders, static declaration =>
        {
            Assert.False(string.IsNullOrWhiteSpace(declaration.DocId));
            Assert.False(string.IsNullOrWhiteSpace(declaration.Target!.DocId));
            Assert.Equal("Forwarders.cs", declaration.SourcePath);
            Assert.Equal("Forwarders.cs", declaration.Target.SourcePath);
        });
        Assert.Contains(forwarders, static declaration => declaration.Symbol.Name == "VoidBlock");
        Assert.Contains(forwarders, static declaration => declaration.Symbol.Name == "StaticReceiver");
        Assert.Contains(forwarders, static declaration => declaration.Symbol.Name == "ParameterReceiver");
    }

    [Fact]
    public async Task ClassifyProjectAsync_NormalizesConstructedGenericTargetToSourceDeclaration()
    {
        using var fixture = CreateFixture(("Product", "Generic.cs", """
            public sealed class Sink<T> { public T Pass(T value) => value; }
            public sealed class Caller
            {
                private readonly Sink<int> sink = new();
                public int Forward(int value) => sink.Pass(value);
            }
            """));

        var declarations = await Classify(fixture);
        var forwarder = Assert.Single(declarations.Where(static declaration => declaration.Symbol.Name == "Forward"));

        Assert.NotNull(forwarder.Target);
        Assert.Equal("M:Sink`1.Pass(`0)~`0", forwarder.Target!.DocId);
        Assert.Equal("Generic.cs", forwarder.Target.SourcePath);
    }

    [Fact]
    public async Task ClassifyProjectAsync_AcceptsInterfaceAndVirtualDeclarationsAsStaticTargets()
    {
        using var fixture = CreateFixture(("Product", "Targets.cs", """
            public interface ITarget { int Pass(int value); }
            public class VirtualTarget { public virtual int Run(int value) => value; }
            public sealed class Caller
            {
                private readonly ITarget interfaceTarget = null!;
                private readonly VirtualTarget virtualTarget = new();
                public int ThroughInterface(int value) => interfaceTarget.Pass(value);
                public int ThroughVirtual(int value) => virtualTarget.Run(value);
            }
            """));

        var declarations = await Classify(fixture);
        var forwarders = declarations.Where(static declaration => declaration.Target is not null).ToArray();

        Assert.Equal(2, forwarders.Length);
        Assert.Contains(forwarders, static declaration => declaration.Target!.Symbol.ContainingType.TypeKind == TypeKind.Interface);
        Assert.Contains(forwarders, static declaration => declaration.Target!.Symbol.IsVirtual);
    }

    [Fact]
    public async Task ClassifyProjectAsync_RejectsBodyReturnArgumentReceiverAndBindingExclusions()
    {
        using var fixture = CreateFixture(("Product", "Exclusions.cs", """
            public sealed class Sink
            {
                public int Pass(int value) => value;
                public long LongPass(int value) => value;
                public int Optional(int value, int extra = 0) => value;
                public int Pair(int first, int second) => first;
                public int Widen(long value) => (int)value;
                public void Ping(int value) { }
                public void RefPing(ref int value) { }
            }
            public static class Extension { public static int Extend(this Sink sink, int value) => value; }
            public sealed class Caller
            {
                private readonly Sink sink = new();
                public int Conditional(int value) => value > 0 ? sink.Pass(value) : 0;
                public int ExtraStatement(int value) { var copy = value; return sink.Pass(copy); }
                public int Assignment(int value) { value = value + 1; return sink.Pass(value); }
                public int Cast(int value) => (int)sink.Widen(value);
                public int NullForgiving(int value) => sink!.Pass(value);
                public int PropertyReceiver(int value) => Target.Pass(value);
                public Sink Target => sink;
                public int LocalReceiver(int value) { var local = sink; return local.Pass(value); }
                public int FactoryReceiver(int value) => Create().Pass(value);
                private Sink Create() => sink;
                public int ConditionalAccess(int value) => sink?.Pass(value) ?? 0;
                public async System.Threading.Tasks.Task<int> Await(int value) => await System.Threading.Tasks.Task.FromResult(sink.Pass(value));
                public int ExceptionHandling(int value) { try { return sink.Pass(value); } catch { throw; } }
                public int ReturnConversion(int value) => sink.LongPass(value);
                public int ParameterTransform(int value) => sink.Pass(value + 1);
                public int ArgumentReorder(int first, int second) => sink.Pair(second: first, first: second);
                public int NamedOrderSame(int value) => sink.Optional(value: value);
                public int OptionalArgument(int value) => sink.Optional(value);
                public void RefArgument(ref int value) => sink.RefPing(ref value);
                public int Extension(int value) => sink.Extend(value);
                public int Unresolved(int value) => Missing.Pass(value);
                public int LocalFunction(int value) { int Pass(int item) => item; return Pass(value); }
            }
            """));

        var declarations = await Classify(fixture);
        var byName = declarations.Where(static declaration => declaration.Symbol.ContainingType.Name == "Caller")
            .ToDictionary(static declaration => declaration.Symbol.Name, StringComparer.Ordinal);

        Assert.Empty(byName.Where(static pair => pair.Value.Target is not null));
    }

    [Fact]
    public async Task ClassifyProjectAsync_IncludesTestProjectsAndExcludesGeneratedDocumentsAndSymbols()
    {
        using var fixture = CreateFixture(
            ("Product", "Normal.cs", "public sealed class Sink { public int Pass(int value) => value; } public sealed class Caller { private Sink sink = new(); public int Forward(int value) => sink.Pass(value); }"),
            ("Product", "Generated.g.cs", "public sealed class GeneratedCaller { private Sink sink = new(); public int Forward(int value) => sink.Pass(value); }"),
            ("Product", "Header.cs", "// <auto-generated/>\npublic sealed class HeaderCaller { private Sink sink = new(); public int Forward(int value) => sink.Pass(value); }"),
            ("Product", "GeneratedSymbol.cs", "public sealed class GeneratedSymbolCaller { private Sink sink = new(); [System.Runtime.CompilerServices.CompilerGenerated] public int Forward(int value) => sink.Pass(value); }"),
            ("Product.Tests", "Tests.cs", "public sealed class Sink { public int Pass(int value) => value; } public sealed class TestCaller { private Sink sink = new(); public int Forward(int value) => sink.Pass(value); }"));

        var production = fixture.Context.Solution.Projects.Single(static project => project.Name == "Product");
        var tests = fixture.Context.Solution.Projects.Single(static project => project.Name == "Product.Tests");
        var productionDeclarations = await TransparentForwardingClassifier.ClassifyProjectAsync(fixture.Context, production, CancellationToken.None);
        var testDeclarations = await TransparentForwardingClassifier.ClassifyProjectAsync(fixture.Context, tests, CancellationToken.None);

        Assert.Contains(productionDeclarations, static declaration => declaration.Symbol.Name == "Forward");
        Assert.DoesNotContain(productionDeclarations, static declaration => declaration.Symbol.ContainingType.Name is "GeneratedCaller" or "HeaderCaller" or "GeneratedSymbolCaller");
        Assert.Contains(testDeclarations, static declaration => declaration.Symbol.Name == "Forward" && declaration.Target is not null);
    }

    [Fact]
    public async Task ClassifyProjectAsync_EvaluatesLinkedDocumentsSeparatelyForEachProject()
    {
        using var fixture = CreateFixture(
            ("ProductA", "Shared.cs", "public sealed class Sink { public int Pass(int value) => value; } public sealed class Caller { private Sink sink = new(); public int Forward(int value) => sink.Pass(value); }"),
            ("ProductB", "Shared.cs", "public sealed class Sink { public int Pass(int value) => value; } public sealed class Caller { private Sink sink = new(); public int Forward(int value) => sink.Pass(value); }"));

        var projects = fixture.Context.Solution.Projects.ToArray();
        var first = await TransparentForwardingClassifier.ClassifyProjectAsync(fixture.Context, projects[0], CancellationToken.None);
        var second = await TransparentForwardingClassifier.ClassifyProjectAsync(fixture.Context, projects[1], CancellationToken.None);

        Assert.Equal(projects[0].Id, Assert.Single(first.Where(static declaration => declaration.Target is not null)).ProjectId);
        Assert.Equal(projects[1].Id, Assert.Single(second.Where(static declaration => declaration.Target is not null)).ProjectId);
    }

    [Fact]
    public async Task ClassifyProjectAsync_MissingSemanticBindingCreatesNoEdge()
    {
        using var fixture = CreateFixture(("Product", "Unbound.cs", "public sealed class Caller { public int Forward(int value) => Missing.Pass(value); }"));

        var declarations = await Classify(fixture);

        Assert.Null(Assert.Single(declarations).Target);
    }

    private static Task<IReadOnlyList<ForwardingDeclaration>> Classify(AnalysisFixture fixture) =>
        TransparentForwardingClassifier.ClassifyProjectAsync(
            fixture.Context,
            fixture.Context.Solution.Projects.Single(),
            CancellationToken.None);

    private static AnalysisFixture CreateFixture(params (string Project, string File, string Source)[] sources)
    {
        var workspace = new FastTestWorkspace();
        var projectIds = sources.Select(static source => source.Project).Distinct(StringComparer.Ordinal)
            .ToDictionary(static name => name, name => workspace.AddProject(name), StringComparer.Ordinal);

        foreach (var source in sources)
        {
            workspace.AddDocument(projectIds[source.Project], source.File, source.Source);
        }

        return new AnalysisFixture(workspace, workspace.CreateReviewContext());
    }

    private sealed class AnalysisFixture(FastTestWorkspace workspace, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose()
        {
            workspace.Dispose();
        }
    }

}
