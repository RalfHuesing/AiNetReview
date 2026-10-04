namespace AiNetReview.IntegrationTests.Reporting;

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.TypeDependencyCycleCandidates;

public sealed class ReviewMapsIntegrationTests
{
    [Fact]
    public async Task ReviewRun_PublishesZeroFindingMapsWithProjectRolesUtf8SizesAndResolvableRoutes()
    {
        using var temp = TestTempDirectory.Create("ainet-review-maps-");
        var root = temp.DirectoryPath;
        var productionDirectory = Path.Combine(root, "src", "Domain");
        var testsDirectory = Path.Combine(root, "tests", "Domain.Tests");
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(testsDirectory);

        const string productionProjectXml = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>";
        const string testsProjectXml = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/Domain/Domain.csproj\" /></ItemGroup></Project>";
        const string firstSource = "namespace Example.Domain;\npublic partial class Widget { public string Label = \"café\"; }\n";
        const string secondSource = "namespace Example.Domain; public partial class Widget { public class Nested { } }\npublic class BaseType { }\npublic class DeepType : BaseType { }";
        const string testsSource = "namespace Example.Domain.Tests; public class WidgetTests { public Example.Domain.DeepType? Subject; }";
        var productionProjectPath = Path.Combine(productionDirectory, "Domain.csproj");
        var testsProjectPath = Path.Combine(testsDirectory, "Domain.Tests.csproj");
        await File.WriteAllTextAsync(productionProjectPath, productionProjectXml);
        await File.WriteAllTextAsync(testsProjectPath, testsProjectXml);
        await File.WriteAllTextAsync(Path.Combine(productionDirectory, "First.cs"), firstSource);
        await File.WriteAllTextAsync(Path.Combine(productionDirectory, "Second.cs"), secondSource);
        await File.WriteAllTextAsync(Path.Combine(testsDirectory, "WidgetTests.cs"), testsSource);
        await RestoreProjectAsync(productionProjectPath, productionDirectory);
        await RestoreProjectAsync(testsProjectPath, testsDirectory);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
            "<Solution><Project Path=\"src/Domain/Domain.csproj\" /><Project Path=\"tests/Domain.Tests/Domain.Tests.csproj\" /></Solution>");

        var analysis = new TypeDependencyCycleCandidatesAnalysis();
        var config = new ReviewConfigValidator(new ReviewAnalysisRegistry([analysis])).Validate(root,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"type-dependency-cycle-candidates\":{}}}");
        using var loaded = await new SolutionLoader().LoadAsync(config);
        var review = await new ReviewRunner().RunAsync(config, loaded);

        Assert.Empty(review.Findings);
        var maps = Assert.IsType<ReviewMaps>(review.Maps);
        var production = Assert.Single(maps.Projects.Where(static project => project.Role == ProjectRole.Production));
        var tests = Assert.Single(maps.Projects.Where(static project => project.Role == ProjectRole.Tests));
        Assert.Equal("src/Domain/Domain.csproj", production.ProjectPath);
        Assert.Equal("tests/Domain.Tests/Domain.Tests.csproj", tests.ProjectPath);
        Assert.Equal(production.Key, Assert.Single(tests.References).TargetProjectKey);
        Assert.Equal(Encoding.UTF8.GetByteCount(firstSource), Assert.Single(maps.Files.Where(file => file.RelativePath == "src/Domain/First.cs")).Utf8Bytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(secondSource), Assert.Single(maps.Files.Where(file => file.RelativePath == "src/Domain/Second.cs")).Utf8Bytes);
        var widget = Assert.Single(maps.Types.Where(type => type.ProjectKey == production.Key && type.Name == "Widget"));
        Assert.Equal(2, widget.Declarations.Count);
        var testType = Assert.Single(maps.Types.Where(type => type.ProjectKey == tests.Key && type.Name == "WidgetTests"));
        var deepType = Assert.Single(maps.Types.Where(type => type.ProjectKey == production.Key && type.Name == "DeepType"));
        Assert.Contains(maps.TypeEdges, edge => edge.IsTestContext
            && edge.FromTypeId == testType.Id
            && edge.ToTypeId == deepType.Id);

