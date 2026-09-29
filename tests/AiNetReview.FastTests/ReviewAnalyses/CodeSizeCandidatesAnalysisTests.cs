namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class CodeSizeCandidatesAnalysisTests
{
    [Fact]
    public void Descriptor_DeclaresAllOptionsWithContractDefaultsAndIntegerRanges()
    {
        var descriptor = new CodeSizeCandidatesAnalysis().Descriptor;

        Assert.Equal("code-size-candidates", descriptor.AnalysisId);
        Assert.Equal("Code Size Candidates", descriptor.Title);
        Assert.Equal(1, descriptor.BehaviorVersion);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Equal(
            new[] { "extremeFileLines", "extremeFileUtf8Bytes", "extremeMemberCodeLines", "extremeTypeCodeLines", "minMemberCodeLines", "minTypeCodeLines", "percentile" },
            descriptor.Options.Select(static option => option.Name));

        var defaults = descriptor.ResolveOptions();
        Assert.Equal(90, defaults["percentile"].GetInt32());
        Assert.Equal(80, defaults["minMemberCodeLines"].GetInt32());
        Assert.Equal(300, defaults["extremeMemberCodeLines"].GetInt32());
        Assert.Equal(300, defaults["minTypeCodeLines"].GetInt32());
        Assert.Equal(800, defaults["extremeTypeCodeLines"].GetInt32());
        Assert.Equal(1000, defaults["extremeFileLines"].GetInt32());
        Assert.Equal(131072, defaults["extremeFileUtf8Bytes"].GetInt32());

        foreach (var option in descriptor.Options)
        {
            Assert.Equal(JsonValueKind.Number, option.DefaultValue.ValueKind);
            var minimum = option.Name == "percentile" ? 50 : 1;
            var maximum = option.Name == "percentile" ? 99 : int.MaxValue;
            Assert.True(option.IsValidValue(JsonSerializer.SerializeToElement(minimum)), option.Name);
            Assert.True(option.IsValidValue(JsonSerializer.SerializeToElement(maximum)), option.Name);
            Assert.False(option.IsValidValue(JsonSerializer.SerializeToElement((long)maximum + 1)), option.Name);
        }

        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("percentile", JsonSerializer.SerializeToElement(49))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("percentile", JsonSerializer.SerializeToElement(100))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("percentile", JsonSerializer.SerializeToElement(90.5))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("minMemberCodeLines", JsonSerializer.SerializeToElement(0))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("minMemberCodeLines", JsonSerializer.SerializeToElement("80"))]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([new("extremeFileLines", JsonSerializer.SerializeToElement(2147483648L))]));
    }

    [Fact]
    public async Task ExecuteAsync_CollectsEverySupportedExecutableDeclarationKind()
    {
        const string source = """
            namespace Sample;
            public sealed class Example
            {
                public Example() { }
                static Example() { }
                public void Method() { }
                public int ExpressionProperty => 1;
                public int this[int index] => index;
                public int Value { get { return 1; } set { } }
                public static Example operator +(Example left, Example right) => left;
                public static implicit operator int(Example value) => 1;
            }
            """;
        using var fixture = CreateContext(new ProjectSpec("Example", source));

        var findings = await Analyze(fixture.Context, Options(extremeMemberCodeLines: 1));

        Assert.Equal(9, findings.Count);
        Assert.Contains(findings, finding => finding.SubjectId.Contains("#ctor", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("Method", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("ExpressionProperty", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("Item", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("get_Value", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("set_Value", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("op_Addition", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("op_Implicit", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.SubjectId.Contains("Value`", StringComparison.Ordinal));
        Assert.All(findings, finding => Assert.Equal("member-size", finding.Discriminator));
        Assert.Contains("get", findings.Single(finding => finding.SubjectId.Contains("get_Value", StringComparison.Ordinal)).Evidence[0].Snippet, StringComparison.Ordinal);
        Assert.Contains("+", findings.Single(finding => finding.SubjectId.Contains("op_Addition", StringComparison.Ordinal)).Evidence[0].Snippet, StringComparison.Ordinal);
        Assert.Contains("operator", findings.Single(finding => finding.SubjectId.Contains("op_Implicit", StringComparison.Ordinal)).Evidence[0].Snippet, StringComparison.Ordinal);
        Assert.All(findings, finding =>
        {
            Assert.Single(finding.RelatedSymbols);
            Assert.Equal(1, finding.Evidence.Count);
            Assert.Equal(finding.SourcePath, finding.Evidence[0].SourcePath);
            Assert.True(finding.Evidence[0].Line > 0);
            Assert.NotEmpty(finding.Evidence[0].Snippet);
        });
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesBodylessAndNonMemberDeclarationsButCountsLocalBodiesWithinOwner()
    {
        const string source = """
            namespace Sample;
            public partial class Example
            {
                public abstract class AbstractType { public abstract void NoBody(); }
                public partial void Hook();
                public partial void Hook() { }
                public void Owner()
                {
                    void Local() { if (true) { if (true) { if (true) { if (true) { } } } } }
                    System.Action lambda = () => { if (true) { if (true) { if (true) { if (true) { } } } } };
                }
                ~Example() { }
            }
            """;
        using var fixture = CreateContext(new ProjectSpec("Example", source));

        var findings = await Analyze(fixture.Context, Options(extremeMemberCodeLines: 1));

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.SubjectId.Contains("Hook", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.SubjectId.Contains("Owner", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.SubjectId.Contains("Local", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.SubjectId.Contains("lambda", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.SubjectId.Contains("Finalize", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.SubjectId.Contains("NoBody", StringComparison.Ordinal));
        Assert.Equal(0, Assert.Single(findings.Where(finding => finding.SubjectId.Contains("Owner", StringComparison.Ordinal))).Metrics["decisionCount"]);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresBranchingForRelativePathAndExplainsDecisionMetrics()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public int FlatMapper(int value)
                {
                    var result = value;
                    result += 1;
                    result += 2;
                    result += 3;
                    result += 4;
                    result += 5;
                    result += 6;
                    result += 7;
                    return result;
                }
                public int Branchy(int value)
                {
                    if (value == 0) { }
                    if (value == 1) { }
                    if (value == 2) { }
                    if (value == 3) { }
                    if (value == 4) { }
                    if (value == 5) { }
                    if (value == 6) { }
                    if (value == 7) { }
                    return value;
                }
            }
            """;
        using var fixture = CreateContext(new ProjectSpec("Example", source));

        var findings = await Analyze(fixture.Context, Options(percentile: 50, minMemberCodeLines: 1, extremeMemberCodeLines: 100));

        var finding = Assert.Single(findings);
        Assert.Contains("Branchy", finding.SubjectId, StringComparison.Ordinal);
        Assert.Equal(8, finding.Metrics["decisionCount"]);
        Assert.Equal(8, finding.Metrics["decisionConstructCount"]);
        Assert.Equal(1, finding.Metrics["maxDecisionNesting"]);
        Assert.Equal(2, finding.Metrics["groupMemberCount"]);
        Assert.Equal(12, finding.Metrics["memberPercentileValue"]);
        Assert.Equal(1, finding.Metrics["relativePathSelected"]);
        Assert.Equal(0, finding.Metrics["extremePathSelected"]);
        Assert.Contains("relative length-and-control-flow path", finding.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotTreatOneFlatSwitchAsBranchy()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public int Map(int value) => value switch
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
        using var fixture = CreateContext(new ProjectSpec("Example", source));

        var findings = await Analyze(fixture.Context, Options(percentile: 50, minMemberCodeLines: 1, extremeMemberCodeLines: 100));

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ExecuteAsync_UsesNestingAlternativeAndAppliesMemberLengthBoundaryInclusively()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public void Nested(bool value)
                {
                    if (value)
                    {
                        if (value)
                        {
                            if (value)
                            {
                                if (value)
                                {
                                }
                            }
                        }
                    }
                }
            }
            """;
        using var fixture = CreateContext(new ProjectSpec("Example", source));

        var exactMinimum = await Analyze(fixture.Context, Options(percentile: 50, minMemberCodeLines: 15, extremeMemberCodeLines: 100));
        var aboveMinimum = await Analyze(fixture.Context, Options(percentile: 50, minMemberCodeLines: 16, extremeMemberCodeLines: 100));

        var finding = Assert.Single(exactMinimum);
        Assert.Equal(4, finding.Metrics["decisionCount"]);
        Assert.Equal(4, finding.Metrics["decisionConstructCount"]);
        Assert.Equal(4, finding.Metrics["maxDecisionNesting"]);
        Assert.Equal(15, finding.Metrics["memberCodeLines"]);
        Assert.Empty(aboveMinimum);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesExtremeLengthBoundaryIndependently()
    {
        using var fixture = CreateContext(new ProjectSpec("Example", "namespace Sample; public class Example { public void Flat() { } }"));

        var exactExtreme = await Analyze(fixture.Context, Options(minMemberCodeLines: 100, extremeMemberCodeLines: 1));
        var aboveExtreme = await Analyze(fixture.Context, Options(minMemberCodeLines: 100, extremeMemberCodeLines: 2));

        Assert.Single(exactExtreme);
        Assert.Empty(aboveExtreme);
    }

    [Fact]
    public async Task ExecuteAsync_UsesIndependentExtremePathAndReportsBothReasonsOnce()
    {
        const string source = """
            namespace Sample;
            public class Example
            {
                public void Large()
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
        using var fixture = CreateContext(new ProjectSpec("Example", source));
        var analysis = new CodeSizeCandidatesAnalysis();

        var bothPaths = await analysis.ExecuteAsync(fixture.Context, Options(minMemberCodeLines: 1, extremeMemberCodeLines: 1), CancellationToken.None);
        var extremeOnly = await analysis.ExecuteAsync(fixture.Context, Options(minMemberCodeLines: 1000, extremeMemberCodeLines: 1), CancellationToken.None);

        var combined = Assert.Single(bothPaths.Findings);
        Assert.Equal(1, combined.Metrics["relativePathSelected"]);
        Assert.Equal(1, combined.Metrics["extremePathSelected"]);
        Assert.Contains("relative length-and-control-flow path", combined.Rationale, StringComparison.Ordinal);
        Assert.Contains("independent extreme-length path", combined.Rationale, StringComparison.Ordinal);
        var extreme = Assert.Single(extremeOnly.Findings);
        Assert.Equal(0, extreme.Metrics["relativePathSelected"]);
        Assert.Equal(1, extreme.Metrics["extremePathSelected"]);
        Assert.Equal(8, extreme.Metrics["decisionCount"]);
        Assert.Contains("regardless of control flow", extreme.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_UsesNearestRankPerProjectIncludesTiesAndSupportsSingleMemberGroups()
    {
        const string tiedSource = """
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
                public void Third()
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
                public void Small()
                {
                }
                public void AlsoSmall()
                {
                }
            }
            """;
        const string singleSource = "namespace Other; public class Example { public void Only() { if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } } }";
        using var fixture = CreateContext(
            new ProjectSpec("ProductionOne", tiedSource),
            new ProjectSpec("ProductionTwo", singleSource));

        var findings = await Analyze(fixture.Context, Options(percentile: 50, minMemberCodeLines: 1, extremeMemberCodeLines: 100));

        Assert.Equal(4, findings.Count);
        Assert.Equal(3, findings.Count(finding => finding.ProjectPath.EndsWith("ProductionOne.csproj", StringComparison.Ordinal)));
        Assert.Equal(1, findings.Count(finding => finding.ProjectPath.EndsWith("ProductionTwo.csproj", StringComparison.Ordinal)));
        Assert.All(findings.Where(finding => finding.ProjectPath.EndsWith("ProductionOne.csproj", StringComparison.Ordinal)), finding =>
        {
            Assert.Equal(5, finding.Metrics["groupMemberCount"]);
            Assert.Equal(11, finding.Metrics["memberPercentileValue"]);
        });
        var singleGroup = Assert.Single(findings.Where(finding => finding.ProjectPath.EndsWith("ProductionTwo.csproj", StringComparison.Ordinal)));
        Assert.Equal(1, singleGroup.Metrics["groupMemberCount"]);
        Assert.Equal(singleGroup.Metrics["memberCodeLines"], singleGroup.Metrics["memberPercentileValue"]);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesTestProjectsGeneratedDocumentsAndGeneratedSymbols()
    {
        const string normal = "namespace Sample; public class Example { public void Normal() { if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } } [System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public void Generated() { if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } if (true) { } } }";
        using var fixture = CreateContext(
            new ProjectSpec("Production", normal, [new DocumentSpec("Example.cs", normal), new DocumentSpec("Noise.g.cs", normal)]),
            new ProjectSpec("Example.Tests", normal));

        var findings = await Analyze(fixture.Context, Options(minMemberCodeLines: 1, extremeMemberCodeLines: 1));

        var finding = Assert.Single(findings);
        Assert.Contains("Normal", finding.SubjectId, StringComparison.Ordinal);
        Assert.Equal("Production/Example.cs", finding.SourcePath);
    }

    [Fact]
    public async Task ExecuteAsync_UsesEvidenceFromTheCapturedSolutionSnapshot()
    {
        const string original = "namespace Sample; public class Example { public void Original() { } }";
        using var fixture = CreateContext(new ProjectSpec("Example", original));
        var document = fixture.Workspace.CurrentSolution.Projects.Single().Documents.Single();
        var updated = fixture.Workspace.CurrentSolution.WithDocumentText(document.Id, SourceText.From("namespace Sample; public class Example { public void Changed() { } }"));
        Assert.True(fixture.Workspace.TryApplyChanges(updated));

        var findings = await Analyze(fixture.Context, Options(extremeMemberCodeLines: 1));

        var finding = Assert.Single(findings);
        Assert.Contains("Original", finding.SubjectId, StringComparison.Ordinal);
        Assert.Contains("Original", finding.Evidence[0].Snippet, StringComparison.Ordinal);
        Assert.Equal("member-size", finding.Discriminator);
    }

    [Fact]
    public async Task ExecuteAsync_ObservesCancellation()
    {
        using var fixture = CreateContext(new ProjectSpec("Example", "namespace Sample; public class Example { public void Run() { } }"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CodeSizeCandidatesAnalysis().ExecuteAsync(
            fixture.Context,
            Options(),
            cancellation.Token));
    }

    private static ReviewAnalysisOptions Options(
        int percentile = 90,
        int minMemberCodeLines = 80,
        int extremeMemberCodeLines = 300) => new CodeSizeCandidatesAnalysis().Descriptor.ResolveOptions(
        [
            new("percentile", JsonSerializer.SerializeToElement(percentile)),
            new("minMemberCodeLines", JsonSerializer.SerializeToElement(minMemberCodeLines)),
            new("extremeMemberCodeLines", JsonSerializer.SerializeToElement(extremeMemberCodeLines)),
        ]);

    private static async System.Threading.Tasks.Task<IReadOnlyList<AiNetReview.Core.Findings.FindingDraft>> Analyze(
        ReviewContext context,
        ReviewAnalysisOptions options) =>
        (await new CodeSizeCandidatesAnalysis().ExecuteAsync(context, options, CancellationToken.None)).Findings;

    [SuppressMessage("Reliability", "CA2000", Justification = "The returned AnalysisFixture owns and disposes the workspace.")]
    private static AnalysisFixture CreateContext(params ProjectSpec[] projects)
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-CodeSize-" + Guid.NewGuid().ToString("N"));
        foreach (var spec in projects)
        {
            var projectId = ProjectId.CreateNewId();
            var projectDirectory = Path.Combine(root, spec.Name);
            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                spec.Name,
                spec.Name,
                LanguageNames.CSharp,
                filePath: Path.Combine(projectDirectory, spec.Name + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: PlatformReferences());
            workspace.AddProject(projectInfo);
            var documents = spec.Documents ?? [new DocumentSpec("Example.cs", spec.Source)];
            foreach (var document in documents)
            {
                workspace.AddDocument(DocumentInfo.Create(
                    DocumentId.CreateNewId(projectId),
                    document.Name,
                    filePath: Path.Combine(projectDirectory, document.Name),
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(document.Source), VersionStamp.Create()))));
            }
        }

        return new AnalysisFixture(workspace, new ReviewContext(workspace.CurrentSolution, root));
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        new[]
        {
            typeof(object).Assembly,
            typeof(System.CodeDom.Compiler.GeneratedCodeAttribute).Assembly,
            typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).Assembly,
        }
        .Distinct()
        .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location));

    private sealed record DocumentSpec(string Name, string Source);

    private sealed record ProjectSpec(string Name, string Source, IReadOnlyList<DocumentSpec>? Documents = null);

    private sealed class AnalysisFixture(AdhocWorkspace workspace, ReviewContext context) : IDisposable
    {
        public AdhocWorkspace Workspace { get; } = workspace;

        public ReviewContext Context { get; } = context;

        public void Dispose() => Workspace.Dispose();
    }
}
