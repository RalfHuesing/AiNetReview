namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using Microsoft.Extensions.DependencyInjection;

public sealed class ZeroConfigIntegrationTests
{
    [Fact]
    public async Task ReviewCommand_ProjectPathVariantsAndMissingExplicitConfigBootstrapAndPublishReports()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-zero-config-");
        var projectRoot = await CreateProjectAsync(tempDirectory.DirectoryPath);
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await using var services = BuildServices();

        var defaultCommand = await InvokeAsync([projectRoot], services);
        AssertSuccessfulReview(projectRoot, defaultCommand);
        Assert.True(File.Exists(configPath));
        var generatedConfig = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"schemaVersion\": 1", generatedConfig, StringComparison.Ordinal);
        Assert.Contains("\"solution\": \"Sample.slnx\"", generatedConfig, StringComparison.Ordinal);
        Assert.Contains("\"outputDirectory\": \"audit-reporting\"", generatedConfig, StringComparison.Ordinal);

        var reviewSubcommand = await InvokeAsync(["review", projectRoot], services);
        AssertSuccessfulReview(projectRoot, reviewSubcommand);

        File.Delete(configPath);
        var explicitMissingConfig = await InvokeAsync(["review", "--config", configPath], services);
        AssertSuccessfulReview(projectRoot, explicitMissingConfig);
        Assert.True(File.Exists(configPath));
    }

    [Fact]
    public async Task ReviewCommand_WithoutSolutionReturnsInvalidInputWithoutCreatingConfig()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-zero-config-no-solution-");
        var projectRoot = tempDirectory.CreateSubdirectory("empty-project");
        await using var services = BuildServices();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync([projectRoot], services, output, error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        using var response = JsonDocument.Parse(error.ToString());
        Assert.Equal("INVALID_INPUT", response.RootElement.GetProperty("code").GetString());
        Assert.Contains("No .sln or .slnx file", response.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot, "ainetreview.json")));
    }

    private static async Task<(int ExitCode, string Output, string Error)> InvokeAsync(
        string[] args,
        ServiceProvider services)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await new ReviewCommand().InvokeAsync(args, services, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static void AssertSuccessfulReview(string projectRoot, (int ExitCode, string Output, string Error) result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        var line = Assert.Single(result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using var response = JsonDocument.Parse(line);
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        var indexPath = response.RootElement.GetProperty("indexPath").GetString()!;
        Assert.StartsWith("audit-reporting/", indexPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot, indexPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static async Task<string> CreateProjectAsync(string testRoot)
    {
        var projectRoot = Path.Combine(testRoot, "SampleProject");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Class1.cs"), "namespace Sample; public sealed class SampleType { public void Run() { } }");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        return projectRoot;
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {await stdout}{await stderr}");
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
