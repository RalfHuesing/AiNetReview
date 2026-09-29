namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.IO;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

public sealed class MethodControlFlowOutliersAnalysisTests
{
    [Fact]
    public async Task ExecuteAsync_UsesNearestRankAndReportsEveryTieAtCutoff()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public void First()
                {
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                }
                public void Second()
                {
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                }
                public void Third() { }
                public void Fourth() { }
                public void Fifth() { }
                public void Sixth() { }
                public void Seventh() { }
                public void Eighth() { }
                public void Ninth() { }
                public void Tenth() { }
            }
            """;
        using var fixture = CreateContext(source);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal("src/Example.cs", finding.SourcePath);
            Assert.Equal("src/Example.csproj", finding.ProjectPath);
            Assert.Equal(8, finding.Metrics["decisionCount"]);
            Assert.Equal(8, finding.Metrics["decisionConstructCount"]);
            Assert.Equal(1, finding.Metrics["maxDecisionNesting"]);
            Assert.Equal(10, finding.Metrics["groupMethodCount"]);
            Assert.Equal(90, finding.Metrics["percentile"]);
            Assert.Equal(8, finding.Metrics["decisionPercentileValue"]);
            Assert.Equal(8, finding.Metrics["decisionCutoff"]);
            Assert.Equal(1, finding.Metrics["decisionRank"]);
            Assert.Contains("ties are included", finding.Rationale, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, finding.Evidence.Count);
            Assert.True(finding.Evidence[0].Line > 0);
            Assert.Contains("public void", finding.Evidence[0].Snippet, StringComparison.Ordinal);
            Assert.Equal("Deepest counted decision", finding.Evidence[1].Label);
            Assert.Contains("if", finding.Evidence[1].Snippet, StringComparison.Ordinal);
        });
        Assert.Contains(result.Findings, finding => finding.SubjectId.Contains("First", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.SubjectId.Contains("Second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PreservesMediumSignatureAndBoundsLongDecisionEvidence()
    {
        var template = """
            namespace Sample;
            public class Example
            {
                public void ValidateDocument(string configurationJson, string resolvedProjectFilePath, int requestVersion, string cancellationTokenText)
                {
                    if (requestVersion == 0) { } // __LONG_COMMENT__
                    if (requestVersion == 1) { }
                    if (requestVersion == 2) { }
                    if (requestVersion == 3) { }
                    if (requestVersion == 4) { }
                    if (requestVersion == 5) { }
                    if (requestVersion == 6) { }
                    if (requestVersion == 7) { }
                }
            }
            """;
        var source = template.Replace("__LONG_COMMENT__", new string('x', 220), StringComparison.Ordinal);
        using var fixture = CreateContext(source);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        var expectedDeclaration = "public void ValidateDocument(string configurationJson, string resolvedProjectFilePath, int requestVersion, string cancellationTokenText)";
        Assert.Equal(expectedDeclaration, finding.Evidence[0].Snippet);
        var decisionEvidence = finding.Evidence[1];
        Assert.Equal("Deepest counted decision", decisionEvidence.Label);
        Assert.True(decisionEvidence.Snippet.Length <= 180);
        Assert.Contains("if (requestVersion == 0)", decisionEvidence.Snippet, StringComparison.Ordinal);
        var sourceLine = source.Split('\n')[decisionEvidence.Line - 1].TrimEnd('\r');
        Assert.True(sourceLine.Length > 180);
        Assert.Contains(decisionEvidence.Snippet, sourceLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CountsSupportedDecisionsAndExcludesNestedFunctions()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public int Measure(bool condition, int value)
                {
                    if (condition) { }
                    switch (value)
                    {
                        case 0: break;
                        case 1: break;
                        default: break;
                    }
                    var mapped = value switch { 0 => 0, 1 => 1, _ => 2 };
                    var result = condition ? 1 : 0;
                    for (var index = 0; index < 1; index++) { }
                    foreach (var item in new[] { 1 }) { }
                    while (false) { }
                    do { } while (false);
                    try { } catch (Exception) { }
                    void Local() { if (condition) { if (condition) { } } }
                    Func<int> lambda = () => condition ? 1 : 0;
                    return mapped + result + lambda();
                }
            }
            """;
        using var fixture = CreateContext(source);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(13, finding.Metrics["decisionCount"]);
        Assert.Equal(9, finding.Metrics["decisionConstructCount"]);
        Assert.Equal(1, finding.Metrics["maxDecisionNesting"]);
        Assert.Equal(1, finding.Metrics["groupMethodCount"]);
        Assert.Equal(13, finding.Metrics["decisionCutoff"]);
        Assert.Equal(4, finding.Metrics["nestingCutoff"]);
    }

    [Fact]
    public async Task ExecuteAsync_TracksNestingButKeepsElseIfOnSameLevel()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public void Chain(int value)
                {
                    if (value == 0) { }
                    else if (value == 1) { }
                    else if (value == 2) { }
                    else if (value == 3) { }
                    else if (value == 4) { }
                    else { }
                    if (value == 5) { if (value == 6) { if (value == 7) { if (value == 8) { } } } }
                }
            }
            """;
        using var fixture = CreateContext(source);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(9, finding.Metrics["decisionCount"]);
        Assert.Equal(9, finding.Metrics["decisionConstructCount"]);
        Assert.Equal(4, finding.Metrics["maxDecisionNesting"]);
        Assert.Equal(4, finding.Metrics["nestingCutoff"]);
    }

    [Fact]
    public async Task ExecuteAsync_UsesExpressionBodiedMethodsAndSkipsGeneratedDocumentsAndMethodsWithoutBodies()
    {
        const string source = """
            using System.CodeDom.Compiler;
            namespace Sample;
            public class Example
            {
                public abstract class AbstractBase { public abstract void NoBody(); }
                [GeneratedCode("generator", "1")]
                public void Generated() { if (true) { if (true) { if (true) { if (true) { } } } } }
                public int Expression(bool condition) => condition ? (condition ? (condition ? (condition ? 1 : 2) : 3) : 4) : 5;
            }
            """;
        using var fixture = CreateContext(source, generatedSource: "namespace Sample; public class GeneratedFile { public void Noise() { if(true) { if(true) { if(true) { if(true) { } } } } }");
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Contains("Expression", finding.SubjectId, StringComparison.Ordinal);
        Assert.Equal(4, finding.Metrics["decisionCount"]);
        Assert.Equal(4, finding.Metrics["decisionConstructCount"]);
        Assert.Equal(4, finding.Metrics["maxDecisionNesting"]);
        Assert.Equal(1, finding.Metrics["groupMethodCount"]);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNoFindingsForProjectWithoutExecutableMethods()
    {
        using var fixture = CreateContext("namespace Sample; public interface IExample { void Run(); }");
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotSelectOneFlatSwitchForItsArmsAlone()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public int Convert(int value) => value switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 2,
                    3 => 3,
                    4 => 4,
                    5 => 5,
                    6 => 6,
                    _ => -1,
                };
            }
            """;
        using var fixture = CreateContext(source);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_SelectsDeepNestingEvenWhenDecisionCountIsBelowCutoff()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public void Run(bool value)
                {
                    if (value)
                    {
                        if (value)
                        {
                            if (value)
                            {
                                if (value) { }
                            }
                        }
                    }
                }
            }
            """;
        using var fixture = CreateContext(source);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(4, finding.Metrics["decisionCount"]);
        Assert.Equal(4, finding.Metrics["decisionConstructCount"]);
        Assert.Equal(8, finding.Metrics["decisionCutoff"]);
        Assert.Equal(4, finding.Metrics["maxDecisionNesting"]);
        Assert.Contains("maxDecisionNesting", finding.Rationale, StringComparison.Ordinal);
        Assert.DoesNotContain("does not meet", finding.Rationale, StringComparison.Ordinal);
        Assert.Contains("if", finding.Evidence[1].Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesTestProjectsFromProductionComparisons()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public void Run()
                {
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                    if (true) { }
                }
            }
            """;
        using var fixture = CreateContext(source, projectName: "Example.Tests");
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("Header.cs", "// <auto-generated />\n")]
    [InlineData("View.g.i.cs", "")]
    public async Task ExecuteAsync_ExcludesGeneratedDocuments(string fileName, string header)
    {
        const string generatedMethod = """
            namespace Generated;
            public class GeneratedType
            {
                public void Run()
                {
                    if (true) { if (true) { if (true) { if (true) { } } } }
                }
            }
            """;
        using var fixture = CreateContext(
            "namespace Sample; public class Ordinary { public void Run() {} }",
            generatedSource: header + generatedMethod,
            generatedFileName: fileName);
        var analysis = new MethodControlFlowOutliersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Descriptor_RejectsPercentilesOutsideIntegerRange()
    {
        var descriptor = new MethodControlFlowOutliersAnalysis().Descriptor;
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("percentile", System.Text.Json.JsonSerializer.SerializeToElement(49))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("percentile", System.Text.Json.JsonSerializer.SerializeToElement(100))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("percentile", System.Text.Json.JsonSerializer.SerializeToElement(90.5))]));
        Assert.Equal(90, descriptor.ResolveOptions()["percentile"].GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_ObservesCancellation()
    {
        using var fixture = CreateContext("namespace Sample; public class Example { public void Run() { } }");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MethodControlFlowOutliersAnalysis().ExecuteAsync(
            fixture.Context,
            new MethodControlFlowOutliersAnalysis().Descriptor.ResolveOptions(),
            cancellation.Token));
    }

    private static AnalysisFixture CreateContext(
        string source,
        string? generatedSource = null,
        string generatedFileName = "Noise.g.cs",
        string projectName = "Example")
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-MethodControlFlow-" + Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "src");
        var projectFile = Path.Combine(projectDirectory, projectName + ".csproj");
        var projectId = ProjectId.CreateNewId();
        var platformAssemblies = new[]
            {
                typeof(object).Assembly,
                typeof(System.CodeDom.Compiler.GeneratedCodeAttribute).Assembly,
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).Assembly,
            }
            .Distinct()
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location));
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp,
            filePath: projectFile,
            compilationOptions: new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: platformAssemblies);
        workspace.AddProject(projectInfo);
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            "Example.cs",
            filePath: Path.Combine(projectDirectory, "Example.cs"),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));
        if (generatedSource is not null)
        {
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                generatedFileName,
                filePath: Path.Combine(projectDirectory, generatedFileName),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(generatedSource), VersionStamp.Create()))));
        }

        return new AnalysisFixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private sealed class AnalysisFixture(AdhocWorkspace workspace, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose() => workspace.Dispose();
    }
}
