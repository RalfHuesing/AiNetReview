namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class DeadCodeCandidatesAnalysisTests
{
    [Fact]
    public void deadcode_test_ReferencesAuditFixtureDeclarations()
    {
        deadcode_test_MethodHost.Touch();
        deadcode_test_UsedOnlyByUnitTest.deadcode_test_ReferencedByTest();
    }

    [Fact]
    public async Task ExecuteAsync_SelfRecursiveOrdinaryMethodWithoutExternalUseIsCandidate()
    {
        using var fixture = CreateFixture(
            ("Product", "Product", """
            namespace Product;
            public sealed class Recursive
            {
                private void deadcode_test_SelfRecursive() { deadcode_test_SelfRecursive(); }
            }
            """, null),
            ("Product.Tests", "Product.Tests", """
            using Product;
            public sealed class UsesType { private Recursive value = new(); }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("deadcode_test_SelfRecursive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_GroupsUnreferencedTypeAndSelectsOnlyUnreferencedOrdinaryMethods()
    {
        using var fixture = CreateFixture(
            ("Product", "Product", """
                namespace Product;
                internal sealed class Orphan
                {
                    public void MemberOne() { }
                    private void MemberTwo() { }
                    private void SelfReference() { _ = typeof(Orphan); }
                }
                public sealed class Used
                {
                    private void UnusedPrivate() { }
                    public void UsedFromTest() { }
                    private void MethodGroupTarget() { }
                    private void Uncertain(string value) { }
                    private void Unrelated() { }
                    public void BindUncertainly() { Uncertain(42); }
                }
                internal static class Extensions
                {
                    public static void ExtensionTarget(this Used value) { }
                    public static void ExtensionCandidate(this Used value) { }
                }
                """, null),
            ("Product.Tests", "Product.Tests", """
                using System;
                using Product;
                public sealed class References
                {
                    private readonly Action callback = Used.MethodGroupTarget;
                    public void Run(Used value) { value.UsedFromTest(); }
                }
                """, null),
            ("GeneratedConsumer", "GeneratedConsumer", """
                using Product;
                public sealed class GeneratedConsumer { public void Run(Used value) { value.ExtensionTarget(); } }
                """, "GeneratedConsumer.g.cs"));
        var analysis = new DeadCodeCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        var orphan = Assert.Single(result.Findings, static finding => finding.SubjectId.Contains("Orphan", StringComparison.Ordinal));
        Assert.Equal("type-candidate", orphan.Discriminator);
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("MemberOne", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("MemberTwo", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("SelfReference", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("UsedFromTest", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("MethodGroupTarget", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("ExtensionTarget", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Uncertain", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedPrivate", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Unrelated", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("ExtensionCandidate", StringComparison.Ordinal));
        Assert.Equal(result.Findings.OrderBy(static finding => finding.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.SourcePath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.StartLine)
            .ThenBy(static finding => finding.SubjectId, StringComparer.Ordinal).Select(static finding => finding.SubjectId),
            result.Findings.Select(static finding => finding.SubjectId));
    }

    [Fact]
    public async Task ExecuteAsync_UsesExternalLibraryApiPolicyByDefaultAndClosedSolutionIncludesPublicSurface()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            public sealed class PublicApi
            {
                public void PublicMethod() { }
                protected void ProtectedMethod() { }
                protected internal void ProtectedInternalMethod() { }
                internal void InternalMethod() { }
                private protected void PrivateProtectedMethod() { }
            }
            public sealed class UnusedPublicType { }
            public sealed class ApiConsumer { public void Run(PublicApi value) { } }
            internal sealed class InternalContainer { public void PublicMember() { } }
            public sealed class Consumer { public void Run() { _ = new InternalContainer(); } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();

        var defaultResult = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        var closedResult = await analysis.ExecuteAsync(
            fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.DoesNotContain(defaultResult.Findings, static finding => finding.SubjectId == "T:UnusedPublicType");
        Assert.DoesNotContain(defaultResult.Findings, static finding => finding.SubjectId.Contains("PublicMethod", StringComparison.Ordinal));
        Assert.Contains(defaultResult.Findings, static finding => finding.SubjectId.Contains("InternalMethod", StringComparison.Ordinal));
        Assert.Contains(defaultResult.Findings, static finding => finding.SubjectId.Contains("PrivateProtectedMethod", StringComparison.Ordinal));
        Assert.Contains(closedResult.Findings, static finding => finding.SubjectId == "T:UnusedPublicType");
        Assert.Contains(closedResult.Findings, static finding => finding.SubjectId.Contains("PublicMethod", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesInterfaceContractsImplementationsOverridesEntryPointsAndGeneratedDeclarations()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            public interface IContract { void ContractMethod(); }
            public class Implementation : IContract
            {
                public void ContractMethod() { }
                public virtual void BaseMethod() { }
            }
            public class Derived : Implementation
            {
                public override void BaseMethod() { }
            }
            public static class Program
            {
                public static void Main() { }
            }
            public class Referenced
            {
                public const int Constant = 1;
                public int Field;
                public int Property { get; set; }
                public event Action? Changed;
                public int this[int index] { get => index; set { } }
                public Referenced() { }
                public static Referenced operator +(Referenced left, Referenced right) => left;
                ~Referenced() { }
                public void Use() { }
            }
            public sealed class Consumer { public void Run(Referenced value) { value.Use(); _ = value.Property; } }
            """, null), ("Product.Generated", "Product.Generated", """
            namespace Generated;
            public class GeneratedType { public void GeneratedMethod() { } }
            """, "GeneratedThing.g.cs"));
        var analysis = new DeadCodeCandidatesAnalysis();

        var result = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("ContractMethod", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("BaseMethod", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Main", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("GeneratedType", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("GeneratedMethod", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Constant", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Field", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Property", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Changed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Item", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains(".ctor", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("op_Addition", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Finalize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_SkipsSymbolWithLocalBindingUncertaintyAndFailsWhenGlobalReferenceCoverageIsUnavailable()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            public class Example
            {
                private void Uncertain(string value) { }
                private void Use() { Uncertain(42); }
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();

        var localResult = await analysis.ExecuteAsync(fixture.Context, analysis.Descriptor.ResolveOptions(), CancellationToken.None);
        using var emptyWorkspace = new AdhocWorkspace();
        using var root = TestTempDirectory.Create();
        var noCoverage = new ReviewContext(emptyWorkspace.CurrentSolution, root.DirectoryPath);

        await Assert.ThrowsAsync<AnalysisFailedException>(() =>
            analysis.ExecuteAsync(noCoverage, analysis.Descriptor.ResolveOptions(), CancellationToken.None));
        Assert.DoesNotContain(localResult.Findings, static finding => finding.SubjectId.Contains("Uncertain", StringComparison.Ordinal));
        Assert.Contains(localResult.Findings, static finding => finding.SubjectId == "M:Example.Use");
    }

    [Fact]
    public void Descriptor_AcceptsOnlySupportedApiSurfaceValues()
    {
        var descriptor = new DeadCodeCandidatesAnalysis().Descriptor;

        Assert.Equal("external_library", descriptor.ResolveOptions()["apiSurface"].GetString());
        Assert.Empty(descriptor.ResolveOptions()["entryPointAttributes"].EnumerateArray());
        Assert.Equal("closed_solution", descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement("closed_solution")),
        ])["apiSurface"].GetString());
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement("Closed_Solution")),
        ]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions([
            new("entryPointAttributes", JsonSerializer.SerializeToElement(new[] { "Unqualified" })),
        ]));
    }

    [Fact]
    public async Task ExecuteAsync_ProtectsFixedAndConfiguredEntryPointAttributesBySemanticIdentity()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            namespace Contracts { public sealed class EntryPointAttribute : System.Attribute { } }
            namespace Fake { public sealed class JSInvokableAttribute : System.Attribute { } }
            internal sealed class Entrypoints
            {
                [Contracts.EntryPoint] private void Configured() { }
                [System.Runtime.CompilerServices.ModuleInitializer] private static void Fixed() { }
                [Microsoft.JSInterop.JSInvokable] public static void JavaScriptEntry() { }
                [Fake.EntryPoint] private void SameSimpleName() { }
                private void Ordinary() { }
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement("closed_solution")),
            new("entryPointAttributes", JsonSerializer.SerializeToElement(new[] { "Contracts.EntryPointAttribute" })),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Configured", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Fixed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("JavaScriptEntry", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("SameSimpleName", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Ordinary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UsesSnapshotRazorBindingsAndFailsOnUnevaluableXaml()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            namespace Ui;
            internal sealed class Widget
            {
                private void HandleClick() { }
                private void Unrelated() { }
            }
            """, null));
        var markupPath = Path.Combine(fixture.Context.ProjectRoot, "Widget.razor");
        await File.WriteAllTextAsync(markupPath, "<Widget @onclick=\"Unrelated\" />");
        var snapshotContext = new ReviewContext(
            fixture.Context.Solution,
            fixture.Context.ProjectRoot,
            [new MarkupDocumentSnapshot(markupPath, "<Widget @onclick=\"HandleClick\" />")]);
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(snapshotContext, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId == "T:Ui.Widget");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("HandleClick", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Unrelated", StringComparison.Ordinal));

        var invalidMarkup = new ReviewContext(
            fixture.Context.Solution,
            fixture.Context.ProjectRoot,
            [new MarkupDocumentSnapshot(Path.ChangeExtension(markupPath, ".xaml"), "<Widget")]);
        await Assert.ThrowsAsync<AnalysisFailedException>(() => analysis.ExecuteAsync(invalidMarkup, options, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_ReflectionProtectsKnownBindingAndLimitsDynamicUncertaintyToTheTargetType()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            using System;
            internal sealed class Reflected
            {
                private void Bound() { }
                private void Dynamic() { }
                private void Other() { }
            }
            internal sealed class Reflection
            {
                private void Run(string name)
                {
                    _ = typeof(Reflected).GetMethod("Bound", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    _ = typeof(Reflected).GetMethod(name);
                }
            }
            internal sealed class Independent { private void Candidate() { } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Reflected", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Bound", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Dynamic", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Other", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Independent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DiRegistrationProtectsRegisteredType()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            using Microsoft.Extensions.DependencyInjection;
            internal sealed class RegisteredService { private void Entry() { } }
            internal sealed class Composition { private IServiceCollection Register(IServiceCollection services) => services.AddSingleton<RegisteredService>(); }
            internal sealed class IndependentService { private void Candidate() { } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId == "T:RegisteredService");
        Assert.Contains(result.Findings, static finding => finding.SubjectId == "T:IndependentService");
    }

    [Fact]
    public async Task ExecuteAsync_RegisteredMiddlewareProtectsFrameworkEntryMethod()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;
            internal sealed class Middleware
            {
                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
                public void Orphan() { }
            }
            internal static class Startup
            {
                private static void Configure(IApplicationBuilder app) => app.UseMiddleware<Middleware>();
            }
            internal sealed class Controller
            {
                [HttpGet] private void RouteEntry() { }
                private void ControllerOrphan() { }
            }
            internal sealed class Independent { private void Candidate() { } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("InvokeAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("RouteEntry", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("ControllerOrphan", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Orphan", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Independent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_XamlCodeBehindAndHandlersUseMarkupSnapshot()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            namespace Ui;
            internal sealed class Page { private void Clicked() { } private void Unrelated() { } }
            internal sealed class Button { public event System.EventHandler? Click; }
            """, null));
        var path = Path.Combine(fixture.Context.ProjectRoot, "Page.xaml");
        var snapshot = """
            <ui:Page xmlns:ui="clr-namespace:Ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Ui.Page">
              <ui:Button Click="Clicked" />
            </ui:Page>
            """;
        var context = new ReviewContext(fixture.Context.Solution, fixture.Context.ProjectRoot, [new MarkupDocumentSnapshot(path, snapshot)]);
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId == "T:Ui.Page");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Clicked", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Unrelated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_JavascriptInvocationWithUnresolvedNameOnlyMakesMatchingMethodsUncertain()
    {
        using var fixture = CreateFixture(("Product", "Product", """
            internal sealed class JsApi { private void Invoke() { } private void Other() { } }
            internal sealed class Independent { private void Candidate() { } }
            """, null));
        var path = Path.Combine(fixture.Context.ProjectRoot, "interop.js");
        var context = new ReviewContext(fixture.Context.Solution, fixture.Context.ProjectRoot,
            [new MarkupDocumentSnapshot(path, "DotNet.invokeMethodAsync('Product', 'Invoke');")]);
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]);

        var result = await analysis.ExecuteAsync(context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Invoke", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Other", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Independent", StringComparison.Ordinal));
    }

    private static AnalysisFixture CreateFixture(params (string Name, string Assembly, string Source, string? FileName)[] projects)
    {
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
        var projectIds = projects.ToDictionary(static project => project.Name, static _ => ProjectId.CreateNewId(), StringComparer.Ordinal);
        foreach (var spec in projects)
        {
            workspace.AddProject(ProjectInfo.Create(
                projectIds[spec.Name],
                VersionStamp.Create(),
                spec.Name,
                spec.Assembly,
                LanguageNames.CSharp,
                filePath: Path.Combine(root.DirectoryPath, spec.Name + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: PlatformReferences().Append(
                    MetadataReference.CreateFromFile(typeof(Microsoft.JSInterop.JSInvokableAttribute).Assembly.Location))));
        }

        foreach (var spec in projects)
        {
            var name = spec.FileName ?? spec.Name + ".cs";
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectIds[spec.Name]),
                name,
                filePath: Path.Combine(root.DirectoryPath, name),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(spec.Source), VersionStamp.Create()))));
        }

        var solution = workspace.CurrentSolution;
        var productionId = projectIds[projects[0].Name];
        foreach (var referencedProject in projects.Skip(1))
        {
            solution = solution.AddProjectReference(projectIds[referencedProject.Name], new ProjectReference(productionId));
        }

        Assert.True(workspace.TryApplyChanges(solution));
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
