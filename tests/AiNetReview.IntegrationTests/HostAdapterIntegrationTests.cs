namespace AiNetReview.IntegrationTests;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using AiNetReview.Core.Rules;
using AiNetReview.IntegrationTests.FixtureRules;
using Microsoft.Extensions.DependencyInjection;

public sealed class HostAdapterIntegrationTests
{
    [Fact]
    public async Task ReviewCommand_RepeatedFixtureScansUseCurrentSourcesAndOptions()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-adapter-repeated-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var sourcePath = Path.Combine(projectDirectory, "Class1.cs");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(sourcePath,
            "namespace Sample; public sealed class Sample { public void FixtureCaseA() { } public void FixtureCaseB() { } }");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");

        var first = await RunFixtureAsync(projectRoot, configPath, "base");
        Assert.Equal(2, first.Detected);
        var firstReport = await ReadRuleReportAsync(projectRoot, first.RunId);
        Assert.Contains("FixtureCaseA", firstReport, StringComparison.Ordinal);
        Assert.Contains("FixtureCaseB", firstReport, StringComparison.Ordinal);
        Assert.Contains("scenario 'base'", firstReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(sourcePath,
            "namespace Sample; public sealed class Sample { public void FixtureCaseC() { } }");
        var second = await RunFixtureAsync(projectRoot, configPath, "alternate");
        Assert.Equal(1, second.Detected);
        var secondReport = await ReadRuleReportAsync(projectRoot, second.RunId);
        Assert.Contains("FixtureCaseC", secondReport, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureCaseA", secondReport, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureCaseB", secondReport, StringComparison.Ordinal);
        Assert.Contains("scenario 'alternate'", secondReport, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario 'base'", secondReport, StringComparison.Ordinal);

        var third = await RunFixtureAsync(projectRoot, configPath, "none");
        Assert.Equal(0, third.Detected);
        var thirdReport = await ReadRuleReportAsync(projectRoot, third.RunId);
        Assert.Contains("| Detected | 0 |", thirdReport, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureCaseA", thirdReport, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureCaseC", thirdReport, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(projectRoot, "reports", first.RunId, "index.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot, "reports", second.RunId, "index.md")));
    }

    [Fact]
    public async Task ReviewCommand_RejectsAdditionalOptionsWithMachineReadableInputError()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", "C:\\project\\ainetreview.json", "--extra"],
            provider,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        using var errorResponse = JsonDocument.Parse(error.ToString());
        Assert.Equal("INVALID_INPUT", errorResponse.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReviewCommand_WhenCancelled_ReturnsCancelledJsonAndExitCode()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", "C:\\project\\ainetreview.json"],
            provider,
            output,
            error,
            cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Empty(output.ToString());
        using var errorResponse = JsonDocument.Parse(error.ToString());
        Assert.Equal("CANCELLED", errorResponse.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReviewCommand_WithTestOnlyFixtureRegistration_PublishesCurrentFinding()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-adapter-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Class1.cs"),
            "namespace Sample; public sealed class Sample { public void FixtureCaseA() { } }");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"fixture-finding\":{}}}");

        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddSingleton<IReviewRule, FixtureFindingRule>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", configPath],
            provider,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var runId = response.RootElement.GetProperty("runId").GetString();
        var report = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId!, "rules", "fixture-finding.md"));
        Assert.Contains("fixturecasea", report, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "fixture-finding", "method-control-flow-outliers" },
            provider.GetRequiredService<RuleRegistry>().Rules.Select(static rule => rule.Descriptor.RuleId));
    }

    [Fact]
    public async Task ReviewCommand_WhenMarkupSnapshotFailsPublishesNoRun()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-adapter-markup-failure-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Class1.cs"),
            "namespace Sample; public sealed class Sample { }");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        var markupPath = Path.Combine(projectDirectory, "Locked.razor");
        await File.WriteAllTextAsync(markupPath, "unreadable during snapshot");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{}}}");
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddSingleton<IReviewRule, MarkupFixtureRule>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var locked = new FileStream(markupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", configPath],
            provider,
            output,
            error);

        Assert.Equal(3, exitCode);
        Assert.Empty(output.ToString());
        using var response = JsonDocument.Parse(error.ToString());
        Assert.Equal("ANALYSIS_FAILED", response.RootElement.GetProperty("code").GetString());
        Assert.Empty(Directory.GetDirectories(Path.Combine(projectRoot, "reports")));
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

    private static async Task<(string RunId, int Detected)> RunFixtureAsync(string projectRoot, string configPath, string scenario)
    {
        var config = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = "Sample.slnx",
            outputDirectory = "reports",
            rules = new Dictionary<string, object> { ["fixture-finding"] = new { scenario } },
        });
        await File.WriteAllTextAsync(configPath, config);
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddSingleton<IReviewRule, FixtureFindingRule>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", configPath], provider, output, error);
        Assert.True(exitCode == 0, $"Fixture scan '{scenario}' failed: {error}");
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static Task<string> ReadRuleReportAsync(string projectRoot, string runId) =>
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "rules", "fixture-finding.md"));

    private sealed class MarkupFixtureRule : IReviewRule
    {
        public RuleDescriptor Descriptor { get; } = new(
            "dead-code-candidates",
            "Fixture",
            1,
            "Fixture rule for markup snapshot publication coverage.",
            "Fixture behavior.",
            ["Is the snapshot available?"]);

        public Task<RuleResult> ExecuteAsync(
            ReviewContext context,
            RuleOptions options,
            CancellationToken cancellationToken) => Task.FromResult(RuleResult.Empty);
    }
}
