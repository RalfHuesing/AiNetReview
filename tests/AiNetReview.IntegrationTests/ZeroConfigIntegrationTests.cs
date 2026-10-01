namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetReview;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using Microsoft.Extensions.DependencyInjection;

public sealed class ZeroConfigIntegrationTests
{
    [Theory]
    [InlineData("review", "review")]
    [InlineData("baseline", "baseline")]
    [InlineData("--cmd", "--cmd")]
    [InlineData(null, "unknown")]
    public void Program_GetCommandCategory_UsesFirstArgument(string? firstArgument, string expected)
    {
        var args = firstArgument is null ? Array.Empty<string>() : [firstArgument, "ignored"];

        Assert.Equal(expected, Program.GetCommandCategory(args));
    }

    [Fact]
    public async Task ReviewCommand_SubcommandBootstrapsProjectConfigAndPublishesReports()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-zero-config-");
        var projectRoot = await CreateProjectAsync(tempDirectory.DirectoryPath);
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await using var services = BuildServices();

        var defaultCommand = await InvokeAsync(["review", projectRoot], services);
        AssertSuccessfulReview(projectRoot, defaultCommand);
        Assert.True(File.Exists(configPath));
        var generatedConfig = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"schemaVersion\": 1", generatedConfig, StringComparison.Ordinal);
        Assert.Contains("\"solution\": \"Sample.slnx\"", generatedConfig, StringComparison.Ordinal);
        Assert.Contains("\"outputDirectory\": \"audit-reporting\"", generatedConfig, StringComparison.Ordinal);
        using (var generatedDocument = JsonDocument.Parse(generatedConfig))
        {
            Assert.Equal(JsonValueKind.True, generatedDocument.RootElement.GetProperty("analyses")
                .GetProperty("indirection-drift-candidates").GetProperty("enabled").ValueKind);
            var analyses = generatedDocument.RootElement.GetProperty("analyses");
            Assert.Equal(90, analyses.GetProperty("method-control-flow-outliers").GetProperty("testOptions").GetProperty("percentile").GetInt32());
            var codeSize = analyses.GetProperty("code-size-candidates");
            Assert.True(codeSize.GetProperty("enabled").GetBoolean());
            Assert.Equal(90, codeSize.GetProperty("percentile").GetInt32());
            Assert.Equal(80, codeSize.GetProperty("minMemberCodeLines").GetInt32());
            Assert.Equal(300, codeSize.GetProperty("extremeMemberCodeLines").GetInt32());
            Assert.Equal(300, codeSize.GetProperty("minTypeCodeLines").GetInt32());
            Assert.Equal(800, codeSize.GetProperty("extremeTypeCodeLines").GetInt32());
            Assert.Equal(1000, codeSize.GetProperty("extremeFileLines").GetInt32());
            Assert.Equal(131072, codeSize.GetProperty("extremeFileUtf8Bytes").GetInt32());
            Assert.Equal(131072, codeSize.GetProperty("testOptions").GetProperty("extremeFileUtf8Bytes").GetInt32());
            Assert.Equal("external_library", analyses.GetProperty("dead-code-candidates").GetProperty("apiSurface").GetString());
            Assert.Empty(analyses.GetProperty("dead-code-candidates").GetProperty("entryPointAttributes").EnumerateArray());
            Assert.True(analyses.GetProperty("structural-duplication-candidates").GetProperty("enabled").GetBoolean());
            Assert.Equal(8, analyses.EnumerateObject().Count());
        }

        var reviewSubcommand = await InvokeAsync(["review", projectRoot], services);
        AssertSuccessfulReview(projectRoot, reviewSubcommand);

        File.Delete(configPath);
        var missingConfig = await InvokeAsync(["review", projectRoot], services);
        AssertSuccessfulReview(projectRoot, missingConfig);
        Assert.True(File.Exists(configPath));
    }

    [Theory]
    [InlineData("--cmd", "baseline")]
    [InlineData("--config", "ainetreview.json")]
    [InlineData("unknown")]
    [InlineData("C:\\project")]
    [InlineData("review", "--cmd", "baseline")]
    [InlineData("review", "--config", "ainetreview.json")]
    public async Task ReviewCommand_RejectsMissingOrUnknownSubcommandAndLegacyOptions(params string[] args)
    {
        await using var services = BuildServices();

        var result = await InvokeAsync(args, services);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        using var response = JsonDocument.Parse(result.Error);
        Assert.Equal("INVALID_INPUT", response.RootElement.GetProperty("code").GetString());
        Assert.Contains("review [project-path]", response.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewCommand_WithoutArguments_DisplaysHelp()
    {
        await using var services = BuildServices();

        var result = await InvokeAsync([], services);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AiNetReview", result.Output, StringComparison.Ordinal);
        Assert.Contains("review", result.Output, StringComparison.Ordinal);
        Assert.Contains("baseline", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public async Task ReviewCommand_WithHelpFlag_DisplaysHelp(string helpFlag)
    {
        await using var services = BuildServices();

        var result = await InvokeAsync([helpFlag], services);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AiNetReview", result.Output, StringComparison.Ordinal);
        Assert.Contains("review", result.Output, StringComparison.Ordinal);
        Assert.Contains("baseline", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("baseline")]
    public async Task ReviewCommand_SubcommandHelp_DisplaysSubcommandHelp(string subcommand)
    {
        await using var services = BuildServices();

        var result = await InvokeAsync([subcommand, "--help"], services);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(subcommand, result.Output, StringComparison.Ordinal);
        Assert.Contains("project-path", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task BaselineCommand_BootstrapsConfigurationAndWritesBaseline()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-cli-baseline-");
        var projectRoot = await CreateProjectAsync(tempDirectory.DirectoryPath);
        File.Delete(Path.Combine(projectRoot, "ainetreview.json"));
        await using var services = BuildServices();

        var relativeProjectRoot = Path.GetRelativePath(Environment.CurrentDirectory, projectRoot);
        var result = await InvokeAsync(["baseline", relativeProjectRoot], services);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var response = JsonDocument.Parse(Assert.Single(result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(projectRoot, "ainetreview.json")));
        using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "ainetreview.json"))))
        {
            var analyses = config.RootElement.GetProperty("analyses");
            Assert.Equal(90, analyses.GetProperty("method-control-flow-outliers").GetProperty("testOptions").GetProperty("percentile").GetInt32());
            Assert.Equal(131072, analyses.GetProperty("code-size-candidates").GetProperty("testOptions").GetProperty("extremeFileUtf8Bytes").GetInt32());
            Assert.Equal("external_library", analyses.GetProperty("dead-code-candidates").GetProperty("apiSurface").GetString());
        }
        Assert.True(File.Exists(Path.Combine(projectRoot, "audit-reporting", "baseline.json")));
    }

    [Fact]
    public async Task ReviewCommand_WithoutSolutionReturnsInvalidInputWithoutCreatingConfig()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-zero-config-no-solution-");
        var projectRoot = tempDirectory.CreateSubdirectory("empty-project");
        await using var services = BuildServices();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(["review", projectRoot], services, output, error);

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
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
