namespace AiNetReview.FastTests.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class DuplicateCodeDetectorTests
{
    [Fact]
    public async Task ScanAsync_IdenticalMethodsAcrossProjectsFormExactClusterWithStableOrder()
    {
        using var fixture = CreateFixture(
            ("ProductA", "A.cs", Wrap("First", BuildBody())),
            ("ProductB", "B.cs", Wrap("Second", BuildBody())));

        var first = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");
        var second = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");

        var cluster = Assert.Single(first);
        Assert.Equal(1.0, cluster.Score);
        Assert.Equal(new[] { "A.cs", "B.cs" }, cluster.Members.Select(static member => member.SourcePath));
        Assert.Equal(first.Select(Describe), second.Select(Describe));
    }

    [Fact]
    public async Task ScanAsync_RetainsProductionOnlyTestOnlyAndMixedClustersInFull()
    {
        var productionBody = BuildBody();
        var testBody = productionBody.Replace("v", "t", StringComparison.Ordinal);
        var mixedBody = productionBody.Replace("v", "m", StringComparison.Ordinal);
        using var fixture = CreateFixture(
            ("Product", "Production.cs", Wrap("ProductionOne", productionBody) + Wrap("ProductionTwo", productionBody) + Wrap("MixedProduction", mixedBody)),
            ("Product.Tests", "Tests.cs", Wrap("TestOne", testBody) + Wrap("TestTwo", testBody) + Wrap("MixedTest", mixedBody)));

        var clusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");

        Assert.Equal(new[] { "production,production", "production,tests", "tests,tests" },
            clusters.Select(cluster => string.Join(',', cluster.Members.Select(static member =>
                member.ProjectPath.Contains(".Tests", StringComparison.Ordinal) ? "tests" : "production").Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));
        Assert.All(clusters, static cluster => Assert.Equal(2, cluster.Members.Count));
        Assert.Equal(6, clusters.Sum(static cluster => cluster.Members.Count));
    }

    [Fact]
    public async Task ScanAsync_WhitespaceAndCommentsDoNotChangeExactTokenFingerprints()
    {
        var compact = Wrap("Compact", BuildBody());
        var formatted = Wrap("Formatted", BuildBody().Replace("; ", ";\n        // comment\n        ", StringComparison.Ordinal));
        using var fixture = CreateFixture(("Product", "Methods.cs", compact + formatted));

        var clusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");

        Assert.Equal(1.0, Assert.Single(clusters).Score);
    }

    [Theory]
    [InlineData("Identifier", "renamed", "fuzzy")]
    [InlineData("Literal", "999", "fuzzy")]
    public async Task ScanAsync_IdentifiersAndLiteralsRemainPartOfTokens(string changedKind, string replacement, string level)
    {
        var changedBody = changedKind == "Identifier"
            ? BuildBody().Replace("var v3 =", $"var {replacement} =", StringComparison.Ordinal)
                .Replace("= v3 +", $"= {replacement} +", StringComparison.Ordinal)
            : BuildBody().Replace("v2 + 3", $"v2 + {replacement}", StringComparison.Ordinal);
        using var fixture = CreateFixture(
            ("Product", "A.cs", Wrap("First", BuildBody()) + Wrap("Second", changedBody)));

        var exact = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");

        Assert.Empty(exact);
        var fuzzy = Assert.Single(await DuplicateCodeDetector.ScanAsync(fixture.Context, level));
        Assert.True(fuzzy.Score < 1.0);
    }

    [Fact]
    public async Task ScanAsync_RespectsThirtyTokenBoundary()
    {
        using var fixture = CreateFixture(("Product", "Methods.cs",
            Wrap("Below", BuildBoundaryBody(29)) + Wrap("At", BuildBoundaryBody(30)) + Wrap("AtAgain", BuildBoundaryBody(30))));

        var clusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");

        var cluster = Assert.Single(clusters);
        Assert.Equal(2, cluster.Members.Count);
        Assert.DoesNotContain(cluster.Members, static member => member.Identity.Contains("Below", StringComparison.Ordinal));
        Assert.All(cluster.Members, static member => Assert.Equal(30, member.TokenCount));
    }

    [Fact]
    public async Task ScanAsync_UsesSelectedThresholdAndDoesNotLetWeakNeighborRemoveExactPair()
    {
        var exactBody = BuildBody();
        var nearBody = ReplaceStatements(exactBody, (8, "var i = a * 7;"));
        using var fixture = CreateFixture(("Product", "Methods.cs",
            Wrap("A", exactBody) + Wrap("B", exactBody) + Wrap("C", nearBody)));

        var exactClusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");
        var nearClusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "near");

        var exact = Assert.Single(exactClusters);
        Assert.Equal(2, exact.Members.Count);
        Assert.Contains(exact.Members, static member => member.Identity.Contains("A", StringComparison.Ordinal));
        Assert.Contains(exact.Members, static member => member.Identity.Contains("B", StringComparison.Ordinal));
        Assert.DoesNotContain(exact.Members, static member => member.Identity.Contains("C", StringComparison.Ordinal));
        Assert.All(nearClusters, static cluster => Assert.InRange(cluster.Score, 0.80, 1.0));
        Assert.Contains(nearClusters.SelectMany(static cluster => cluster.Members), static member => member.Identity.Contains("C", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_FormsFuzzyPairsOnlyWhenFuzzyIsSelected()
    {
        var body = BuildBody();
        var variant = ReplaceStatements(body,
            (1, "var v2 = v1 * 9;"), (6, "var v7 = v6 * 10;"), (11, "var v12 = v11 * 11;"), (17, "var v18 = v17 * 12;"));
        using var fixture = CreateFixture(("Product", "Methods.cs", Wrap("Base", body) + Wrap("Fuzzy", variant)));

        Assert.Empty(await DuplicateCodeDetector.ScanAsync(fixture.Context, "near"));
        var cluster = Assert.Single(await DuplicateCodeDetector.ScanAsync(fixture.Context, "fuzzy"));
        Assert.InRange(cluster.Score, 0.65, 0.7999);
    }

    [Fact]
    public async Task ScanAsync_RejectsUnrelatedBodiesBelowFuzzyThreshold()
    {
        var body = BuildBody();
        var variant = ReplaceStatements(body,
            (0, "var v1 = value * 11;"), (3, "var v4 = v3 * 12;"), (6, "var v7 = v6 * 13;"),
            (9, "var v10 = v9 * 14;"), (12, "var v13 = v12 * 15;"), (18, "var v19 = v18 * 16;"));
        using var fixture = CreateFixture(("Product", "Methods.cs", Wrap("Base", body) + Wrap("Other", variant)));

        Assert.Empty(await DuplicateCodeDetector.ScanAsync(fixture.Context, "fuzzy"));
    }

    [Fact]
    public async Task ScanAsync_ClustersTransitivelyFromOnlyEdgesMeetingSelectedThreshold()
    {
        var body = BuildBody();
        var nearOne = ReplaceStatements(body, (8, "var i = a * 7;"));
        var nearTwo = ReplaceStatements(body, (14, "var o = a * 8;"));
        using var fixture = CreateFixture(("Product", "Methods.cs",
            Wrap("Base", body) + Wrap("NearOne", nearOne) + Wrap("NearTwo", nearTwo)));

        var cluster = Assert.Single(await DuplicateCodeDetector.ScanAsync(fixture.Context, "near"));

        Assert.Equal(3, cluster.Members.Count);
        Assert.InRange(cluster.Score, 0.80, 1.0);
    }

    [Fact]
    public async Task ScanAsync_IncludesMethodsConstructorsAccessorsAndLocalFunctions()
    {
        var body = BuildBody();
        var source = Wrap("Ordinary", body)
            + "public sealed class Constructed { public Constructed() { " + BodyStatements(body) + " } }\n"
            + "public sealed class Property { public int Value { get { " + BodyStatements(body) + " return 1; } } }\n"
            + "public static class Locals { public static int Run() { int Local(int value) { " + BodyStatements(body) + " return 1; } return Local(1); } }\n";
        using var fixture = CreateFixture(("Product", "Declarations.cs", source));

        var clusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");

        Assert.Contains(clusters.SelectMany(static cluster => cluster.Members), static member => member.Identity.Contains("#ctor", StringComparison.Ordinal));
        Assert.Contains(clusters.SelectMany(static cluster => cluster.Members), static member => member.Identity.Contains("get_Value", StringComparison.Ordinal));
        Assert.Contains(clusters.SelectMany(static cluster => cluster.Members), static member => member.Identity.Contains("Local", StringComparison.Ordinal));
        Assert.Contains(clusters.SelectMany(static cluster => cluster.Members), static member => member.Identity.Contains("Ordinary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_IncludesExpressionBodiedMethodsAccessorsAndLocalFunctions()
    {
        var expression = string.Join(" + ", Enumerable.Repeat("value + 1", 12));
        var localExpression = expression.Replace("value", "item", StringComparison.Ordinal);
        var source = "public static class Expressions { public static int First(int value) => " + expression
            + "; public static int Second(int value) => " + expression
            + "; public int Value { get => " + expression
            + "; } public static int Run(int value) { int LocalOne(int item) => " + localExpression
            + "; int LocalTwo(int item) => " + localExpression
            + "; return LocalOne(value) + LocalTwo(value); } }";
        using var fixture = CreateFixture(("Product", "Expressions.cs", source));

        var clusters = await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact");
        var members = clusters.SelectMany(static cluster => cluster.Members).ToArray();

        Assert.Contains(members, static member => member.Identity.Contains("First", StringComparison.Ordinal));
        Assert.Contains(members, static member => member.Identity.Contains("Second", StringComparison.Ordinal));
        Assert.Contains(members, static member => member.Identity.Contains("get_Value", StringComparison.Ordinal));
        Assert.Contains(members, static member => member.Identity.Contains("Local", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_IncludesProductionAndTestMethodsButExcludesGeneratedDocumentsAndSymbols()
    {
        var body = BuildBody();
        using var fixture = CreateFixture(
            ("Product", "A.cs", Wrap("First", body)),
            ("Product.Tests", "Test.cs", "namespace Xunit { [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class FactAttribute : System.Attribute { public string? Skip { get; set; } } } public static class TestFixture { [Xunit.Fact(Skip = \"deliberately skipped\")] public static int TestDuplicate(int value) { " + BodyStatements(body) + " return v20; } }"),
            ("Product", "Generated.g.cs", Wrap("FileGenerated", body)),
            ("Product", "HeaderGenerated.cs", "// <auto-generated/>\n" + Wrap("HeaderGenerated", body)),
            ("Product", "Attributed.cs", "[System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public static class Attributed { public static void Run() { " + BodyStatements(body) + " } }"),
            ("Product", "B.cs", Wrap("Second", body)));

        var cluster = Assert.Single(await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact"));

        Assert.Equal(new[] { "Test.cs", "A.cs", "B.cs" }, cluster.Members.Select(static member => member.SourcePath));
    }

    [Fact]
    public async Task ScanAsync_CrossProjectPairIsCollectedAndCancellationPropagates()
    {
        using var fixture = CreateFixture(("ProductA", "A.cs", Wrap("First", BuildBody())), ("ProductB", "B.cs", Wrap("Second", BuildBody())));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DuplicateCodeDetector.ScanAsync(fixture.Context, "exact", cancellationToken: cancellation.Token));
        Assert.Equal(2, Assert.Single(await DuplicateCodeDetector.ScanAsync(fixture.Context, "exact")).Members.Count);
    }

    [Fact]
    public async Task ScanAsync_PathFailureThrowsInsteadOfReturningClustersCollectedEarlier()
    {
        using var fixture = CreateFixture(("ProductA", "A.cs", Wrap("First", BuildBody())), ("ProductB", "B.cs", Wrap("Second", BuildBody())));
        var secondProjectId = fixture.Context.Solution.Projects.Single(static project => project.Name == "ProductB").Id;
        var invalidSolution = fixture.Context.Solution.WithProjectFilePath(secondProjectId, Path.Combine(Path.GetTempPath(), "Outside.csproj"));
        var invalidContext = new ReviewContext(invalidSolution, fixture.Context.ProjectRoot);

        await Assert.ThrowsAsync<AnalysisFailedException>(() => DuplicateCodeDetector.ScanAsync(invalidContext, "exact"));
    }

    private static string Describe(DuplicateCodeDetector.DuplicateCodeCluster cluster) =>
        $"{cluster.Score:R}|{string.Join(",", cluster.Members.Select(static member => member.ProjectPath + ":" + member.SourcePath + ":" + member.Identity))}";

    private static string BuildBody() => string.Join(" ", Enumerable.Range(1, 20).Select(index =>
        $"var {ToIdentifier(index)} = {(index == 1 ? "value" : ToIdentifier(index - 1))} + {index};")) + " return " + ToIdentifier(20) + ";";

    private static string BuildBoundaryBody(int tokenCount)
    {
        var assignmentCount = tokenCount == 30 ? 5 : 4;
        var statements = string.Join(" ", Enumerable.Range(0, assignmentCount).Select(index => $"int value{index} = {index};"));
        var emptyStatements = tokenCount == 29 ? " ; ; ; ;" : string.Empty;
        return statements + emptyStatements + " return 1;";
    }

    private static string Wrap(string typeName, string body) =>
        $"public static class {typeName} {{ public static int Run(int value) {{ {body} }} }}\n";

    private static string BodyStatements(string body) => body.Replace("return v20;", string.Empty, StringComparison.Ordinal);

    private static string ReplaceStatements(string body, params (int Index, string Replacement)[] replacements)
    {
        var statements = body.Split(" ", StringSplitOptions.RemoveEmptyEntries).Chunk(6).Select(static part => string.Join(" ", part)).ToArray();
        foreach (var replacement in replacements)
        {
            statements[replacement.Index] = replacement.Replacement;
        }

        return string.Join(" ", statements);
    }

    private static string ToIdentifier(int index) => $"v{index}";

    private static AnalysisFixture CreateFixture(params (string Project, string File, string Source)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
        var groups = documents.GroupBy(static document => document.Project, StringComparer.Ordinal).ToArray();
        var projectIds = groups.ToDictionary(static group => group.Key, static _ => ProjectId.CreateNewId(), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            workspace.AddProject(ProjectInfo.Create(
                projectIds[group.Key], VersionStamp.Create(), group.Key, group.Key, LanguageNames.CSharp,
                filePath: Path.Combine(root.DirectoryPath, group.Key + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: PlatformReferences()));
        }

        foreach (var document in documents)
        {
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectIds[document.Project]), document.File,
                filePath: Path.Combine(root.DirectoryPath, document.File),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(document.Source), VersionStamp.Create()))));
        }

        if (!workspace.TryApplyChanges(workspace.CurrentSolution))
        {
            throw new InvalidOperationException("Could not initialize Roslyn test workspace.");
        }

        return new AnalysisFixture(workspace, new ReviewContext(workspace.CurrentSolution, root.DirectoryPath), root);
    }

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
