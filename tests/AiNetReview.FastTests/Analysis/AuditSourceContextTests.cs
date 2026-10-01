namespace AiNetReview.FastTests.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class AuditSourceContextTests
{
    [Fact]
    public async Task CreateAsync_PreparesSnapshotTypesRolesReferencesLocationsAndUncertaintyOrigins()
    {
        using var fixture = CreateFixture();
        var context = await AuditSourceContext.CreateAsync(fixture.Context);

        var production = Assert.Single(context.Projects.Where(static project => project.ProjectName == "Product"));
        var tests = Assert.Single(context.Projects.Where(static project => project.ProjectName == "Product.Tests"));
        Assert.Equal(ProjectRole.Production, production.Role);
        Assert.Equal(ProjectRole.Tests, tests.Role);

        var outer = Assert.Single(context.Types.Where(static type => type.FullyQualifiedName.EndsWith("Outer", StringComparison.Ordinal)));
        var nested = Assert.Single(context.Types.Where(static type => type.FullyQualifiedName.EndsWith("Outer.Nested", StringComparison.Ordinal)));
        Assert.Equal(outer.Id, nested.OuterTypeId);
        var partial = context.Types.Single(static type => type.FullyQualifiedName.EndsWith("Partial", StringComparison.Ordinal));
        Assert.Equal(3, partial.Declarations.Count);
        Assert.False(partial.IsGenerated);
        Assert.Contains(context.Types, static type => type.FullyQualifiedName.EndsWith("ValueCallback", StringComparison.Ordinal));
        Assert.Equal(2, context.Types.Count(static type => type.FullyQualifiedName.EndsWith("Other.AmbiguousTarget", StringComparison.Ordinal)));

        var sharedFiles = context.Files.Where(static file => file.Path == "src/Shared.cs").ToArray();
        Assert.Equal(2, sharedFiles.Length);
        Assert.NotEqual(sharedFiles[0].ProjectId, sharedFiles[1].ProjectId);

        var testType = Assert.Single(context.Types.Where(static type => type.FullyQualifiedName.EndsWith("WidgetTests", StringComparison.Ordinal)));
        var productionReferences = context.References.Where(reference => reference.SourceTypeId == testType.Id).ToArray();
        Assert.Contains(productionReferences, reference => reference.TargetTypeId == outer.Id);
        Assert.Contains(productionReferences, reference => reference.TargetTypeId == outer.Id && reference.TargetMemberId is not null);
        Assert.Contains(productionReferences, reference => reference.Kind == SolutionSymbolReferenceKind.MethodGroup
            && reference.TargetTypeId == outer.Id);
        var extensions = Assert.Single(context.Types.Where(static type => type.FullyQualifiedName.EndsWith("ExtensionMethods", StringComparison.Ordinal)));
        Assert.Contains(productionReferences, reference => reference.TargetTypeId == extensions.Id
            && reference.TargetMemberId?.Contains("Extend", StringComparison.Ordinal) == true);
        Assert.Contains(productionReferences, reference => reference.TargetMemberId?.Contains("Echo", StringComparison.Ordinal) == true);
        Assert.All(productionReferences, reference =>
        {
            Assert.False(reference.Location.Path.EndsWith("Generated.g.cs", StringComparison.Ordinal));
            Assert.True(reference.Location.StartLine > 0);
            Assert.True(reference.Location.StartColumn > 0);
        });

        Assert.Contains(context.References, reference => reference.SourceTypeId is null
            && reference.SourcePath.EndsWith("WidgetTests.cs", StringComparison.Ordinal)
            && reference.TargetTypeId == outer.Id);
        Assert.Contains(context.Uncertainties, uncertainty => uncertainty.OriginTypeId == testType.Id
            && uncertainty.SourcePath.EndsWith("WidgetTests.cs", StringComparison.Ordinal)
            && uncertainty.Location.StartLine > 0);
        Assert.Contains(context.Uncertainties, uncertainty => uncertainty.Reason is "TargetProjectAmbiguous" or "UnresolvedBinding"
            && uncertainty.OriginTypeId == testType.Id
            && uncertainty.CandidateSymbolId.Contains("Other.AmbiguousTarget", StringComparison.Ordinal));
        Assert.DoesNotContain(context.References, reference => reference.TargetMemberId?.Contains("Other.AmbiguousTarget", StringComparison.Ordinal) == true);
        foreach (var uncertainty in context.Uncertainties)
        {
            Assert.DoesNotContain(context.References, reference => reference.SourceProjectId == uncertainty.SourceProjectId
                && reference.SourcePath == uncertainty.SourcePath
                && reference.Location.Span == uncertainty.Location.Span);
        }
        Assert.DoesNotContain(context.References, reference => reference.SourcePath.EndsWith("Generated.g.cs", StringComparison.Ordinal));

        var repeated = await AuditSourceContext.CreateAsync(fixture.Context);
        Assert.Equal(context.References, repeated.References);
        Assert.Equal(context.Uncertainties, repeated.Uncertainties);
        Assert.DoesNotContain(context.References.GroupBy(reference =>
            (reference.SourceProjectId, reference.SourcePath, reference.Location.Span, reference.TargetTypeId)),
            static group => group.Count() > 1);
    }

    [Fact]
    public async Task CreateAsync_UsesMaterializedDocumentTextAfterBackingFileChanges()
    {
        using var fixture = CreateFixture();
        var sourcePath = Path.Combine(fixture.Root.DirectoryPath, "src", "Production.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace ChangedOnDisk; public class Replacement { }");

        var context = await AuditSourceContext.CreateAsync(fixture.Context);

        Assert.Contains(context.Types, static type => type.FullyQualifiedName.EndsWith("Outer", StringComparison.Ordinal));
        Assert.DoesNotContain(context.Types, static type => type.FullyQualifiedName.Contains("ChangedOnDisk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_PropagatesCancellationAndFailsWhenSemanticCoverageIsUnavailable()
    {
        using var fixture = CreateFixture();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AuditSourceContext.CreateAsync(fixture.Context, canceled.Token));

        using var workspace = new AdhocWorkspace();
        using var root = TestTempDirectory.Create();
        var emptyContext = new ReviewContext(workspace.CurrentSolution, root.DirectoryPath);
        await Assert.ThrowsAsync<AnalysisFailedException>(() => AuditSourceContext.CreateAsync(emptyContext));
    }

    private static Fixture CreateFixture()
    {
#pragma warning disable CA2000 // The fixture takes ownership of both disposable resources.
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
#pragma warning restore CA2000
        var productionId = ProjectId.CreateNewId();
        var testId = ProjectId.CreateNewId();
        var targetOneId = ProjectId.CreateNewId();
        var targetTwoId = ProjectId.CreateNewId();
        var productionPath = Path.Combine(root.DirectoryPath, "src", "Product.csproj");
        var testPath = Path.Combine(root.DirectoryPath, "tests", "Product.Tests", "Product.Tests.csproj");
        workspace.AddProject(CreateProject(productionId, "Product", productionPath));
        workspace.AddProject(CreateProject(testId, "Product.Tests", testPath));
        workspace.AddProject(CreateProject(targetOneId, "TargetOne", Path.Combine(root.DirectoryPath, "src", "TargetOne.csproj"), "Targets"));
        workspace.AddProject(CreateProject(targetTwoId, "TargetTwo", Path.Combine(root.DirectoryPath, "src", "TargetTwo.csproj"), "Targets"));
        var solution = workspace.CurrentSolution
            .AddProjectReference(testId, new ProjectReference(productionId))
            .AddProjectReference(testId, new ProjectReference(targetOneId))
            .AddProjectReference(testId, new ProjectReference(targetTwoId));
        Assert.True(workspace.TryApplyChanges(solution));

        AddDocument(workspace, productionId, root.DirectoryPath, "src/Production.cs", """
            namespace Product;
            public class Outer
            {
                public class Nested { public int Target(int value) => value; }
            }
            public static class ExtensionMethods { public static int Extend(this Outer.Nested value, int item) => value.Target(item); }
            public class Box<T> { public T Echo(T value) => value; }
            public delegate int ValueCallback(int value);
            public partial class Partial { public void First() { } }
            """);
        AddDocument(workspace, productionId, root.DirectoryPath, "src/Partial.cs", "namespace Product; public partial class Partial { public void Second() { } }");
        AddDocument(workspace, productionId, root.DirectoryPath, "src/Generated.g.cs", """
            // <auto-generated />
            namespace Product;
            public class GeneratedConsumer { public int Use() => new Outer.Nested().Target(1); }
            public partial class Partial { public void GeneratedPart() { } }
            """);
        AddDocument(workspace, productionId, root.DirectoryPath, "src/Shared.cs", "namespace Shared; public class SharedType { }");
        AddDocument(workspace, testId, root.DirectoryPath, "src/Shared.cs", "namespace Shared; public class SharedType { }");
        AddDocument(workspace, targetOneId, root.DirectoryPath, "src/TargetOne.cs", "namespace Other; public class AmbiguousTarget { public static void Use() { } }");
        AddDocument(workspace, targetTwoId, root.DirectoryPath, "src/TargetTwo.cs", "namespace Other; public class AmbiguousTarget { public static void Use() { } }");
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/WidgetTests.cs", """
            using static Product.Outer;
            using WidgetAlias = Product.Outer.Nested;
            using System;
            using Product;
            namespace Product.Tests;
            public partial class WidgetTests
            {
                public int Test(WidgetAlias value)
                {
                    Func<int, int> callback = value.Target;
                    var result = value.Extend(1);
                    return new Box<string>().Echo("x").Length + callback(2) + result;
                }

                public void Uncertain() { _ = new WidgetAlias().Target("wrong"); }
                public void Ambiguous() { Other.AmbiguousTarget.Use(); }
            }
            """);
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/WidgetTests.Part.cs", "namespace Product.Tests; public partial class WidgetTests { public void Second() { _ = new Product.Box<int>().Echo(1); } }");
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/Generated.g.cs", """
            // <auto-generated />
            public class GeneratedTestConsumer { public int Use() => new Product.Outer.Nested().Target(1); }
            """);

        var snapshotSolution = workspace.CurrentSolution;
        foreach (var project in snapshotSolution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var materialized = document.GetTextAsync().GetAwaiter().GetResult();
                snapshotSolution = snapshotSolution.WithDocumentText(document.Id, materialized, PreservationMode.PreserveIdentity);
            }
        }

        return new Fixture(workspace, root, new ReviewContext(snapshotSolution, root.DirectoryPath));
    }

    private static ProjectInfo CreateProject(ProjectId id, string name, string path, string? assemblyName = null) => ProjectInfo.Create(
        id,
        VersionStamp.Create(),
        assemblyName ?? name,
        name,
        LanguageNames.CSharp,
        filePath: path,
        compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
        parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
        metadataReferences: PlatformReferences());

    private static void AddDocument(AdhocWorkspace workspace, ProjectId projectId, string root, string relativePath, string source) =>
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            Path.GetFileName(relativePath),
            filePath: Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));

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

    private sealed class Fixture(AdhocWorkspace workspace, TestTempDirectory root, ReviewContext context) : IDisposable
    {
        public AdhocWorkspace Workspace { get; } = workspace;

        public TestTempDirectory Root { get; } = root;

        public ReviewContext Context { get; } = context;

        public void Dispose()
        {
            Workspace.Dispose();
            Root.Dispose();
        }
    }
}
