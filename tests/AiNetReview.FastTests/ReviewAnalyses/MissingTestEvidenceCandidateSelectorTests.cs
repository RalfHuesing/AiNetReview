namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class MissingTestEvidenceCandidateSelectorTests
{
    [Fact]
    public async Task SelectAsync_IncludesEverySpecifiedExecutableFunctionKindAndExpressionBodies()
    {
        const string source = """
            using System;
            public partial class Example
            {
                public void Method() { if (true) { } }
                private void PrivateMethod() { if (true) { } }
                public Example() { if (true) { } }
                public int Property { get { if (true) { } return 0; } set { if (true) { } } }
                public int ExpressionProperty => true ? 1 : 0;
                public int InitProperty { get; init { if (value > 0) { } } }
                public int this[int index] { get => index > 0 ? index : 0; set { if (value > 0) { } } }
                public event Action Changed { add { if (value is not null) { } } remove { if (value is not null) { } } }
                public static Example operator +(Example left, Example right) { if (left is not null) { } return left; }
                public static implicit operator int(Example value) => value is null ? 0 : 1;
                partial void Implemented();
                partial void Implemented() { if (true) { } }
                partial void DeclarationOnly();
                public void NestedBodiesOnly()
                {
                    void Local() { if (true) { } }
                    Func<bool> callback = () => true ? true : false;
                }
            }
            """;
        using var workspace = new FastTestWorkspace();
        var project = AddProjectWithDocument(workspace, "Example", source);
        Assert.False(ReviewSourceClassifier.IsTestProject(project), project.FilePath);
        Assert.Single(project.Documents);

        var candidates = await SelectAsync(workspace.Solution, minDecisionCount: 1, minDecisionNesting: 1);
        var methods = candidates.Select(static candidate => candidate.Method).ToArray();

        Assert.Contains(methods, static method => method.Name == "Method" && method.MethodKind == MethodKind.Ordinary);
        Assert.Contains(methods, static method => method.Name == "PrivateMethod" && method.DeclaredAccessibility == Accessibility.Private);
        Assert.Contains(methods, static method => method.MethodKind == MethodKind.Constructor);
        Assert.Contains(methods, static method => method.Name == "get_Property" && method.MethodKind == MethodKind.PropertyGet);
        Assert.Contains(methods, static method => method.Name == "set_Property" && method.MethodKind == MethodKind.PropertySet);
        Assert.Contains(methods, static method => method.Name == "get_ExpressionProperty" && method.MethodKind == MethodKind.PropertyGet);
        Assert.Contains(methods, static method => method.Name == "set_InitProperty" && method.MethodKind == MethodKind.PropertySet);
        Assert.Contains(methods, static method => method.Name == "get_Item" && method.MethodKind == MethodKind.PropertyGet);
        Assert.Contains(methods, static method => method.Name == "set_Item" && method.MethodKind == MethodKind.PropertySet);
        Assert.Contains(methods, static method => method.MethodKind == MethodKind.EventAdd);
        Assert.Contains(methods, static method => method.MethodKind == MethodKind.EventRemove);
        Assert.Contains(methods, static method => method.MethodKind == MethodKind.UserDefinedOperator);
        Assert.Contains(methods, static method => method.MethodKind == MethodKind.Conversion);
        Assert.Contains(methods, static method => method.Name == "Implemented");
        Assert.Single(methods.Where(static method => method.Name == "Implemented"));
        Assert.DoesNotContain(methods, static method => method.Name is "DeclarationOnly" or "Local" or "NestedBodiesOnly");
    }

    [Fact]
    public async Task SelectAsync_IncludesExpressionBodiedIndexerGetter()
    {
        using var workspace = new FastTestWorkspace();
        AddProjectWithDocument(workspace, "Example", "public sealed class Example { public int this[int index] => index > 0 ? index : 0; }");

        var candidates = await SelectAsync(workspace.Solution, minDecisionCount: 1, minDecisionNesting: 1);

        var getter = Assert.Single(candidates);
        Assert.Equal("get_Item", getter.Method.Name);
        Assert.Equal(MethodKind.PropertyGet, getter.Method.MethodKind);
        Assert.Equal(1, getter.Measurement.DecisionCount);
    }

    [Fact]
    public async Task SelectAsync_UsesSharedMetricsForBothOrGatesAndIndirectAndCombination()
    {
        const string source = """
            public class Example
            {
                public void CountGate()
                {
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                }

                public void NestingGate()
                {
                    if (true)
                    {
                        if (true)
                        {
                            if (true) { }
                        }
                    }
                }

                public void IndirectCountGate()
                {
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                }

                public void IndirectNestingGate()
                {
                    if (true)
                    {
                        if (true)
                        {
                            if (true)
                            {
                                if (true) { }
                            }
                        }
                    }
                }

                public void BelowNontrivialButAboveIndirect()
                {
                    if (true) { }
                }
            }
            """;
        using var workspace = new FastTestWorkspace();
        AddProjectWithDocument(workspace, "Example", source);

        var candidates = await MissingTestEvidenceCandidateSelector.SelectAsync(
            workspace.Solution,
            minDecisionCount: 5,
            minDecisionNesting: 3,
            minIndirectDecisionCount: 6,
            minIndirectDecisionNesting: 4,
            CancellationToken.None);

        var byName = candidates.ToDictionary(static candidate => candidate.Method.Name, StringComparer.Ordinal);
        Assert.Equal(5, byName["CountGate"].Measurement.DecisionCount);
        Assert.Equal(1, byName["CountGate"].Measurement.MaxDecisionNesting);
        Assert.False(byName["CountGate"].MeetsIndirectThresholds);
        Assert.Equal(3, byName["NestingGate"].Measurement.DecisionCount);
        Assert.Equal(3, byName["NestingGate"].Measurement.MaxDecisionNesting);
        Assert.False(byName["NestingGate"].MeetsIndirectThresholds);
        Assert.True(byName["IndirectCountGate"].MeetsIndirectThresholds);
        Assert.True(byName["IndirectNestingGate"].MeetsIndirectThresholds);
        Assert.DoesNotContain(byName.Keys, static name => name == "BelowNontrivialButAboveIndirect");

        var reversedThresholdCandidates = await MissingTestEvidenceCandidateSelector.SelectAsync(
            workspace.Solution,
            minDecisionCount: 5,
            minDecisionNesting: 4,
            minIndirectDecisionCount: 1,
            minIndirectDecisionNesting: 1,
            CancellationToken.None);
        Assert.DoesNotContain(reversedThresholdCandidates, static candidate => candidate.Method.Name == "BelowNontrivialButAboveIndirect");
    }

    [Fact]
    public async Task SelectAsync_ExcludesGeneratedDocumentsSymbolsAndTestProjects()
    {
        using var workspace = new FastTestWorkspace();
        var project = AddProjectWithDocument(
            workspace,
            "Example",
            "using System.CodeDom.Compiler; public class Example { public void Included() { if (true) { } } [GeneratedCode(\"test\", \"1\")] public void GeneratedMethod() { if (true) { } } [System.Runtime.CompilerServices.CompilerGenerated] public void CompilerGeneratedMethod() { if (true) { } } } [GeneratedCode(\"test\", \"1\")] public class GeneratedType { public void GeneratedTypeMethod() { if (true) { } } }");
        AddDocument(workspace, project.Id, "Generated.g.cs", "public class GeneratedDocument { public void GeneratedDocumentMethod() { if (true) { } } }");
        AddProjectWithDocument(workspace, "Example.Tests", "public class TestProject { public void Excluded() { if (true) { } } }");

        var candidates = await SelectAsync(workspace.Solution, minDecisionCount: 1, minDecisionNesting: 1);

        Assert.Equal(["Included"], candidates.Select(static candidate => candidate.Method.Name));
    }

    private static Task<IReadOnlyList<MissingTestEvidenceFunctionCandidate>> SelectAsync(
        Solution solution,
        int minDecisionCount,
        int minDecisionNesting) => MissingTestEvidenceCandidateSelector.SelectAsync(
            solution,
            minDecisionCount,
            minDecisionNesting,
            minIndirectDecisionCount: 5,
            minIndirectDecisionNesting: 3,
            CancellationToken.None);

    private static Project AddProjectWithDocument(FastTestWorkspace workspace, string name, string source)
    {
        var projectId = workspace.AddProject(name, parseOptions: CSharpParseOptions.Default);
        workspace.AddDocument(projectId, "Source.cs", source,
            Path.Combine(workspace.RootPath, name, "Source.cs"));
        return workspace.Solution.GetProject(projectId)!;
    }

    private static void AddDocument(FastTestWorkspace workspace, ProjectId projectId, string name, string source, string? path = null) =>
        workspace.AddDocument(projectId, name, source, path ?? Path.Combine(
            workspace.RootPath, workspace.Solution.GetProject(projectId)!.Name, name));

}
