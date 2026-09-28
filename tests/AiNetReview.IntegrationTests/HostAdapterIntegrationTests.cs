namespace AiNetReview.IntegrationTests;

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using AiNetReview.Core.Rules;
using AiNetReview.IntegrationTests.FixtureRules;
using Microsoft.Extensions.DependencyInjection;

public sealed class HostAdapterIntegrationTests
{
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
            new[] { "fixture-finding", "template-noop" },
            provider.GetRequiredService<RuleRegistry>().Rules.Select(static rule => rule.Descriptor.RuleId));
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
}
