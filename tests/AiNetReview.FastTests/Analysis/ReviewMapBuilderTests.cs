namespace AiNetReview.FastTests.Analysis;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class ReviewMapBuilderTests
{
    [Fact]
    public async Task BuildAsync_MapsUtf8FilesNamespacesPartialAndNestedTypesAndDirectRoleScopedEdges()
    {
        using var fixture = CreateFixture();

        var maps = await ReviewMapBuilder.BuildAsync(fixture.Context, CancellationToken.None);

        var production = Assert.Single(maps.Projects.Where(static project => project.Role == ProjectRole.Production));
        var tests = Assert.Single(maps.Projects.Where(static project => project.Role == ProjectRole.Tests));
        Assert.Equal("src/DömÄin/DömÄin.csproj", production.ProjectPath);
        Assert.Equal(ProjectClassificationReason.ProjectNameSuffix, tests.ClassificationReason);
        Assert.All(production.Key, static character => Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        var projectReference = Assert.Single(tests.References);
        Assert.Equal(production.Key, projectReference.TargetProjectKey);
        Assert.Equal(production.ProjectPath, projectReference.TargetProjectPath);
        Assert.Equal(ProjectRole.Production, projectReference.TargetRole);

        var firstFile = Assert.Single(maps.Files.Where(file => file.ProjectKey == production.Key && file.RelativePath == "src/DömÄin/First.cs"));
        Assert.Equal(Encoding.UTF8.GetByteCount(fixture.FirstSource), firstFile.Utf8Bytes);
        Assert.Equal(2, firstFile.Lines);
        Assert.Equal(new[] { "Example.Domain" }, firstFile.Namespaces);
        Assert.Equal(Encoding.UTF8.GetByteCount(fixture.SecondSource), Assert.Single(maps.Files.Where(file => file.RelativePath == "src/DömÄin/Second.cs")).Utf8Bytes);
        var mixedNamespaceFile = Assert.Single(maps.Files.Where(file => file.ProjectKey == production.Key && file.RelativePath == "src/DömÄin/Global.cs"));
        Assert.Equal(new[] { "<global>", "Example.Domain" }, mixedNamespaceFile.Namespaces);
        Assert.DoesNotContain(maps.Files, static file => file.RelativePath.EndsWith(".g.cs", StringComparison.Ordinal));

        var widget = Assert.Single(maps.Types.Where(type => type.ProjectKey == production.Key && type.Name == "Widget"));
        Assert.Equal("Example.Domain", widget.Namespace);
        Assert.Equal(new[]
        {
            new ReviewMapTypeDeclaration("src/DömÄin/First.cs", 2),
            new ReviewMapTypeDeclaration("src/DömÄin/Second.cs", 1),
        }, widget.Declarations);
        Assert.Contains(maps.Types, type => type.ProjectKey == production.Key
            && type.Name == "Widget.Nested" && type.Namespace == "Example.Domain"
            && type.FullyQualifiedName.Contains("Widget.Nested", StringComparison.Ordinal));
        Assert.Contains(maps.Types, type => type.ProjectKey == production.Key && type.Name == "GlobalType" && type.Namespace == "<global>");
        Assert.DoesNotContain(maps.Types, static type => type.Name == "GeneratedType");

        var baseType = Assert.Single(maps.Types.Where(type => type.ProjectKey == production.Key && type.Name == "BaseType"));
        var middleType = Assert.Single(maps.Types.Where(type => type.ProjectKey == production.Key && type.Name == "MiddleType"));
        var deepType = Assert.Single(maps.Types.Where(type => type.ProjectKey == production.Key && type.Name == "DeepType"));
        var consumer = Assert.Single(maps.Types.Where(type => type.ProjectKey == tests.Key && type.Name == "WidgetTests"));
        Assert.Contains(maps.TypeEdges, edge => edge.FromTypeId == middleType.Id && edge.ToTypeId == baseType.Id && !edge.IsTestContext);
        Assert.Contains(maps.TypeEdges, edge => edge.FromTypeId == deepType.Id && edge.ToTypeId == middleType.Id && !edge.IsTestContext);
        Assert.DoesNotContain(maps.TypeEdges, edge => edge.FromTypeId == deepType.Id && edge.ToTypeId == baseType.Id);
        var testEdge = Assert.Single(maps.TypeEdges.Where(edge => edge.FromTypeId == consumer.Id && edge.ToTypeId == deepType.Id));
        Assert.True(testEdge.IsTestContext);
        Assert.Contains(testEdge.Witnesses, witness => witness.ProjectKey == tests.Key
            && witness.SourcePath == "tests/Domain.Tests/WidgetTests.cs" && witness.Line == 1);
        Assert.All(maps.TypeEdges, edge =>
        {
            var from = maps.Types.Single(type => type.Id == edge.FromTypeId);
            var to = maps.Types.Single(type => type.Id == edge.ToTypeId);
            Assert.Equal(edge.IsTestContext, from.ProjectKey == tests.Key && to.ProjectKey == production.Key);
        });

        var repeated = await ReviewMapBuilder.BuildAsync(fixture.Context, CancellationToken.None);
        Assert.Equal(Fingerprint(maps), Fingerprint(repeated));
        using var reversedFixture = CreateFixture(reverseInsertionOrder: true);
        var reversedMaps = await ReviewMapBuilder.BuildAsync(reversedFixture.Context, CancellationToken.None);
        Assert.Equal(Fingerprint(maps), Fingerprint(reversedMaps));
    }

    [Fact]
    public async Task BuildAsync_PropagatesCancellation()
    {
        using var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReviewMapBuilder.BuildAsync(fixture.Context, cancellation.Token));
    }

    private static string Fingerprint(ReviewMaps maps) => string.Join('\n',
        maps.Projects.Select(project => $"P:{project.Key}:{project.ProjectPath}:{project.Role}:{string.Join(',', project.References.Select(static reference => reference.TargetProjectKey))}")
            .Concat(maps.Files.Select(file => $"F:{file.ProjectKey}:{file.RelativePath}:{file.Utf8Bytes}:{file.Lines}:{string.Join(',', file.Namespaces)}:{string.Join(',', file.TypeIds)}"))
            .Concat(maps.Types.Select(type => $"T:{type.Id}:{type.ProjectKey}:{type.Namespace}:{type.Name}:{string.Join(',', type.Declarations.Select(static declaration => $"{declaration.SourcePath}:{declaration.Line}"))}"))
            .Concat(maps.TypeEdges.Select(edge => $"E:{edge.FromTypeId}:{edge.ToTypeId}:{edge.IsTestContext}:{string.Join(',', edge.Witnesses.Select(static witness => $"{witness.ProjectKey}/{witness.SourcePath}:{witness.Line}:{witness.Kind}"))}")));

    private static Fixture CreateFixture(bool reverseInsertionOrder = false)
    {
        var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "AiNetReview-ReviewMap", Guid.NewGuid().ToString("N"));
        var production = ProjectId.CreateNewId();
        var tests = ProjectId.CreateNewId();
        var references = FastTestReferences.CreatePlatformReferences().ToArray();
        if (reverseInsertionOrder)
        {
            AddProject(workspace, tests, "Domain.Tests", Path.Combine(root, "tests", "Domain.Tests", "Domain.Tests.csproj"), references);
            AddProject(workspace, production, "DömÄin", Path.Combine(root, "src", "DömÄin", "DömÄin.csproj"), references);
        }
        else
        {
            AddProject(workspace, production, "DömÄin", Path.Combine(root, "src", "DömÄin", "DömÄin.csproj"), references);
            AddProject(workspace, tests, "Domain.Tests", Path.Combine(root, "tests", "Domain.Tests", "Domain.Tests.csproj"), references);
        }
        var linkedPath = Path.Combine(root, "shared", "Linked.cs");
        Assert.True(workspace.TryApplyChanges(workspace.CurrentSolution.AddProjectReference(tests, new ProjectReference(production))));

        var firstSource = "namespace Example.Domain;\r\npublic partial class Widget { public string Label = \"café\"; }";
        var secondSource = "namespace Example.Domain; public partial class Widget { public class Nested { } }\npublic class BaseType { }\npublic class MiddleType : BaseType { }\npublic class DeepType : MiddleType { }";
        void AddDocuments()
        {
            AddDocument(workspace, production, "First.cs", firstSource, Path.Combine(root, "src", "DömÄin", "First.cs"));
            AddDocument(workspace, production, "Second.cs", secondSource, Path.Combine(root, "src", "DömÄin", "Second.cs"));
            AddDocument(workspace, production, "Global.cs", "namespace Example.Domain { public class ScopedType { } }\npublic class GlobalType { }", Path.Combine(root, "src", "DömÄin", "Global.cs"));
            AddDocument(workspace, production, "Generated.g.cs", "namespace Example.Domain; public class GeneratedType { }", Path.Combine(root, "src", "DömÄin", "Generated.g.cs"));
            AddDocument(workspace, production, "Linked.cs", "namespace Example.Domain; public class LinkedType { }", linkedPath);
            AddDocument(workspace, tests, "WidgetTests.cs", "namespace Example.Domain.Tests; public class WidgetTests { public Example.Domain.DeepType? Subject; }", Path.Combine(root, "tests", "Domain.Tests", "WidgetTests.cs"));
            AddDocument(workspace, tests, "Linked.cs", "namespace Example.Domain; public class LinkedType { }", linkedPath);
        }
        AddDocuments();

        return new Fixture(workspace, new ReviewContext(workspace.CurrentSolution, root), firstSource, secondSource);
    }

    private static void AddProject(AdhocWorkspace workspace, ProjectId id, string name, string path, MetadataReference[] references) =>
        workspace.AddProject(ProjectInfo.Create(
            id,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: path,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            metadataReferences: references));

    private static void AddDocument(AdhocWorkspace workspace, ProjectId id, string name, string source, string path) =>
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(id),
            name,
            filePath: path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));

    private sealed class Fixture(AdhocWorkspace workspace, ReviewContext context, string firstSource, string secondSource) : IDisposable
    {
        public ReviewContext Context { get; } = context;
        public string FirstSource { get; } = firstSource;
        public string SecondSource { get; } = secondSource;
        public void Dispose() => workspace.Dispose();
    }
}
