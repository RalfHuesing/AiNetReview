namespace AiNetReview.FastTests.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

public sealed class TestFrameworkClassifierTests
{
    [Fact]
    public void IsActiveTestRoot_RecognizesXunitV2V3AndPerAttributeStaticExclusions()
    {
        const string source = """
            using Xunit;
            using NUnit.Framework;
            public sealed class ProjectFactAttribute : Xunit.FactAttribute { }
            public sealed class Tests
            {
                [Fact] public void Fact() { }
                [ProjectFact] public void ProjectDerivedFact() { }
                [Theory] public void Theory(int value) { }
                [CustomFact] public void DerivedFact() { }
                [CustomTheory] public void DerivedTheory(int value) { }
                [Xunit.v3.InterfaceFact] public void V3InterfaceFact() { }
                [Xunit.v3.DerivedInterfaceFact] public void DerivedV3InterfaceFact() { }
                [Xunit.v3.InterfaceFact(Skip = "reason")] public void V3Skipped() { }
                [Xunit.v3.InterfaceFact(Explicit = true)] public void V3Explicit() { }
                [Fact("reason")] public void SkippedByConstructor() { }
                [Fact(Skip = "reason")] public void SkippedByProperty() { }
                [Fact(SkipWhen = "Condition")] public void DynamicSkipWhen() { }
                [Fact(SkipUnless = "Condition")] public void DynamicSkipUnless() { }
                [Fact(Explicit = true)] public void Explicit() { }
                [Fact(Skip = "reason", SkipWhen = "Condition")] public void DynamicSkipOverridesStaticSkip() { }
                [Fact(Explicit = true, SkipWhen = "Condition")] public void ExplicitDespiteDynamicSkip() { }
                [Fact(Skip = null)] public void NullSkip() { }
                [Fact(Skip = "reason"), NUnit.Framework.Test] public void OtherActiveFrameworkAttribute() { }
                [Fact(Explicit = true), Theory] public void OtherActiveXunitAttribute() { }
                public void Helper() { }
            }
            """;

        using var fixture = Compile(source);

        AssertRoots(fixture, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Tests.Fact"] = true,
            ["Tests.ProjectDerivedFact"] = true,
            ["Tests.Theory"] = true,
            ["Tests.DerivedFact"] = true,
            ["Tests.DerivedTheory"] = true,
            ["Tests.V3InterfaceFact"] = true,
            ["Tests.DerivedV3InterfaceFact"] = true,
            ["Tests.V3Skipped"] = false,
            ["Tests.V3Explicit"] = false,
            ["Tests.SkippedByConstructor"] = false,
            ["Tests.SkippedByProperty"] = false,
            ["Tests.DynamicSkipWhen"] = true,
            ["Tests.DynamicSkipUnless"] = true,
            ["Tests.Explicit"] = false,
            ["Tests.DynamicSkipOverridesStaticSkip"] = true,
            ["Tests.ExplicitDespiteDynamicSkip"] = false,
            ["Tests.NullSkip"] = true,
            ["Tests.OtherActiveFrameworkAttribute"] = true,
            ["Tests.OtherActiveXunitAttribute"] = true,
            ["Tests.Helper"] = false,
        });
    }

    [Fact]
    public void IsActiveTestRoot_RecognizesNUnitRootsAndWholeFixtureExclusions()
    {
        const string source = """
            using NUnit.Framework;
            [TestFixture]
            public sealed class Tests
            {
                [Test] public void Test() { }
                [TestCase(1)] public void TestCase(int value) { }
                [TestCaseSource] public void TestCaseSource(int value) { }
                [CustomTest] public void DerivedTest() { }
                [CustomTestCase] public void DerivedTestCase(int value) { }
                [CustomTestCaseSource] public void DerivedTestCaseSource(int value) { }
                [TestCase(Ignore = "one row is ignored")] public void CaseIgnore(int value) { }
                [TestCase(Explicit = true)] public void CaseExplicit(int value) { }
                [Ignore] [Test] public void IgnoredMethod() { }
                [CustomIgnore] [Test] public void DerivedIgnoredMethod() { }
                [Explicit] [Test] public void ExplicitMethod() { }
                [CustomExplicit] [Test] public void DerivedExplicitMethod() { }
            }
            [TestFixture(Ignore = "fixture ignored")]
            public sealed class IgnoredFixture { [Test] public void Test() { } }
            [CustomTestFixture(Ignore = "derived fixture ignored")]
            public sealed class DerivedIgnoredFixture { [Test] public void Test() { } }
            [TestFixture(Ignore = "")]
            public sealed class EmptyFixtureIgnore { [Test] public void Test() { } }
            [TestFixture(Explicit = true)]
            public sealed class ExplicitFixture { [Test] public void Test() { } }
            [CustomTestFixture(Explicit = true)]
            public sealed class DerivedExplicitFixture { [Test] public void Test() { } }
            [TestFixture(Ignore = "fixture ignored")]
            public sealed class ExcludedBeforeOtherAttribute
            {
                [Test, Xunit.Fact] public void Test() { }
            }
            """;

        using var fixture = Compile(source);

        AssertRoots(fixture, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Tests.Test"] = true,
            ["Tests.TestCase"] = true,
            ["Tests.TestCaseSource"] = true,
            ["Tests.DerivedTest"] = true,
            ["Tests.DerivedTestCase"] = true,
            ["Tests.DerivedTestCaseSource"] = true,
            ["Tests.CaseIgnore"] = true,
            ["Tests.CaseExplicit"] = true,
            ["Tests.IgnoredMethod"] = false,
            ["Tests.DerivedIgnoredMethod"] = false,
            ["Tests.ExplicitMethod"] = false,
            ["Tests.DerivedExplicitMethod"] = false,
            ["IgnoredFixture.Test"] = false,
            ["DerivedIgnoredFixture.Test"] = false,
            ["EmptyFixtureIgnore.Test"] = true,
            ["ExplicitFixture.Test"] = false,
            ["DerivedExplicitFixture.Test"] = false,
            ["ExcludedBeforeOtherAttribute.Test"] = false,
        });
    }

    [Fact]
    public void IsActiveTestRoot_RequiresMSTestClassAndHonorsIgnoreOnMethodOrFixture()
    {
        const string source = """
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            [TestClass]
            public sealed class Tests
            {
                [TestMethod] public void TestMethod() { }
                [DataTestMethod] public void DataTestMethod(int value) { }
                [CustomTestMethod] public void DerivedMethod() { }
                [CustomDataTestMethod] public void DerivedDataMethod(int value) { }
                [Ignore] [TestMethod] public void IgnoredMethod() { }
            }
            public sealed class MissingTestClass
            {
                [TestMethod] public void TestMethod() { }
            }
            [CustomTestClass]
            public sealed class DerivedTestClass
            {
                [DataTestMethod] public void DataTestMethod(int value) { }
            }
            [TestClass, Ignore]
            public sealed class IgnoredFixture
            {
                [TestMethod] public void TestMethod() { }
            }
            [TestClass]
            public sealed class FixtureWithMethodAndHelper
            {
                [TestMethod, CustomIgnore] public void DerivedIgnore() { }
                public void Helper() { }
            }
            [TestClass, CustomIgnore]
            public sealed class FixtureWithDerivedIgnore
            {
                [TestMethod] public void TestMethod() { }
            }
            """;

        using var fixture = Compile(source);

        AssertRoots(fixture, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Tests.TestMethod"] = true,
            ["Tests.DataTestMethod"] = true,
            ["Tests.DerivedMethod"] = true,
            ["Tests.DerivedDataMethod"] = true,
            ["Tests.IgnoredMethod"] = false,
            ["MissingTestClass.TestMethod"] = false,
            ["DerivedTestClass.DataTestMethod"] = true,
            ["IgnoredFixture.TestMethod"] = false,
            ["FixtureWithMethodAndHelper.DerivedIgnore"] = false,
            ["FixtureWithMethodAndHelper.Helper"] = false,
            ["FixtureWithDerivedIgnore.TestMethod"] = false,
        });
    }

    [Fact]
    public void IsActiveTestRoot_IgnoresSourceLookalikesAndRequiresKnownTestMetadata()
    {
        const string source = """
            namespace Xunit
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class FactAttribute : System.Attribute { }
            }
            namespace NUnit.Framework
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class TestAttribute : System.Attribute { }
            }
            namespace Microsoft.VisualStudio.TestTools.UnitTesting
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class TestMethodAttribute : System.Attribute { }
            }
            public sealed class SourceLookalike
            {
                [Xunit.Fact] public void LooksLikeFact() { }
                [NUnit.Framework.Test] public void LooksLikeNUnitTest() { }
                [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod] public void LooksLikeMSTestMethod() { }
                public void Helper() { }
            }
            """;

        using var testNamedFixture = Compile(source, includeFrameworkMetadata: false, projectName: "Example.Tests");
        using var productionFixture = Compile(
            "public sealed class Production { public void Run() { } }",
            includeFrameworkMetadata: false,
            projectName: "Example");
        using var frameworkReferenceOnly = Compile(
            "public sealed class Helpers { public void Run() { } }",
            includeFrameworkMetadata: true,
            projectName: "Example");

        AssertRoots(testNamedFixture, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["SourceLookalike.LooksLikeFact"] = false,
            ["SourceLookalike.LooksLikeNUnitTest"] = false,
            ["SourceLookalike.LooksLikeMSTestMethod"] = false,
            ["SourceLookalike.Helper"] = false,
        });
        AssertRoots(productionFixture, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Production.Run"] = false,
        });
        AssertRoots(frameworkReferenceOnly, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Helpers.Run"] = false,
        });
    }

    private static void AssertRoots(CompilationFixture fixture, IReadOnlyDictionary<string, bool> expected)
    {
        var root = fixture.Document.GetSyntaxRootAsync().GetAwaiter().GetResult()!;
        var semanticModel = fixture.Document.GetSemanticModelAsync().GetAwaiter().GetResult()!;
        var actual = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(
                method => method.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.ValueText + "." + method.Identifier.ValueText,
                method => TestFrameworkClassifier.IsActiveTestRoot(
                    fixture.Project,
                    (IMethodSymbol)semanticModel.GetDeclaredSymbol(method)!),
                StringComparer.Ordinal);

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }

    private static CompilationFixture Compile(
        string source,
        bool includeFrameworkMetadata = true,
        string projectName = "Example.Tests")
    {
        var references = FastTestReferences.CreatePlatformReferences().ToList();
        if (includeFrameworkMetadata)
        {
            references.Add(TestFrameworkMetadata.Reference);
        }

        var workspace = new FastTestWorkspace();
        var projectPath = Path.Combine(Path.GetTempPath(), "AiNetReview", projectName + ".csproj");
        var projectId = workspace.AddProject(projectName, parseOptions: CSharpParseOptions.Default,
            projectFilePath: projectPath, metadataReferences: references);
        var documentId = workspace.AddDocument(projectId, "Tests.cs", source,
            Path.Combine(Path.GetDirectoryName(projectPath)!, "Tests.cs"));

        var project = workspace.Solution.GetProject(projectId)!;
        var compilation = project.GetCompilationAsync().GetAwaiter().GetResult()!;
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        return new CompilationFixture(workspace, project, project.GetDocument(documentId)!);
    }

    private sealed class CompilationFixture(FastTestWorkspace workspace, Project project, Document document) : IDisposable
    {
        public Project Project { get; } = project;

        public Document Document { get; } = document;

        public void Dispose() => workspace.Dispose();
    }

    private static class TestFrameworkMetadata
    {
        private const string Source = """
            using System;
            namespace Xunit
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class FactAttribute : Attribute
                {
                    public FactAttribute(string? skip = null) { Skip = skip; }
                    public string? Skip { get; set; }
                    public bool Explicit { get; set; }
                    public string? SkipWhen { get; set; }
                    public string? SkipUnless { get; set; }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TheoryAttribute : FactAttribute { public TheoryAttribute(string? skip = null) : base(skip) { } }
                public sealed class CustomFactAttribute : FactAttribute { }
                public sealed class CustomTheoryAttribute : TheoryAttribute { }
            }
            namespace Xunit.v3
            {
                public interface IFactAttribute
                {
                    string? Skip { get; }
                    bool Explicit { get; }
                    string? SkipWhen { get; }
                    string? SkipUnless { get; }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class InterfaceFactAttribute : Attribute, IFactAttribute
                {
                    public string? Skip { get; set; }
                    public bool Explicit { get; set; }
                    public string? SkipWhen { get; set; }
                    public string? SkipUnless { get; set; }
                }
                public sealed class DerivedInterfaceFactAttribute : InterfaceFactAttribute { }
            }
            namespace NUnit.Framework
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestCaseAttribute : TestAttribute
                {
                    public TestCaseAttribute(params object[] arguments) { }
                    public string? Ignore { get; set; }
                    public bool Explicit { get; set; }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestCaseSourceAttribute : TestAttribute { }
                public sealed class CustomTestAttribute : TestAttribute { }
                public sealed class CustomTestCaseAttribute : TestCaseAttribute { }
                public sealed class CustomTestCaseSourceAttribute : TestCaseSourceAttribute { }
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public class TestFixtureAttribute : Attribute
                {
                    public string? Ignore { get; set; }
                    public bool Explicit { get; set; }
                }
                public sealed class CustomTestFixtureAttribute : TestFixtureAttribute { }
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
                public class IgnoreAttribute : Attribute { }
                public sealed class CustomIgnoreAttribute : IgnoreAttribute { }
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
                public class ExplicitAttribute : Attribute { }
                public sealed class CustomExplicitAttribute : ExplicitAttribute { }
            }
            namespace Microsoft.VisualStudio.TestTools.UnitTesting
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestMethodAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class DataTestMethodAttribute : TestMethodAttribute { }
                public sealed class CustomTestMethodAttribute : TestMethodAttribute { }
                public sealed class CustomDataTestMethodAttribute : DataTestMethodAttribute { }
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public class TestClassAttribute : Attribute { }
                public sealed class CustomTestClassAttribute : TestClassAttribute { }
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
                public class IgnoreAttribute : Attribute { }
                public sealed class CustomIgnoreAttribute : IgnoreAttribute { }
            }
            """;

        public static MetadataReference Reference { get; } = CreateReference();

        private static MetadataReference CreateReference()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(Source);
            var compilation = CSharpCompilation.Create(
                "FrameworkContracts",
                [syntaxTree],
                FastTestReferences.CreatePlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var assembly = new MemoryStream();
            var emit = compilation.Emit(assembly);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return MetadataReference.CreateFromImage(assembly.ToArray(), filePath: "xunit.nunit.mstest.contracts.dll");
        }
    }
}
