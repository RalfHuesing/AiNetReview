namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class NonAsciiIdentifiersAnalysisTests
{
    [Fact]
    public void Descriptor_HasExpectedMetadata()
    {
        var analysis = new NonAsciiIdentifiersAnalysis();
        var descriptor = analysis.Descriptor;

        Assert.Equal("non-ascii-identifiers", descriptor.AnalysisId);
        Assert.Equal("Non-ASCII Identifiers", descriptor.Title);
        Assert.Equal(2, descriptor.BehaviorVersion);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Empty(descriptor.Options);
        Assert.NotEmpty(descriptor.Purpose);
        Assert.NotEmpty(descriptor.Measurement);
        Assert.NotEmpty(descriptor.ReviewQuestions);
    }

    [Fact]
    public async Task ExecuteAsync_Reports_AllNonAsciiIdentifierKinds()
    {
        const string source = """
            namespace MyCompany.Prüfung;

            public class BestätigungsService
            {
                private int zählerStand = 0;

                public string Straße { get; set; } = "";

                public void PrüfeDaten(int größe)
                {
                    int zähler = 0;
                    void BerechneHöhe() { }
                    BerechneHöhe();
                }
            }

            public record Dateneinträge(int Wert);

            public struct PunktKoordinate_Höhe { }

            public interface IPrüfbar { }

            public enum BestätigungsStatus
            {
                Ausgeführt,
                Pending
            }
            """;

        using var fixture = CreateFixture(("Sample", "src/Service.cs", source));
        var analysis = new NonAsciiIdentifiersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.NotEmpty(result.Findings);

        var discriminators = result.Findings.Select(f => f.Discriminator).ToList();
        Assert.Contains("namespace", discriminators);
        Assert.Contains("type", discriminators);
        Assert.Contains("field", discriminators);
        Assert.Contains("property", discriminators);
        Assert.Contains("method", discriminators);
        Assert.Contains("parameter", discriminators);
        Assert.Contains("variable", discriminators);
        Assert.Contains("local-function", discriminators);
        Assert.Contains("enum-member", discriminators);

        var rationales = result.Findings.Select(f => f.Rationale).ToList();
        Assert.Contains(rationales, r => r.Contains("MyCompany.Prüfung") && r.Contains("ü"));
        Assert.Contains(rationales, r => r.Contains("BestätigungsService") && r.Contains("ä"));
        Assert.Contains(rationales, r => r.Contains("zählerStand") && r.Contains("ä"));
        Assert.Contains(rationales, r => r.Contains("Straße") && r.Contains("ß"));
        Assert.Contains(rationales, r => r.Contains("PrüfeDaten") && r.Contains("ü"));
        Assert.Contains(rationales, r => r.Contains("größe") && r.Contains("ö"));
        Assert.Contains(rationales, r => r.Contains("zähler") && r.Contains("ä"));
        Assert.Contains(rationales, r => r.Contains("BerechneHöhe") && r.Contains("ö"));
        Assert.Contains(rationales, r => r.Contains("Dateneinträge") && r.Contains("ä"));
        Assert.Contains(rationales, r => r.Contains("PunktKoordinate_Höhe") && r.Contains("ö"));
        Assert.Contains(rationales, r => r.Contains("IPrüfbar") && r.Contains("ü"));
        Assert.Contains(rationales, r => r.Contains("BestätigungsStatus") && r.Contains("ä"));
        Assert.Contains(rationales, r => r.Contains("Ausgeführt") && r.Contains("ü"));

        // Validate all findings pass CurrentFindingValidator
        var validator = new CurrentFindingValidator();
        var validated = await validator.ValidateAndSortAsync(analysis.Descriptor.AnalysisId, fixture.Context, result.Findings);
        Assert.Equal(result.Findings.Count, validated.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_PureAsciiAndVerbatimKeywords()
    {
        const string source = """
            namespace MyCompany.PureAscii;

            public class ValidAscii_123
            {
                private int @event = 0;

                public string @class { get; set; } = "safe";

                public void ExecuteMethod(int @override, int validParam_1)
                {
                    int @string = 42;
                    int normalVar = 100;
                }
            }
            """;

        using var fixture = CreateFixture(("Sample", "src/ValidAscii.cs", source));
        var analysis = new NonAsciiIdentifiersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesTestProjectsAndHelpers()
    {
        const string source = """
            namespace Xunit
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class FactAttribute : System.Attribute { public string? Skip { get; set; } }
            }
            namespace Sample.Tests
            {
                public class Test_Klasse_Mit_Umläuten
                {
                    [Xunit.Fact(Skip = "deliberately skipped")]
                    public void Test_Größe() { }
                    public void Helper_Mit_Umläuten() { }
                }
            }
            """;

        using var fixture = CreateFixture(("Sample.Tests", "src/UnitTest1.cs", source));
        var analysis = new NonAsciiIdentifiersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Contains(result.Findings, static finding => finding.Rationale.Contains("Test_Größe", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.Rationale.Contains("Helper_Mit_Umläuten", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.Rationale.Contains("Test_Klasse_Mit_Umläuten", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_Skips_GeneratedDocumentsAndSymbols()
    {
        const string generatedDocSource = """
            // <auto-generated>
            // This file was generated by a tool.
            // </auto-generated>
            namespace Sample;

            public class Generierte_Klasse { }
            """;

        const string generatedSymbolSource = """
            namespace Sample;

            [System.CodeDom.Compiler.GeneratedCodeAttribute("Tool", "1.0")]
            public class Klasse_Mit_Attribut
            {
                public int Größe { get; set; }
            }
            """;

        using var fixture = CreateFixture(
            ("Sample", "src/Generated.g.cs", generatedDocSource),
            ("Sample", "src/Annotated.cs", generatedSymbolSource));

        var analysis = new NonAsciiIdentifiersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_Handles_MultipleVariablesOnSameLine()
    {
        const string source = """
            namespace Sample;

            public class MultiVarClass
            {
                public void Method()
                {
                    int zähler = 1, höhe = 2;
                }
            }
            """;

        using var fixture = CreateFixture(("Sample", "src/MultiVar.cs", source));
        var analysis = new NonAsciiIdentifiersAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Equal(2, result.Findings.Count);

        var validator = new CurrentFindingValidator();
        var validated = await validator.ValidateAndSortAsync(analysis.Descriptor.AnalysisId, fixture.Context, result.Findings);
        Assert.Equal(2, validated.Count);
    }

    private static AnalysisFixture CreateFixture(params (string Project, string File, string Source)[] documents)
    {
        var root = TestTempDirectory.Create();
        var workspace = new AdhocWorkspace();
        var projectGroups = documents.GroupBy(static doc => doc.Project, StringComparer.Ordinal);
        var projectIds = projectGroups.ToDictionary(static group => group.Key, static _ => ProjectId.CreateNewId(), StringComparer.Ordinal);

        foreach (var group in projectGroups)
        {
            var projectDirectory = Path.Combine(root.DirectoryPath, group.Key);
            Directory.CreateDirectory(projectDirectory);
            workspace.AddProject(ProjectInfo.Create(
                projectIds[group.Key],
                VersionStamp.Create(),
                group.Key,
                group.Key,
                LanguageNames.CSharp,
                filePath: Path.Combine(projectDirectory, group.Key + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: PlatformReferences()));
        }

        foreach (var document in documents)
        {
            var fullPath = Path.Combine(root.DirectoryPath, document.Project, document.File);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectIds[document.Project]),
                document.File,
                filePath: fullPath,
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
