namespace AiNetReview.IntegrationTests.Configuration;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;

public sealed class SolutionLoaderTests
{
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public async Task LoadAsync_LoadsAndCompilesSupportedSolutions(string extension)
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, extension, "namespace Sample; public sealed class SampleType { }");
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson($"Sample{extension}"));

        using var loaded = await new SolutionLoader().LoadAsync(config);

        var project = Assert.Single(loaded.Solution.Projects);
        Assert.Equal("Sample", project.Name);
    }

    [Fact]
    public async Task LoadAsync_ReportsCompilationErrorsAsAnalysisFailure()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class Broken { Missing.Library.Type Value = new(); }",
            addMissingReference: true);
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("compilation error", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsSourceIncludedFromOutsideProjectRoot()
    {
        using var temp = TestTempDirectory.Create();
        var outsideSource = temp.CreateFile("outside.cs", "namespace Sample; public sealed class ExternalSource { }");
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class SampleType { }",
            outsideSource);
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("outside the project root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsSourceIncludedFromOutputDirectory()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class SampleType { }",
            sourceInOutputDirectory: true);
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx", "Sample/reports"));

        var error = await Assert.ThrowsAsync<InvalidReviewInputException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("must not contain C# source files", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RuleRegistry Registry() => new([new TemplateNoOpRule()]);

    private static string ConfigurationJson(string solution, string outputDirectory = "reports") => $$"""
        {
          "schemaVersion": 1,
          "solution": "{{solution}}",
          "outputDirectory": "{{outputDirectory}}",
          "rules": { "template-noop": {} }
        }
        """;

    private static async Task<string> CreateProjectAsync(
        TestTempDirectory temp,
        string solutionExtension,
        string source,
        string? externalSource = null,
        bool addMissingReference = false,
        bool sourceInOutputDirectory = false)
    {
        var root = temp.GetPath("mini-project");
        var projectDirectory = Path.Combine(root, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var externalItem = externalSource is null
            ? string.Empty
            : $"<ItemGroup><Compile Include=\"{SecurityElementEscape(Path.GetRelativePath(projectDirectory, externalSource))}\" /></ItemGroup>";
        var missingReference = addMissingReference
            ? "<ItemGroup><Reference Include=\"Missing.Library\" /></ItemGroup>"
            : string.Empty;
        await File.WriteAllTextAsync(projectFile,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>{externalItem}{missingReference}</Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Class1.cs"), source);
        if (sourceInOutputDirectory)
        {
            var outputDirectory = Path.Combine(projectDirectory, "reports");
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "Included.cs"), "namespace Sample; public sealed class Included { }");
        }
        await RestoreAsync(projectFile, projectDirectory);

        var solutionPath = Path.Combine(root, "Sample" + solutionExtension);
        if (solutionExtension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        }
        else
        {
            const string projectGuid = "{18C3BBA5-AB06-4CB3-A205-CF3122FFCD43}";
            await File.WriteAllTextAsync(solutionPath, $$"""
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.0.31903.59
                MinimumVisualStudioVersion = 10.0.40219.1
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Sample", "Sample\Sample.csproj", "{{projectGuid}}"
                EndProject
                Global
                 GlobalSection(SolutionConfigurationPlatforms) = preSolution
                  Debug|Any CPU = Debug|Any CPU
                 EndGlobalSection
                 GlobalSection(ProjectConfigurationPlatforms) = postSolution
                  {{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                  {{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                 EndGlobalSection
                EndGlobal
                """);
        }

        return root;
    }

    private static async Task RestoreAsync(string projectFile, string workingDirectory)
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {output}");
    }

    private static string SecurityElementEscape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
