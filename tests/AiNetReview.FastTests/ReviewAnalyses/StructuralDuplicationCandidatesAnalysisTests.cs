namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class StructuralDuplicationCandidatesAnalysisTests
{
    [Fact]
    public void Descriptor_IsEnabledByDefaultAndRejectsCustomOptions()
    {
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        Assert.Equal("structural-duplication-candidates", analysis.Descriptor.AnalysisId);
        Assert.Equal("Structural Duplication Candidates", analysis.Descriptor.Title);
        Assert.Equal(2, analysis.Descriptor.BehaviorVersion);
        Assert.True(analysis.Descriptor.DefaultEnabled);
        Assert.Empty(analysis.Descriptor.Options);
        Assert.Empty(analysis.Descriptor.ResolveOptions().Values);
        Assert.Throws<ArgumentException>(() => analysis.Descriptor.ResolveOptions(
            [KeyValuePair.Create("minTokens", JsonSerializer.SerializeToElement(60))]));
    }

    [Fact]
    public async Task ExecuteAsync_FindsExactRenamedFragmentsAcrossProjectsAndDifferentSurroundingBodies()
    {
        var sourceA = Wrap("First", BuildListBody("values", "result", "item", "transformed", "beforeA", 1));
        using var fixture = CreateFixture(
            ("Product; One", "first;é.cs", sourceA),
            ("Product Two", "second.cs", Wrap("Second", BuildListBody("items", "copy", "entry", "mapped", "beforeB", 2), "items")));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("Product Two.csproj", finding.ProjectPath);
        Assert.Equal("second.cs", finding.SourcePath);
        Assert.StartsWith("structural-duplicate:", finding.Discriminator, StringComparison.Ordinal);
        Assert.Equal(4, finding.Metrics["statementCount"]);
        Assert.True(finding.Metrics["tokenCount"] >= 60);
        Assert.Equal(2, finding.Metrics["memberCount"]);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.Equal(new[] { "executableCount", "memberCount", "statementCount", "tokenCount" }, finding.Metrics.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, finding.Evidence.Count);
        Assert.Equal(2, finding.RelatedSymbols.Count);
        Assert.Contains("project=\"Product Two.csproj\";start=", finding.Evidence[0].Detail, StringComparison.Ordinal);
        Assert.Contains(";end=", finding.Evidence[0].Detail, StringComparison.Ordinal);
        Assert.Equal(Wrap("Second", BuildListBody("items", "copy", "entry", "mapped", "beforeB", 2), "items").Trim(), finding.Evidence[0].Snippet);

        var validated = await new CurrentFindingValidator().ValidateAndSortAsync(
            analysis.Descriptor.AnalysisId,
            fixture.Context,
            result.Findings,
            CancellationToken.None);
        Assert.Single(validated);
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesFixedTokenAndStatementFloorsAndDistinctOwners()
    {
        var exactlySixty = BuildThresholdStatements((9, false), (8, true), (8, false));
        var fiftyNine = BuildThresholdStatements((8, false), (8, true), (8, true));
        Assert.Equal(60, CountTokens(exactlySixty));
        Assert.Equal(59, CountTokens(fiftyNine));

        using var thresholdFixture = CreateFixture(("Product", "Threshold.cs",
            WrapStatements("AtFloorOne", exactlySixty, "input") + WrapStatements("AtFloorTwo", exactlySixty, "value")
            + WrapStatements("BelowOne", fiftyNine, "input") + WrapStatements("BelowTwo", fiftyNine, "value")));
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        AssertNoCompilationErrors(thresholdFixture.Context);
        var findings = (await analysis.ExecuteAsync(thresholdFixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;
        var finding = Assert.Single(findings);
        Assert.Equal(60, finding.Metrics["tokenCount"]);
        Assert.Equal(3, finding.Metrics["statementCount"]);
        Assert.Equal(2, finding.Metrics["memberCount"]);

        using var oneOwner = CreateFixture(("Product", "OneOwner.cs",
            WrapStatements("RepeatedInsideOneOwner", "{ " + exactlySixty + " } { " + exactlySixty + " }", "input")));
        AssertNoCompilationErrors(oneOwner.Context);
        Assert.Empty((await analysis.ExecuteAsync(oneOwner.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        var twoStatements = BuildThresholdStatements((20, false), (20, false));
        using var statementFloor = CreateFixture(("Product", "TwoStatements.cs",
            WrapStatements("First", twoStatements, "input") + WrapStatements("Second", twoStatements, "value")));
        AssertNoCompilationErrors(statementFloor.Context);
        Assert.Empty((await analysis.ExecuteAsync(statementFloor.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesVariableReuseTypesLiteralsOperatorsAndMemberNames()
    {
        var shared = BuildThresholdStatements((9, false), (8, true), (8, false));
        var changedLiteral = shared.Replace("value0 = input + input", "value0 = input + 7", StringComparison.Ordinal);
        var changedOperator = shared.Replace("value1 = +input + input", "value1 = +input - input", StringComparison.Ordinal);
        var changedType = shared.Replace("int value0", "long value0", StringComparison.Ordinal);
        var reused = BuildReuseStatements("first", "second", "third", "input", "first");
        var separate = BuildReuseStatements("one", "two", "three", "input", "two");

        using var fixture = CreateFixture(("Product", "Retained.cs",
            WrapStatements("Original", shared, "input")
            + WrapStatements("Literal", changedLiteral, "value")
            + WrapStatements("Operator", changedOperator, "value")
            + WrapStatements("Type", changedType, "value")
            + WrapStatements("ReuseA", reused, "input")
            + WrapStatements("ReuseB", separate, "value")));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;
        Assert.Empty(findings);
    }

    [Fact]
    public async Task ExecuteAsync_TreatsLambdaLocalFunctionAndUnboundOrDynamicStatementsAsBarriers()
    {
        var barriers = new[]
        {
            "System.Func<int> deferred = () => input + 1;",
            "int Local() => input;",
            "Unknown<int>();",
            "((dynamic)input).Unknown();",
            "((dynamic)input)[input];",
            "this[input];",
            "Overload<int>(default);",
        };
        var source = string.Concat(barriers.Select((barrier, index) =>
            WrapBarrier("First" + index, "input", barrier) + WrapBarrier("Second" + index, "value", barrier.Replace("input", "value", StringComparison.Ordinal))));
        using var fixture = CreateFixture(("Product", "Barriers.cs", source));
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_AnalyzesLocalFunctionsIndependentlyWithDistinctFallbackIdentities()
    {
        var fragment = BuildThresholdStatements((9, false), (8, true), (8, false));
        var source = "public static class First { public static int Run(int input) { void Local(int value) { "
            + fragment.Replace("input", "value", StringComparison.Ordinal) + " } Local(input); return input; } }"
            + "public static class Second { public static int Run(int input) { void Local(int value) { "
            + fragment.Replace("input", "value", StringComparison.Ordinal) + " } Local(input); return input; } }";
        using var fixture = CreateFixture(("Product", "Locals.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.All(finding.RelatedSymbols, static symbol => Assert.Contains("Local", symbol.SymbolId, StringComparison.Ordinal));
        Assert.NotEqual(finding.RelatedSymbols[0].SymbolId, finding.RelatedSymbols[1].SymbolId);
    }

    [Fact]
    public async Task ExecuteAsync_AnalyzesLocalFunctionsInsideNestedLambdasAndKeepsLambdaStatementAsBarrier()
    {
        var body = BuildThresholdStatements((9, false), (8, true), (8, false)).Replace("input", "value", StringComparison.Ordinal);
        var source = WrapNestedLambdaLocal("First", "input", body) + WrapNestedLambdaLocal("Second", "input", body);
        using var fixture = CreateFixture(("Product", "NestedOwners.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.All(finding.RelatedSymbols, static symbol =>
        {
            Assert.Contains("Local", symbol.SymbolId, StringComparison.Ordinal);
        });
        Assert.NotEqual(finding.RelatedSymbols[0].SymbolId, finding.RelatedSymbols[1].SymbolId);
    }

    [Fact]
    public async Task ExecuteAsync_UsesExactUtf16OffsetsAndOneBasedEndExclusiveEvidenceCoordinates()
    {
        var fragment = BuildThresholdStatements((9, false), (8, true), (8, false));
        var source = "public static class First { public static void Run(int input) { string marker = \"😀\"; " + fragment + " } }\n"
            + "public static class Second { public static void Run(int input) { string marker = \"🚀\"; " + fragment + " } }\n";
        using var fixture = CreateFixture(("Product", "Positions.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var firstOwner = (await tree.GetRootAsync()).DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();
        var statements = firstOwner.Body!.Statements.Skip(1).ToArray();
        var firstToken = statements[0].GetFirstToken();
        var lastToken = statements[^1].GetLastToken();
        var text = await tree.GetTextAsync();
        var start = text.Lines.GetLinePosition(firstToken.SpanStart);
        var end = text.Lines.GetLinePosition(lastToken.Span.End);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal("M:First.Run(System.Int32)", finding.SubjectId);
        Assert.Equal($"structural-duplicate:{firstToken.SpanStart}:{lastToken.Span.End - firstToken.SpanStart}", finding.Discriminator);
        Assert.Equal(start.Line + 1, finding.StartLine);
        Assert.Equal(2, finding.Evidence.Count);
        Assert.Equal($"project=\"Product.csproj\";start={start.Line + 1}:{start.Character + 1};end={end.Line + 1}:{end.Character + 1}", finding.Evidence[0].Detail);
        Assert.Equal("Product.csproj", finding.ProjectPath);
        Assert.Equal("Positions.cs", finding.SourcePath);
    }

    [Fact]
    public async Task ExecuteAsync_AnalyzesConstructorsAndAllPropertyAndEventAccessorsAsOwners()
    {
        var fragment = BuildThresholdStatements((9, false), (8, true), (8, false)).Replace("input", "seed", StringComparison.Ordinal);
        var source = BuildOwnerFixture("OwnerA", fragment) + BuildOwnerFixture("OwnerB", fragment);
        using var fixture = CreateFixture(("Product", "Owners.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        Assert.NotEmpty(findings);
        var ownerIds = findings.SelectMany(static finding => finding.Evidence).Select(static item => item.Label).ToArray();
        Assert.Contains(ownerIds, static id => id.Contains("#ctor", StringComparison.Ordinal));
        Assert.Contains(ownerIds, static id => id.Contains("get_Read", StringComparison.Ordinal));
        Assert.Contains(ownerIds, static id => id.Contains("set_Write", StringComparison.Ordinal));
        Assert.Contains(ownerIds, static id => id.Contains("add_Changed", StringComparison.Ordinal));
        Assert.Contains(ownerIds, static id => id.Contains("remove_Changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UsesNullableReferenceAnnotationsInRecursiveParameterTypeKeys()
    {
        var nullableBody = BuildStringStatements("input");
        var source = "#nullable enable\n"
            + "public static class First { public static void Run(string? input) { " + nullableBody + " } }\n"
            + "public static class Second { public static void Run(string? input) { " + nullableBody + " } }\n"
            + "public static class Third { public static void Run(string input) { " + nullableBody + " } }\n";
        using var fixture = CreateFixture(("Product", "Nullable.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal(2, finding.Metrics["memberCount"]);
        Assert.Contains("First", finding.Evidence[0].Label, StringComparison.Ordinal);
        Assert.Contains("Second", finding.Evidence[1].Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsSameDisplayTypeParametersFromDistinctLocalFunctionDeclarationsSeparate()
    {
        var calls = string.Join(", ", Enumerable.Repeat("item", 12));
        var source = "public static class GenericLocals { "
            + "private static int Count<T>(params T[] values) => values.Length; "
            + $"public static void First() {{ void Local<T>(T item) {{ int a = Count({calls}); int b = Count({calls}); int c = Count({calls}); }} Local(1); }} "
            + $"public static void Second() {{ void Local<T>(T item) {{ int a = Count({calls}); int b = Count({calls}); int c = Count({calls}); }} Local(1); }} }}";
        using var fixture = CreateFixture(("Product", "GenericLocals.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        Assert.Empty((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ExecuteAsync_RetainsBoundNamedArgumentIdentifiersLiterally()
    {
        var firstCall = string.Join(" + ", Enumerable.Repeat("Target(value: input)", 10));
        var secondCall = string.Join(" + ", Enumerable.Repeat("Target(input: input)", 10));
        var source = "public static class First { static int Target(int value) => value; public static void Run(int input) { "
            + $"int a = {firstCall}; int b = {firstCall}; int c = {firstCall}; }}}}\n"
            + "public static class Second { static int Target(int input) => input; public static void Run(int input) { "
            + $"int a = {secondCall}; int b = {secondCall}; int c = {secondCall}; }}}}\n";
        using var fixture = CreateFixture(("Product", "NamedArguments.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        Assert.Empty((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesOperatorDestructorAndExpressionBodiedOwners()
    {
        var fragment = BuildThresholdStatements((9, false), (8, true), (8, false)).Replace("input", "seed", StringComparison.Ordinal);
        var source = "public sealed class First { "
            + $"public static void Expression(int input) => System.Console.WriteLine(input); "
            + $"public static First operator +(First left, First right) {{ int seed = left.GetHashCode(); {fragment} return left; }} "
            + $"~First() {{ int seed = GetHashCode(); {fragment} }} }}\n"
            + "public sealed class Second { "
            + $"public static void Expression(int input) => System.Console.WriteLine(input); "
            + $"public static Second operator +(Second left, Second right) {{ int seed = left.GetHashCode(); {fragment} return left; }} "
            + $"~Second() {{ int seed = GetHashCode(); {fragment} }} }}\n";
        using var fixture = CreateFixture(("Product", "ExcludedOwners.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        Assert.Empty((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesTestProjectFragmentsButExcludesGeneratedDocumentsAndSymbols()
    {
        var body = BuildThresholdStatements((9, false), (8, true), (8, false));
        var generatedSymbolBody = body.Replace("input", "value", StringComparison.Ordinal);
        var production = WrapStatements("RegularOne", body, "input") + WrapStatements("RegularTwo", body, "input")
            + "public static class GeneratedSymbols { "
            + "[System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public static void One(int value) { " + generatedSymbolBody + " } "
            + "[System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public static void Two(int value) { " + generatedSymbolBody + " } }";
        var generatedDocument = WrapStatements("GeneratedDocumentOne", body, "input")
            + WrapStatements("GeneratedDocumentTwo", body, "input");
        var testProject = "public static class TestOne { [Xunit.Fact(Skip = \"deliberately skipped\")] public static void Run(int input) { " + body + " } }"
            + WrapStatements("TestTwo", body, "input");
        using var fixture = CreateFixture(
            ("Product", "Regular.cs", production),
            ("Product", "Generated.g.cs", generatedDocument),
            ("Product.Tests", "Tests.cs", testProject),
            ("Product.Tests", "FactAttribute.cs", "namespace Xunit { [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class FactAttribute : System.Attribute { public string? Skip { get; set; } } }"));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal(4, finding.Metrics["memberCount"]);
        Assert.Equal(new[] { "Tests.cs", "Tests.cs", "Regular.cs", "Regular.cs" }, finding.Evidence.Select(static evidence => evidence.SourcePath));
    }

    [Fact]
    public async Task ExecuteAsync_RetainsProductionOnlyTestOnlyAndMixedFragmentGroupsInFull()
    {
        var productionFragment = BuildThresholdStatements((9, false), (8, true), (8, false));
        var testFragment = productionFragment.Replace(" + input", " - input", StringComparison.Ordinal);
        var mixedFragment = productionFragment.Replace("int value", "long value", StringComparison.Ordinal);
        using var fixture = CreateFixture(
            ("Product", "Production.cs", WrapStatements("ProductionOne", productionFragment, "input")
                + WrapStatements("ProductionTwo", productionFragment, "input")
                + WrapStatements("MixedProduction", mixedFragment, "input")),
            ("Product.Tests", "Tests.cs", WrapStatements("TestOne", testFragment, "input")
                + WrapStatements("TestTwo", testFragment, "input")
                + WrapStatements("MixedTest", mixedFragment, "input")));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        Assert.Equal(new[] { "production,production", "production,tests", "tests,tests" },
            findings.Select(finding => string.Join(',', finding.Evidence.Select(static evidence =>
                evidence.SourcePath == "Tests.cs" ? "tests" : "production").Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));
        Assert.All(findings, static finding => Assert.Equal(2, finding.Metrics["memberCount"]));
        Assert.Equal(6, findings.Sum(static finding => (int)finding.Metrics["memberCount"]));
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesForeachCatchPatternAndDeconstructionDeclarationsBySymbol()
    {
        var source = WrapDeclarationKinds("First", "input", "itemOne", "exceptionOne", "foundOne", "leftOne", "rightOne")
            + WrapDeclarationKinds("Second", "values", "entryTwo", "failureTwo", "matchTwo", "firstTwo", "secondTwo");
        using var fixture = CreateFixture(("Product", "Declarations.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        var finding = Assert.Single(findings, static item => item.Metrics["statementCount"] == 5);
        Assert.Equal(2, finding.Metrics["memberCount"]);
        Assert.Equal(2, finding.Metrics["executableCount"]);
    }

    [Fact]
    public async Task ExecuteAsync_UsesArrayRankAndFunctionPointerSignatureInRecursiveTypeKeys()
    {
        var arrayStatements = BuildArrayStatements("input");
        var pointerStatements = BuildFunctionPointerStatements("input");
        var pointedAtStatements = BuildPointerStatements("input");
        var source = "public static class ArraysA { public static void Run(int[] input) { " + arrayStatements + " } }\n"
            + "public static class ArraysB { public static void Run(int[] input) { " + arrayStatements + " } }\n"
            + "public static class ArraysC { public static void Run(int[,] input) { " + arrayStatements + " } }\n"
            + "public static unsafe class FunctionPointersA { public static void Run(delegate*<int, int> input) { " + pointerStatements + " } }\n"
            + "public static unsafe class FunctionPointersB { public static void Run(delegate*<int, int> input) { " + pointerStatements + " } }\n"
            + "public static unsafe class FunctionPointersC { public static void Run(delegate* unmanaged[Cdecl]<int, int> input) { " + pointerStatements + " } }\n"
            + "public static unsafe class PointersA { public static void Run(int* input) { " + pointedAtStatements + " } }\n"
            + "public static unsafe class PointersB { public static void Run(int* input) { " + pointedAtStatements + " } }\n"
            + "public static unsafe class PointersC { public static void Run(long* input) { " + pointedAtStatements + " } }\n";
        using var fixture = CreateFixture(("Product", "TypeKeys.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, static finding => finding.Evidence.All(static item => item.Label.Contains("Arrays", StringComparison.Ordinal)));
        Assert.Contains(findings, static finding => finding.Evidence.All(static item => item.Label.Contains("FunctionPointers", StringComparison.Ordinal)));
        Assert.Contains(findings, static finding => finding.Evidence.All(static item => item.Label.Contains("Pointers", StringComparison.Ordinal)));
        Assert.All(findings, static finding => Assert.Equal(2, finding.Metrics["memberCount"]));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotMatchFunctionPointerTypesWithDifferentNestedSignatureBoundaries()
    {
        var statements = string.Join(" ", Enumerable.Range(0, 3).Select(_ =>
            string.Join(" = ", Enumerable.Repeat("input", 10)) + ";"));
        Assert.Equal(60, CountTokens(statements));
        var source = "public static unsafe class First { public static void Run(delegate*<int, delegate*<int, void>> input) { " + statements + " } }\n"
            + "public static unsafe class Second { public static void Run(delegate*<delegate*<int, int, void>> input) { " + statements + " } }\n";
        using var fixture = CreateFixture(("Product", "NestedFunctionPointers.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesTopLevelLambdaAndAnonymousMethodBodiesAsOwners()
    {
        var fragment = string.Join(" ", Enumerable.Range(0, 3).Select(_ =>
            string.Join(" = ", Enumerable.Repeat("input", 10)) + ";"));
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        using (var topLevel = CreateFixture(
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true),
            ("Product", "TopLevel.cs", fragment.Replace("input", "args", StringComparison.Ordinal)
                + " public static class Eligible { public static void Run(string[] args) { " + fragment.Replace("input", "args", StringComparison.Ordinal) + " } }")))
        {
            AssertNoCompilationErrors(topLevel.Context);
            var findings = (await analysis.ExecuteAsync(topLevel.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;
            Assert.Empty(findings);
        }

        foreach (var (fileName, callback) in new[]
        {
            ("Lambda.cs", "System.Action<int> callback = input => { " + fragment + " };"),
            ("AnonymousMethod.cs", "System.Action<int> callback = delegate(int input) { " + fragment + " };")
        })
        {
            var source = "public static class Excluded { public static void Run() { " + callback + " } }\n"
                + "public static class Eligible { public static void Run(int input) { " + fragment + " } }\n";
            using var fixture = CreateFixture(("Product", fileName, source));
            AssertNoCompilationErrors(fixture.Context);
            var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;
            Assert.Empty(findings);
        }

        using var eligibleOwners = CreateFixture(("Product", "EligibleOwners.cs",
            "public static class First { public static void Run(int input) { " + fragment + " } }\n"
            + "public static class Second { public static void Run(int input) { " + fragment + " } }\n"));
        AssertNoCompilationErrors(eligibleOwners.Context);
        var positive = (await analysis.ExecuteAsync(eligibleOwners.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;
        var finding = Assert.Single(positive);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.Equal(2, finding.Metrics["memberCount"]);
        Assert.All(finding.Evidence, static evidence => Assert.Contains("Run", evidence.Label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_RetainsContainedGroupWhenOneOccurrenceIsUncoveredAndDoesNotUnionPartialOverlap()
    {
        var firstSequence = Enumerable.Range(1, 5).Select(index => BuildPatternStatement("left" + index, "input", index));
        var shiftedSequence = Enumerable.Range(2, 5).Select(index => BuildPatternStatement("right" + index, "value", index));
        var source = WrapStatements("First", string.Join(" ", firstSequence), "input")
            + WrapStatements("Second", string.Join(" ", Enumerable.Range(1, 5).Select(index => BuildPatternStatement("copy" + index, "input", index))), "input")
            + WrapStatements("Third", string.Join(" ", shiftedSequence), "value");
        using var fixture = CreateFixture(("Product", "Overlap.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        Assert.Contains(findings, static finding => finding.Metrics["statementCount"] == 5 && finding.Metrics["memberCount"] == 2);
        Assert.Contains(findings, static finding => finding.Metrics["statementCount"] == 4 && finding.Metrics["memberCount"] == 3);
        Assert.DoesNotContain(findings, static finding => finding.Metrics["statementCount"] == 3);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsRepeatedRegionsWithinOneOwnerWhenAnotherOwnerAlsoMatches()
    {
        var fragment = BuildThresholdStatements((9, false), (8, true), (8, false));
        var source = WrapStatements("Repeated", "{ " + fragment + " } { " + fragment.Replace("value", "copy", StringComparison.Ordinal) + " }", "input")
            + WrapStatements("Other", fragment, "value");
        using var fixture = CreateFixture(("Product", "RepeatedRegions.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal(3, finding.Metrics["memberCount"]);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.Equal(3, finding.Evidence.Count);
        Assert.Equal(2, finding.RelatedSymbols.Select(static symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ExecuteAsync_FlatLongDuplicateListsRetainEveryQualifyingOccurrenceRange()
    {
        const int statementCount = 100;
        var repeatedAssignment = "input = " + string.Join(" + ", Enumerable.Repeat("input", 12)) + ";";
        var body = string.Join(" ", Enumerable.Repeat(repeatedAssignment, statementCount));
        using var fixture = CreateFixture(("Product", "FlatDuplicates.cs",
            WrapStatements("First", body, "input") + WrapStatements("Second", body, "value")));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var findings = (await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(statementCount, finding.Metrics["statementCount"]);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.Equal(2, finding.Evidence.Count);
        Assert.Equal(2, finding.RelatedSymbols.Count);
        Assert.Equal(new[] { "M:First.Run(System.Int32)", "M:Second.Run(System.Int32)" },
            finding.RelatedSymbols.Select(static occurrence => occurrence.SymbolId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UsesRecursiveNestedGenericAndNullableValueTypeKeys()
    {
        var repeated = "var result = new Outer(); string text = result.ToString(); text = "
            + "text + " + string.Join(" + ", Enumerable.Repeat("text", 30)) + ";";
        var source = "#nullable enable\n"
            + "namespace Model { public class Outer<T> { public class Inner<U> { public override string ToString() => string.Empty; } } }\n"
            + "namespace CloneA { using Outer = Model.Outer<int?>.Inner<string?>; public static class A { public static void Run() { " + repeated.Replace("result", "a", StringComparison.Ordinal).Replace("text", "label", StringComparison.Ordinal) + " } } }\n"
            + "namespace CloneB { using Outer = Model.Outer<int?>.Inner<string?>; public static class B { public static void Run() { " + repeated.Replace("result", "b", StringComparison.Ordinal).Replace("text", "caption", StringComparison.Ordinal) + " } } }\n"
            + "namespace CloneC { using Outer = Model.Outer<int>.Inner<string?>; public static class C { public static void Run() { " + repeated.Replace("result", "c", StringComparison.Ordinal).Replace("text", "message", StringComparison.Ordinal) + " } } }\n";
        using var fixture = CreateFixture(("Product", "NestedTypes.cs", source));
        AssertNoCompilationErrors(fixture.Context);
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        var finding = Assert.Single((await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None)).Findings);

        Assert.Equal(2, finding.Metrics["memberCount"]);
        Assert.Equal(2, finding.Metrics["executableCount"]);
        Assert.Contains("CloneA", finding.Evidence[0].Label, StringComparison.Ordinal);
        Assert.Contains("CloneB", finding.Evidence[1].Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation()
    {
        using var fixture = CreateFixture(("Product", "Methods.cs", Wrap("First", BuildThresholdStatements((9, false), (8, true), (8, false)))
            + Wrap("Second", BuildThresholdStatements((9, false), (8, true), (8, false)))));
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationAfterScanningHasStarted()
    {
        var body = string.Join(" ", Enumerable.Repeat("input++;", 1_000));
        using var fixture = CreateFixture(("Product", "Large.cs", WrapStatements("Large", body, "input")));
        var project = fixture.Context.Solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await project.Documents.Single().GetSyntaxTreeAsync();
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        using var cancellation = new CancellationTokenSource();

        var execution = Task.Run(() => analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), cancellation.Token));
        await Task.Delay(20);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);
    }

    [Fact]
    public async Task ExecuteAsync_FailsInsteadOfReturningPartialResultsWhenAProjectHasNoPath()
    {
        using var root = TestTempDirectory.Create();
        using var workspace = new AdhocWorkspace();
        var project = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "MissingPath", "MissingPath",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: FastTestReferences.CreatePlatformReferences());
        workspace.AddProject(project);
        workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project.Id), "Valid.cs",
            filePath: Path.Combine(root.DirectoryPath, "Valid.cs"),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("public class A { public void Run() { } }"), VersionStamp.Create()))));
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        await Assert.ThrowsAsync<AnalysisFailedException>(() => analysis.ExecuteAsync(
            new ReviewContext(workspace.CurrentSolution, root.DirectoryPath), analysis.Descriptor.ResolveOptions(), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotReturnEarlierGroupsWhenALaterSourceFailsPathValidation()
    {
        using var root = TestTempDirectory.Create();
        using var workspace = new AdhocWorkspace();
        var goodId = ProjectId.CreateNewId();
        var badId = ProjectId.CreateNewId();
        var references = FastTestReferences.CreatePlatformReferences();
        workspace.AddProject(ProjectInfo.Create(goodId, VersionStamp.Create(), "AProduct", "AProduct", LanguageNames.CSharp,
            filePath: Path.Combine(root.DirectoryPath, "AProduct.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true), metadataReferences: references));
        workspace.AddProject(ProjectInfo.Create(badId, VersionStamp.Create(), "ZProduct", "ZProduct", LanguageNames.CSharp,
            filePath: Path.Combine(root.DirectoryPath, "ZProduct.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true), metadataReferences: references));
        workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(goodId), "Good.cs",
            filePath: Path.Combine(root.DirectoryPath, "Good.cs"),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(
                WrapStatements("First", BuildThresholdStatements((9, false), (8, true), (8, false)), "input")
                + WrapStatements("Second", BuildThresholdStatements((9, false), (8, true), (8, false)), "value")), VersionStamp.Create()))));
        workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(badId), "Outside.cs",
            filePath: Path.Combine(root.DirectoryPath, "..", "Outside.cs"),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("public class Outside { }"), VersionStamp.Create()))));
        var analysis = new StructuralDuplicationCandidatesAnalysis();

        await Assert.ThrowsAsync<AnalysisFailedException>(() => analysis.ExecuteAsync(
            new ReviewContext(workspace.CurrentSolution, root.DirectoryPath), analysis.Descriptor.ResolveOptions(), CancellationToken.None));
    }

    private static string BuildListBody(string parameter, string result, string item, string transformed, string prefix, int salt) =>
        $"int {prefix} = {parameter}.Length + {salt}; "
        + $"var {result} = new System.Collections.Generic.List<int>(); "
        + $"foreach (var {item} in {parameter}) {{ if ({item} < 0) continue; var {transformed} = ({item} * 2) + 1; if ({transformed} > 1000) continue; {result}.Add({transformed}); }} "
        + $"{result}.Sort(); return {result};";

    private static string BuildThresholdStatements(params (int Terms, bool UnaryPlus)[] terms) =>
        string.Join(" ", terms.Select((entry, index) =>
        {
            var expression = string.Join(" + ", Enumerable.Repeat("input", entry.Terms));
            if (entry.UnaryPlus) expression = "+" + expression;
            return $"int value{index} = {expression};";
        }));

    private static string BuildReuseStatements(string first, string second, string third, string parameter, string secondSource)
    {
        var firstExpression = "+" + string.Join(" + ", Enumerable.Repeat(parameter, 8));
        var secondExpression = "+" + string.Join(" + ", Enumerable.Repeat(first, 8));
        var thirdTerms = Enumerable.Range(0, 8).Select(index => index == 1 ? secondSource : first);
        var thirdExpression = "+" + string.Join(" + ", thirdTerms);
        return $"int {first} = {firstExpression}; int {second} = {secondExpression}; int {third} = {thirdExpression};";
    }

    private static string BuildPatternStatement(string name, string parameter, int salt) =>
        $"int {name} = +{parameter} + " + string.Join(" + ", Enumerable.Repeat(parameter, 7)) + $" + {salt};";

    private static string BuildStringStatements(string parameter) => string.Join(" ", Enumerable.Range(0, 3).Select(index =>
        $"string? value{index} = {string.Join(" + ", Enumerable.Repeat(index == 0 ? parameter : $"value{index - 1}", 20))};"));

    private static string BuildOwnerFixture(string typeName, string fragment) =>
        $"public sealed class {typeName} {{ "
        + $"public {typeName}(int input) {{ int seed = input; {fragment} }} "
        + $"public int Read {{ get {{ int seed = GetHashCode(); {fragment} return seed; }} }} "
        + $"public int Write {{ set {{ int seed = value; {fragment} }} }} "
        + $"public event System.EventHandler? Changed {{ add {{ int seed = value.GetHashCode(); {fragment} }} remove {{ int seed = value.GetHashCode(); {fragment} }} }} }}\n";

    private static string BuildArrayStatements(string parameter) => string.Join(" ", Enumerable.Range(0, 3).Select(index =>
        $"int value{index} = {string.Join(" + ", Enumerable.Repeat($"{parameter}.Length", 24))};"));

    private static string BuildFunctionPointerStatements(string parameter) => string.Join(" ", Enumerable.Range(0, 3).Select(index =>
        $"int value{index} = {string.Join(" + ", Enumerable.Repeat($"{parameter}(1)", 14))};"));

    private static string BuildPointerStatements(string parameter) => string.Join(" ", Enumerable.Range(0, 3).Select(index =>
        $"bool value{index} = {string.Join(" || ", Enumerable.Repeat($"{parameter} == null", 14))};"));

    private static string WrapDeclarationKinds(
        string typeName,
        string parameter,
        string foreachName,
        string exceptionName,
        string patternName,
        string leftName,
        string rightName)
    {
        var foreachBody = $"foreach (var {foreachName} in {parameter}) {{ int sum = {string.Join(" + ", Enumerable.Repeat(foreachName, 12))}; }}";
        var catchBody = $"try {{ }} catch (System.Exception {exceptionName}) {{ int code = {string.Join(" + ", Enumerable.Repeat(exceptionName + ".HResult", 12))}; }}";
        var patternBody = $"if ({parameter} is object {patternName}) {{ int hash = {string.Join(" + ", Enumerable.Repeat(patternName + ".GetHashCode()", 12))}; }}";
        var deconstruction = $"var ({leftName}, {rightName}) = ({parameter}.Length, {parameter}.Length);";
        var use = $"int combined = {string.Join(" + ", Enumerable.Repeat(leftName + " + " + rightName, 12))};";
        return $"public static class {typeName} {{ public static void Run(int[] {parameter}) {{ {foreachBody} {catchBody} {patternBody} {deconstruction} {use} }} }}\n";
    }

    private static string WrapNestedLambdaLocal(string name, string parameter, string localBody) =>
        $"public static class {name} {{ public static void Run(int {parameter}) {{ "
        + $"int beforeOne = {parameter} + {parameter}; int beforeTwo = {parameter} + {parameter}; "
        + $"System.Action callback = () => {{ int Local(int value) {{ {localBody} return value; }} _ = Local({parameter}); }}; "
        + $"int afterOne = {parameter} + {parameter}; int afterTwo = {parameter} + {parameter}; }} }}\n";

    private static string WrapBarrier(string typeName, string parameter, string barrier)
    {
        var prefix = BuildBarrierStatement("beforeOne", parameter) + " " + BuildBarrierStatement("beforeTwo", parameter);
        var suffix = BuildBarrierStatement("afterOne", parameter) + " " + BuildBarrierStatement("afterTwo", parameter);
        return $"public class {typeName} {{ public static void Overload<T>(int value) {{ }} public static void Overload<T>(string value) {{ }} "
            + $"public void Run(int {parameter}) {{ {prefix} {barrier} {suffix} }} }}\n";
    }

    private static string BuildBarrierStatement(string name, string parameter) =>
        $"int {name} = " + string.Join(" + ", Enumerable.Repeat(parameter, 18)) + ";";

    private static int CountTokens(string statements) =>
        CSharpSyntaxTree.ParseText("class C { void M(int input) { " + statements + " } }").GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single()
            .Body!.Statements.SelectMany(static statement => statement.DescendantTokens())
            .Count(static token => !token.IsMissing);

    private static string Wrap(string typeName, string body, string parameter = "values") =>
        $"public static class {typeName} {{ public static System.Collections.Generic.List<int> Run(int[] {parameter}) {{ {body} }} }}\n";

    private static string WrapStatements(string name, string statements, string parameter) =>
        $"public static class {name} {{ public static void Run(int {parameter}) {{ {statements.Replace("input", parameter, StringComparison.Ordinal)} }} }}\n";

    private static void AssertNoCompilationErrors(ReviewContext context)
    {
        foreach (var project in context.Solution.Projects)
        {
            var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
            Assert.NotNull(compilation);
            Assert.DoesNotContain(compilation!.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }

    private static AnalysisFixture CreateFixture(params (string Project, string File, string Source)[] documents) =>
        CreateFixture(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true), documents);

    private static AnalysisFixture CreateFixture(
        CSharpCompilationOptions compilationOptions,
        params (string Project, string File, string Source)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
        var groups = documents.GroupBy(static document => document.Project, StringComparer.Ordinal).ToArray();
        var projectIds = groups.ToDictionary(static group => group.Key, static _ => ProjectId.CreateNewId(), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            workspace.AddProject(ProjectInfo.Create(
                projectIds[group.Key],
                VersionStamp.Create(),
                group.Key,
                group.Key,
                LanguageNames.CSharp,
                filePath: Path.Combine(root.DirectoryPath, group.Key + ".csproj"),
                compilationOptions: compilationOptions,
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: FastTestReferences.CreatePlatformReferences()));
        }

        foreach (var document in documents)
        {
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectIds[document.Project]),
                document.File,
                filePath: Path.Combine(root.DirectoryPath, document.File),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(document.Source), VersionStamp.Create()))));
        }

        if (!workspace.TryApplyChanges(workspace.CurrentSolution))
        {
            throw new InvalidOperationException("Could not initialize Roslyn test workspace.");
        }

        return new AnalysisFixture(workspace, new ReviewContext(workspace.CurrentSolution, root.DirectoryPath), root);
    }

    private sealed class AnalysisFixture(AdhocWorkspace workspace, ReviewContext context, IDisposable root) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose()
        {
            workspace.Dispose();
            root.Dispose();
        }
    }
}