        var published = await new MarkdownReportWriter().WriteAsync(config, review);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, published.RunId);
        var mapRootPath = Path.Combine(runDirectory, "maps", "index.md");
        var projectsPath = Path.Combine(runDirectory, "maps", "projects.md");
        var auditPath = Path.Combine(runDirectory, "maps", "audit", "index.md");
        var productionStructurePath = Path.Combine(runDirectory, "maps", "production", production.Key, "structure.md");
        var productionDependenciesPath = Path.Combine(runDirectory, "maps", "production", production.Key, "dependencies.md");
        var testsStructurePath = Path.Combine(runDirectory, "maps", "tests", tests.Key, "structure.md");
        var testsDependenciesPath = Path.Combine(runDirectory, "maps", "tests", tests.Key, "dependencies.md");
        Assert.All(new[] { mapRootPath, projectsPath, auditPath, productionStructurePath, productionDependenciesPath, testsStructurePath, testsDependenciesPath },
            path => Assert.True(File.Exists(path), $"Expected map was not published: '{path}'."));
        var mapIndex = await File.ReadAllTextAsync(mapRootPath);
        Assert.Contains("`projects.md`", mapIndex, StringComparison.Ordinal);
        Assert.Contains("`audit/index.md`", mapIndex, StringComparison.Ordinal);
        var projects = await File.ReadAllTextAsync(projectsPath);
        Assert.Contains("production", projects, StringComparison.Ordinal);
        Assert.Contains("tests", projects, StringComparison.Ordinal);
        Assert.Contains(production.ProjectPath, projects, StringComparison.Ordinal);
        Assert.Contains(tests.ProjectPath, projects, StringComparison.Ordinal);
        Assert.Contains($"`production/{production.Key}/structure.md`", projects, StringComparison.Ordinal);
        Assert.Contains($"`production/{production.Key}/dependencies.md`", projects, StringComparison.Ordinal);
        Assert.Contains($"`tests/{tests.Key}/structure.md`", projects, StringComparison.Ordinal);
        Assert.Contains($"`tests/{tests.Key}/dependencies.md`", projects, StringComparison.Ordinal);
        Assert.Contains($"`{production.ProjectPath}` (production); `production/{production.Key}/structure.md`", projects, StringComparison.Ordinal);
        var audit = await File.ReadAllTextAsync(auditPath);
        Assert.Contains("No findings in this audit.", audit, StringComparison.Ordinal);
        var structure = await File.ReadAllTextAsync(productionStructurePath);
        Assert.Contains("First.cs", structure, StringComparison.Ordinal);
        var firstFile = Assert.Single(maps.Files.Where(file => file.ProjectKey == production.Key && file.RelativePath == "src/Domain/First.cs"));
        Assert.Contains($"| `src/Domain/First.cs` | {firstFile.Lines} | {Encoding.UTF8.GetByteCount(firstSource)} |", structure, StringComparison.Ordinal);
        Assert.Contains($"| `src/Domain` | 2 | {Encoding.UTF8.GetByteCount(firstSource) + Encoding.UTF8.GetByteCount(secondSource)} |", structure, StringComparison.Ordinal);
        Assert.Contains("Widget", structure, StringComparison.Ordinal);
        Assert.Contains("Nested", structure, StringComparison.Ordinal);
        var dependencies = await File.ReadAllTextAsync(testsDependenciesPath);
        Assert.Contains("`WidgetTests` → `DeepType`", dependencies, StringComparison.Ordinal);
        Assert.Contains("### `tests/Domain.Tests/WidgetTests.cs`", dependencies, StringComparison.Ordinal);
        Assert.Contains("L1 ExplicitTypeUse", dependencies, StringComparison.Ordinal);
        Assert.DoesNotContain(testType.Id, dependencies, StringComparison.Ordinal);
        Assert.DoesNotContain(deepType.Id, dependencies, StringComparison.Ordinal);
        var productionDependencies = await File.ReadAllTextAsync(productionDependenciesPath);
        Assert.Contains("## Project routes", productionDependencies, StringComparison.Ordinal);
        Assert.Contains("Domain.Tests/Domain.Tests.csproj", productionDependencies, StringComparison.Ordinal);
        Assert.Contains("### `DeepType`", productionDependencies, StringComparison.Ordinal);
        Assert.Contains("## Consumer declarations", productionDependencies, StringComparison.Ordinal);
        Assert.Contains("### `tests/Domain.Tests/Domain.Tests.csproj`", productionDependencies, StringComparison.Ordinal);
        Assert.Contains("- `WidgetTests`: `tests/Domain.Tests/WidgetTests.cs:1`", productionDependencies, StringComparison.Ordinal);
        Assert.Contains("- Test consumers (`tests/Domain.Tests/Domain.Tests.csproj`): `WidgetTests`", productionDependencies, StringComparison.Ordinal);
        Assert.DoesNotContain("#### Test consumers", productionDependencies, StringComparison.Ordinal);
        Assert.DoesNotContain("##### `tests/Domain.Tests/Domain.Tests.csproj`", productionDependencies, StringComparison.Ordinal);
        Assert.DoesNotContain(testType.Id, productionDependencies, StringComparison.Ordinal);
        Assert.DoesNotContain(deepType.Id, productionDependencies, StringComparison.Ordinal);
    }

    private static async Task RestoreProjectAsync(string projectFile, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {await output}{await error}");
    }
}
