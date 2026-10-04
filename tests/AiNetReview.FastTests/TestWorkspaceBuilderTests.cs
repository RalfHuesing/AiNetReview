namespace AiNetReview.FastTests;

using System.Linq;
using System.IO;
using Microsoft.CodeAnalysis;

public sealed class TestWorkspaceBuilderTests
{
    [Fact]
    public async Task BuilderCreatesCompilableProjectsAndDocumentsWithoutCreatingFiles()
    {
        using var builder = new TestWorkspaceBuilder();
        var library = builder.AddProject("Library", FastTestReferences.CreatePlatformReferences());
        var consumer = builder.AddProject("Consumer", FastTestReferences.CreatePlatformReferences());
        builder.AddProjectReference(consumer, new ProjectReference(library));

        var libraryDocument = builder.AddDocument(library, "Library.cs", "namespace Example; public sealed class Item { }");
        builder.AddDocument(consumer, "Consumer.cs", "namespace Example; public sealed class Use { public Item Value { get; } = new(); }");

        Assert.Equal(Path.Combine(builder.RootPath, "Library.csproj"), builder.Solution.GetProject(library)!.FilePath);
        Assert.Equal(Path.Combine(builder.RootPath, "Library.cs"), builder.Solution.GetDocument(libraryDocument)!.FilePath);
        Assert.False(Directory.Exists(builder.RootPath));

        foreach (var project in builder.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }
}
