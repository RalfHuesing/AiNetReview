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

    [Theory]
    [InlineData("external_library")]
    [InlineData("closed_solution")]
    public async Task ExecuteAsync_ProtectsInternalExecutableEntryPointTypeButStillChecksItsMethods(string apiSurface)
    {
        using var fixture = CreateFixture(OutputKind.ConsoleApplication, ("Product", "Product", """
            internal static class Bootstrap
            {
                public static int Main() => 0;
                private static void UnusedHelper() { }
            }
            internal sealed class UnusedType { }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement(apiSurface)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.Discriminator == "type-candidate"
            && finding.SubjectId.Contains("Bootstrap", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Bootstrap.Main", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedType", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("external_library")]
    [InlineData("closed_solution")]
    public async Task ExecuteAsync_ProtectsOnlyConfiguredStartupTypeAndContainingChain(string apiSurface)
    {
        using var fixture = CreateFixture(OutputKind.ConsoleApplication, "Outer.SelectedStartup", ("Product", "Product", """
            using System.Threading.Tasks;
            internal partial class Outer
            {
                internal static class SelectedStartup
                {
                    public static async Task<int> Main()
                    {
                        await Task.Yield();
                        return 0;
                    }
                    private static void UnusedHelper() { }
                }

                internal static class OtherStartup
                {
                    public static int Main() => 1;
                }
            }
            internal static class Consumer
            {
                private static System.Type KeepOtherTypeReferenced() => typeof(Outer.OtherStartup);
            }
            internal partial class Outer { private void UnusedOuterHelper() { } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement(apiSurface)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.Discriminator == "type-candidate"
            && finding.SubjectId == "T:Outer");
        Assert.DoesNotContain(result.Findings, static finding => finding.Discriminator == "type-candidate"
            && finding.SubjectId == "T:Outer.SelectedStartup");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("SelectedStartup.Main", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("OtherStartup.Main", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedOuterHelper", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("external_library")]
    [InlineData("closed_solution")]
    public async Task ExecuteAsync_ProtectsExplicitPartialProgramForTopLevelStatements(string apiSurface)
    {
        using var fixture = CreateFixture(OutputKind.ConsoleApplication, ("Product", "Product", """
            using System;
            Console.WriteLine("run");
            partial class Program
            {
                private static void UnusedHelper() { }
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement(apiSurface)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.Discriminator == "type-candidate"
            && finding.SubjectId.Contains("Program", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedHelper", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("external_library")]
    [InlineData("closed_solution")]
    public async Task ExecuteAsync_DoesNotProtectLibraryMainByName(string apiSurface)
    {
        using var fixture = CreateFixture(("Product", "Product", """
            internal static class Bootstrap
            {
                public static int Main() => 0;
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var options = analysis.Descriptor.ResolveOptions([
            new("apiSurface", JsonSerializer.SerializeToElement(apiSurface)),
        ]);

        var result = await analysis.ExecuteAsync(fixture.Context, options, CancellationToken.None);

        Assert.Contains(result.Findings, static finding => finding.Discriminator == "type-candidate"
            && finding.SubjectId == "T:Bootstrap");
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

        Assert.Equal(3, descriptor.BehaviorVersion);
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

    [Fact]
    public async Task ExecuteAsync_ProtectsXunitV2V3DerivedTestsFixturesLifecycleAndDataBindingsButNotHelpers()
    {
        var framework = CreateMetadataReference("xunit.v3.core", XunitMetadata);
        using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Xunit;
            [assembly: Xunit.v3.AssemblyFixture(typeof(SharedFixture))]
            public sealed class CustomFactAttribute : FactAttribute { }
            public sealed class CustomV3FactAttribute : Attribute, Xunit.v3.IFactAttribute { }
            public sealed class Tests : ProviderBase, IClassFixture<SharedFixture>
            {
                [CustomFact(Skip = "disabled")]
                public void Skipped() { }
                [CustomV3Fact] public void V3InterfaceTest() { }
                [MemberData("Rows")] public void Theory(int value) { }
                [MemberData("ExternalRows", MemberType = typeof(DataProvider))] public void ExternalTheory(int value) { }
                [AsyncMemberData] public void AsyncTheory(int value) { }
                [ClassData(typeof(DataProvider))] public void ClassTheory(int value) { }
                public static IEnumerable<object[]> Rows() => Array.Empty<object[]>();
                public static IEnumerable<object[]> Rows(int count) => Array.Empty<object[]>();
                private void UnusedHelper() { }
            }
            public class ProviderBase
            {
                public static object[][] Alternative() => [];
                public static System.Threading.Tasks.Task<IEnumerable<object[]>> AsyncRows() => System.Threading.Tasks.Task.FromResult<IEnumerable<object[]>>([]);
                public static System.Threading.Tasks.Task<IEnumerable<object[]>> AsyncRows(int count) => System.Threading.Tasks.Task.FromResult<IEnumerable<object[]>>([]);
                public static System.Threading.Tasks.Task<IEnumerable<object[]>> AsyncAlternative() => System.Threading.Tasks.Task.FromResult<IEnumerable<object[]>>([]);
            }
            public sealed class AsyncMemberDataAttribute : MemberDataAttribute("AsyncRows") { }
            public class BaseTests
            {
                [Theory, MemberData("BaseRows")] public void InheritedTheory(int value) { }
                public static IEnumerable<object[]> BaseRows() => Array.Empty<object[]>();
                private void InheritedHelper() { }
            }
            public sealed class DerivedTests : BaseTests { }
            [CollectionDefinition("shared")]
            public sealed class SharedCollection : ICollectionFixture<SharedFixture> { }
            [Collection("shared")]
            public sealed class CollectionTests
            {
                [Fact] public void CollectionEntry() { }
                private void CollectionHelper() { }
            }
            public sealed class SharedFixture : IDisposable, IAsyncDisposable, IAsyncLifetime
            {
                public void Dispose() { }
                public Task InitializeAsync() => Task.CompletedTask;
                public Task DisposeAsync() => Task.CompletedTask;
                ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
                public void OrdinaryCandidate() { }
            }
            public sealed class DataProvider : IEnumerable<object[]>
            {
                public static IEnumerable<object[]> ExternalRows() => Array.Empty<object[]>();
                public IEnumerator<object[]> GetEnumerator() => Array.Empty<object[]>().AsEnumerable().GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                public void HelperCandidate() { }
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Tests", StringComparison.Ordinal)
            && finding.Discriminator == "type-candidate");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Skipped", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("V3InterfaceTest", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("CollectionEntry", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId == "T:CollectionTests");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Rows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Alternative", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("AsyncRows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("AsyncAlternative", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("ExternalRows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("InheritedTheory", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("SharedFixture", StringComparison.Ordinal)
            && finding.Discriminator == "type-candidate");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Dispose", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("InitializeAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("GetEnumerator", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("UnusedHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("InheritedHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("CollectionHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("OrdinaryCandidate", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("HelperCandidate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ProtectsNunitTestsHooksFixtureSourcesAndNamedDataProviders()
    {
        var framework = CreateMetadataReference("nunit.framework", NUnitMetadata);
        using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
            using System;
            using System.Collections.Generic;
            using NUnit.Framework;
            [TestFixture, TestFixtureSource(typeof(FixtureData), "FixtureRows")]
            public sealed class Tests : HookBase
            {
                [SetUp] private void BeforeEach() { }
                [TearDown] private void AfterEach() { }
                [Explicit, CustomCase, TestCaseSource(typeof(Cases), "Rows")] public void Case(int value) { }
                [TestCaseSource(typeof(TypeOnlyData))] public void TypeOnlyCase(int value) { }
                [TestCaseSource((string)null)] public void UnknownRowsCase(int value) { }
                [TestCaseSource("MissingRows")] public void MissingRowsCase(int value) { }
                [Ignore("disabled"), Test] public void IgnoredTest() { }
                [Theory] public void DataPoint([ValueSource(typeof(Cases), "Values")] int value) { }
                private static IEnumerable<object[]> PrivateRows() => Array.Empty<object[]>();
                private static void PrivateHelper() { }
                public void OrdinaryHelper() { }
            }
            [SetUpFixture] public sealed class GlobalFixture { public void Helper() { } }
            public abstract class HookBase
            {
                [OneTimeSetUp] public void BeforeAll() { }
                [OneTimeTearDown] public void AfterAll() { }
                [TestFixtureSetUp] public void LegacyBeforeAll() { }
                [TestFixtureTearDown] public void LegacyAfterAll() { }
            }
            public sealed class CustomCaseAttribute : TestAttribute { }
            public sealed class Cases
            {
                public static object[] Rows(int value) => [value];
                public static int[] Values => [1];
                [Datapoint] public static int Point = 1;
                [DatapointSource] public static int[] Points => [2];
                public static void Unrelated() { }
            }
            public sealed class FixtureData { public static object[] FixtureRows => [new object()]; }
            public sealed class TypeOnlyData { public static object[] Cases => [new object()]; private static void PrivateHelper() { } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Tests", StringComparison.Ordinal)
            && finding.Discriminator == "type-candidate");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("BeforeEach", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("AfterEach", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("BeforeAll", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("AfterAll", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("LegacyBeforeAll", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("LegacyAfterAll", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Rows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Values", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Point", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("FixtureRows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("TypeOnlyCase", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("PrivateRows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("IgnoredTest", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("GlobalFixture", StringComparison.Ordinal)
            && finding.Discriminator == "type-candidate");
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("OrdinaryHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("PrivateHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Unrelated", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("PrivateHelper", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ProtectsMstestMethodsLifecycleDataAndFixtureProviderBindings()
    {
        var framework = CreateMetadataReference("Microsoft.VisualStudio.TestPlatform.TestFramework", MsTestMetadata);
        using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
            using System;
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            [assembly: AssemblyFixtureProvider(typeof(FixtureProvider))]
            [TestClass]
            public sealed class Tests
            {
                [TestMethod] public void Basic() { }
                [DataTestMethod, DynamicData("Rows", typeof(Data), DynamicDataSourceType.AutoDetect, DynamicDataDisplayName = nameof(Data.DisplayName), DynamicDataDisplayNameDeclaringType = typeof(Data))]
                public void Data(int value) { }
                [TestInitialize] public void Before() { }
                [TestCleanup] public void After() { }
                public void DynamicName(object value) { }
                public void Helper() { }
            }
            public sealed class Data
            {
                public static object[] Rows => [1];
                public static string DisplayName(object value) => "case";
            }
            public static class FixtureProvider
            {
                [AssemblyInitialize] public static void Setup() { }
                [AssemblyCleanup] public static void Cleanup() { }
                public static object[] Fixtures => [];
                public static void Helper() { }
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Tests", StringComparison.Ordinal)
            && finding.Discriminator == "type-candidate");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Basic", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Before", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("After", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Rows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("DisplayName", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("FixtureProvider", StringComparison.Ordinal)
            && finding.Discriminator == "type-candidate");
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Helper", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_RecognizesBothMSTestFrameworkAssemblyIdentities()
    {
        foreach (var assemblyName in new[] { "Microsoft.VisualStudio.TestPlatform.TestFramework", "MSTest.TestFramework" })
        {
            var framework = CreateMetadataReference(assemblyName, MsTestMetadata);
            using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
                using Microsoft.VisualStudio.TestTools.UnitTesting;
                [TestClass]
                public sealed class ValidTests
                {
                    [TestMethod] public void ValidCase() { }
                    [TestInitialize] public void Setup() { }
                    private void OrdinaryHelper() { }
                }
                """, null));
            var analysis = new DeadCodeCandidatesAnalysis();
            var result = await analysis.ExecuteAsync(fixture.Context,
                analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
                CancellationToken.None);

            Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId == "T:ValidTests");
            Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("ValidCase", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("Setup", StringComparison.Ordinal));
            Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("OrdinaryHelper", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ExecuteAsync_LocalLookalikeFrameworkAttributeDoesNotProtectTheMethodOrNeighboringHelper()
    {
        var framework = CreateMetadataReference("xunit.v3.core", XunitMetadata);
        using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            public sealed class Lookalike
            {
                [Xunit.Fact] private void Imitation() { }
                private void NeighboringHelper() { }
            }
            public sealed class Consumer { public System.Type UsedType => typeof(Lookalike); }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("Imitation", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("NeighboringHelper", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UncertainProviderNameStaysLocalToPlausibleSourceType()
    {
        var framework = CreateMetadataReference("xunit.v3.core", XunitMetadata);
        using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
            using Xunit;
            public sealed class Tests
            {
                [Theory, MemberData("Rows")] public void Case(int value) { }
                public static System.Collections.Generic.IEnumerable<object[]> Rows() => [];
                public static System.Collections.Generic.IEnumerable<object[]> Rows(int count) => [];
                private void TestHelper() { }
            }
            public sealed class Independent { private void Candidate() { } }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("TestHelper", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId == "T:Independent");
        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId == "T:Tests");
    }

    [Theory]
    [InlineData("external_library", false)]
    [InlineData("closed_solution", true)]
    public async Task ExecuteAsync_ProtectsXunitV2DerivedSkippedTestsAndKeepsApiSurfaceForTestProjects(string apiSurface, bool includesPublicCandidates)
    {
        var framework = CreateMetadataReference("xunit.core", XunitV2Metadata);
        using var fixture = CreateFixtureWithReferences([framework], ("Product.Tests", "Product.Tests", """
            using Xunit;
            public sealed class CustomFactAttribute : FactAttribute { }
            public sealed class Tests
            {
                [CustomFact(Skip = "temporarily disabled")] public void SkippedTest() { }
                private void OrdinaryHelper() { }
            }
            public sealed class PublicUnused
            {
                public void PublicMethod() { }
            }
            """, null));
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(fixture.Context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement(apiSurface))]),
            CancellationToken.None);

        Assert.DoesNotContain(result.Findings, static finding => finding.SubjectId.Contains("SkippedTest", StringComparison.Ordinal));
        Assert.Contains(result.Findings, static finding => finding.SubjectId.Contains("OrdinaryHelper", StringComparison.Ordinal));
        Assert.Equal(includesPublicCandidates,
            result.Findings.Any(static finding => finding.SubjectId == "T:PublicUnused"));
    }

    [Fact]
    public async Task ExecuteAsync_BroadUnknownProviderTypeExcludesOnlyItsReachableProjectAreaAndReportsTheReason()
    {
        var framework = CreateMetadataReference("xunit.v3.core", XunitMetadata);
        using var fixture = CreateFixtureWithReferences([framework],
            ("Product", "Product", "namespace Product; public sealed class ProductClass { private void Candidate() { } }", null),
            ("Product.Tests", "Product.Tests", """
                using Xunit;
                public sealed class Tests
                {
                    [Theory, MemberData("Rows", MemberType = typeof(Missing.Provider))]
                    public void Case(int value) { }
                }
                """, null),
            ("Isolated", "Isolated", "namespace Isolated; public sealed class Independent { private void Candidate() { } }", null));
        var solution = fixture.Context.Solution;
        var isolated = solution.Projects.Single(static project => project.Name == "Isolated");
        var product = solution.Projects.Single(static project => project.Name == "Product");
        Assert.Contains(solution.Projects.Single(static project => project.Name == "Product.Tests").ProjectReferences,
            reference => reference.ProjectId == product.Id);
        var testProject = solution.Projects.Single(static project => project.Name == "Product.Tests");
        Assert.Contains(solution.GetProject(testProject.ProjectReferences.Single().ProjectId)!.Name, new[] { "Product" });
        solution = solution.RemoveProjectReference(isolated.Id, new ProjectReference(product.Id));
        var context = new ReviewContext(solution, fixture.Context.ProjectRoot);
        var analysis = new DeadCodeCandidatesAnalysis();
        var result = await analysis.ExecuteAsync(context,
            analysis.Descriptor.ResolveOptions([new("apiSurface", JsonSerializer.SerializeToElement("closed_solution"))]),
            CancellationToken.None);

        Assert.Empty(result.Findings.Where(static finding => finding.ProjectPath is "Product.csproj" or "Product.Tests.csproj"));
        Assert.Contains(result.Findings, static finding => finding.ProjectPath == "Isolated.csproj");
        Assert.Equal(new[] { "Product.Tests.csproj", "Product.csproj" },
            result.ScopeExclusions.Select(static exclusion => exclusion.ProjectPath).OrderBy(static path => path, StringComparer.Ordinal));
        Assert.All(result.ScopeExclusions, static exclusion => Assert.Contains("statically resolvable", exclusion.Reason, StringComparison.Ordinal));
    }

    private static AnalysisFixture CreateFixture(params (string Name, string Assembly, string Source, string? FileName)[] projects)
        => CreateFixture(OutputKind.DynamicallyLinkedLibrary, mainTypeName: null, additionalReferences: null, projects: projects);

    private static AnalysisFixture CreateFixture(
        OutputKind outputKind,
        params (string Name, string Assembly, string Source, string? FileName)[] projects)
        => CreateFixture(outputKind, mainTypeName: null, additionalReferences: null, projects: projects);

    private static AnalysisFixture CreateFixture(
        OutputKind outputKind,
        string? mainTypeName,
        params (string Name, string Assembly, string Source, string? FileName)[] projects)
        => CreateFixture(outputKind, mainTypeName, additionalReferences: null, projects: projects);

    private static AnalysisFixture CreateFixtureWithReferences(
        IEnumerable<MetadataReference> additionalReferences,
        params (string Name, string Assembly, string Source, string? FileName)[] projects)
        => CreateFixture(OutputKind.DynamicallyLinkedLibrary, mainTypeName: null, additionalReferences: additionalReferences, projects: projects);

    private static AnalysisFixture CreateFixture(
        OutputKind outputKind,
        string? mainTypeName,
        IEnumerable<MetadataReference>? additionalReferences,
        params (string Name, string Assembly, string Source, string? FileName)[] projects)
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
                compilationOptions: new CSharpCompilationOptions(outputKind, mainTypeName: mainTypeName),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: PlatformReferences().Append(
                    MetadataReference.CreateFromFile(typeof(Microsoft.JSInterop.JSInvokableAttribute).Assembly.Location))
                    .Concat(additionalReferences ?? Array.Empty<MetadataReference>())));
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

    private static MetadataReference CreateMetadataReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private const string XunitV2Metadata = """
        namespace Xunit
        {
            [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true)]
            public class FactAttribute : System.Attribute { public string? Skip { get; set; } }
            public class TheoryAttribute : FactAttribute { }
            public class MemberDataAttribute(string memberName) : System.Attribute { public System.Type? MemberType { get; set; } }
            public sealed class ClassDataAttribute(System.Type classType) : System.Attribute { }
            public interface IClassFixture<T> { }
            public interface ICollectionFixture<T> { }
            public interface IAsyncLifetime { System.Threading.Tasks.Task InitializeAsync(); System.Threading.Tasks.Task DisposeAsync(); }
        }
        """;

    private const string XunitMetadata = """
        namespace Xunit
        {
            [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true)]
            public class FactAttribute : System.Attribute { public string? Skip { get; set; } }
            public class TheoryAttribute : FactAttribute { }
            public sealed class MemberDataAttribute(string memberName) : System.Attribute { public System.Type? MemberType { get; set; } }
            public sealed class ClassDataAttribute(System.Type classType) : System.Attribute { }
            public sealed class CollectionAttribute(string name) : System.Attribute { }
            public sealed class CollectionDefinitionAttribute(string name) : System.Attribute { }
            public interface IClassFixture<T> { }
            public interface ICollectionFixture<T> { }
            public interface IAsyncLifetime { System.Threading.Tasks.Task InitializeAsync(); System.Threading.Tasks.Task DisposeAsync(); }
        }
        namespace Xunit.v3
        {
            public interface IFactAttribute { }
            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class AssemblyFixtureAttribute(System.Type fixtureType) : System.Attribute { }
        }
        """;

    private const string NUnitMetadata = """
        namespace NUnit.Framework
        {
            [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true)] public class TestAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class ExplicitAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class IgnoreAttribute(string reason) : System.Attribute { }
            public sealed class TheoryAttribute : TestAttribute { }
            public sealed class TestCaseAttribute(params object[] values) : TestAttribute { }
            public sealed class TestCaseSourceAttribute : System.Attribute
            {
                public TestCaseSourceAttribute(string sourceName) { }
                public TestCaseSourceAttribute(System.Type sourceType, string sourceName) { }
                public TestCaseSourceAttribute(System.Type sourceType) { }
                public System.Type? SourceType { get; }
            }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class TestFixtureAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class TestFixtureSourceAttribute : System.Attribute
            {
                public TestFixtureSourceAttribute(string sourceName) { }
                public TestFixtureSourceAttribute(System.Type sourceType, string sourceName) { }
                public TestFixtureSourceAttribute(System.Type sourceType) { }
                public System.Type? SourceType { get; }
            }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class SetUpFixtureAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class SetUpAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class TearDownAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class OneTimeSetUpAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class OneTimeTearDownAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class TestFixtureSetUpAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class TestFixtureTearDownAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Parameter)] public sealed class ValueSourceAttribute : System.Attribute
            {
                public ValueSourceAttribute(string sourceName) { }
                public ValueSourceAttribute(System.Type sourceType, string sourceName) { }
                public System.Type? SourceType { get; }
            }
            [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property)] public sealed class DatapointAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property)] public sealed class DatapointSourceAttribute : System.Attribute { }
        }
        """;

    private const string MsTestMetadata = """
        namespace Microsoft.VisualStudio.TestTools.UnitTesting
        {
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class TestClassAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public class TestMethodAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class DataTestMethodAttribute : TestMethodAttribute { }
            public enum DynamicDataSourceType { AutoDetect, Property, Method, Field }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class DynamicDataAttribute : System.Attribute
            {
                public DynamicDataAttribute(string name) { }
                public DynamicDataAttribute(string name, DynamicDataSourceType sourceKind) { }
                public DynamicDataAttribute(string name, System.Type sourceType, DynamicDataSourceType sourceKind) { }
                public DynamicDataAttribute(string name, System.Type sourceType, params object[] args) { }
                public System.Type? DynamicDataDisplayNameDeclaringType { get; set; }
                public string? DynamicDataDisplayName { get; set; }
            }
            [System.AttributeUsage(System.AttributeTargets.Assembly)] public sealed class AssemblyFixtureProviderAttribute(System.Type fixtureType) : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class AssemblyInitializeAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class AssemblyCleanupAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class ClassInitializeAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class ClassCleanupAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class TestInitializeAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class TestCleanupAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class GlobalTestInitializeAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class GlobalTestCleanupAttribute : System.Attribute { }
        }
        """;

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
