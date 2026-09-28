namespace AiNetReview.IntegrationTests;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public async Task ReviewCommand_ProductionDuplicateCodeRulePublishesCurrentCrossProjectClusters()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-duplicate-code-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var firstProject = Path.Combine(projectRoot, "ProductA");
        var secondProject = Path.Combine(projectRoot, "ProductB");
        Directory.CreateDirectory(firstProject);
        Directory.CreateDirectory(secondProject);
        var firstProjectFile = Path.Combine(firstProject, "ProductA.csproj");
        var secondProjectFile = Path.Combine(secondProject, "ProductB.csproj");
        var firstSource = Path.Combine(firstProject, "First.cs");
        var secondSource = Path.Combine(secondProject, "Second.cs");
        const string projectContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        await File.WriteAllTextAsync(firstProjectFile, projectContent);
        await File.WriteAllTextAsync(secondProjectFile, projectContent);
        var exactBody = BuildDuplicateBody();
        var nearBody = exactBody.Replace("var v8 = v7 + 8;", "var v8 = v7 * 8;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(firstSource, WrapDuplicateMethod("FirstContainer", "RunFirst", exactBody));
        await File.WriteAllTextAsync(secondSource,
            WrapDuplicateMethod("SecondContainer", "RunSecond", exactBody)
            + WrapDuplicateMethod("NearContainer", "RunNear", nearBody));
        await RestoreProjectAsync(firstProjectFile, firstProject);
        await RestoreProjectAsync(secondProjectFile, secondProject);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"ProductA/ProductA.csproj\" /><Project Path=\"ProductB/ProductB.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");

        await WriteDuplicateConfigAsync(configPath, "exact");
        var exact = await RunProductionDuplicateCodeAsync(configPath);
        var exactReport = await ReadDuplicateCodeReportAsync(projectRoot, exact.RunId);
        Assert.Equal(1, exact.Detected);
        Assert.Contains("\"minimumSimilarity\": \"exact\"", exactReport, StringComparison.Ordinal);
        Assert.Contains("2 methods;", exactReport, StringComparison.Ordinal);
        Assert.Matches("[0-9]+(?:\\.[0-9]+)?% similarity \\(minimum [0-9]+(?:\\.[0-9]+)?%\\)", exactReport);
        Assert.Contains("ProductA/First.cs:", exactReport, StringComparison.Ordinal);
        Assert.Contains("ProductB/Second.cs:", exactReport, StringComparison.Ordinal);
        Assert.Contains("| Source | Signal | Other Locations |", exactReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Metrics", exactReport, StringComparison.Ordinal);
        AssertMarkdownLinksResolve(Path.Combine(projectRoot, "reports", exact.RunId));

        await WriteDuplicateConfigAsync(configPath, "fuzzy");
        var fuzzy = await RunProductionDuplicateCodeAsync(configPath);
        var fuzzyReport = await ReadDuplicateCodeReportAsync(projectRoot, fuzzy.RunId);
        Assert.Equal(1, fuzzy.Detected);
        Assert.Contains("\"minimumSimilarity\": \"fuzzy\"", fuzzyReport, StringComparison.Ordinal);
        Assert.Contains("3 methods;", fuzzyReport, StringComparison.Ordinal);
        Assert.Contains("similarity (minimum 65.0%)", fuzzyReport, StringComparison.Ordinal);
        AssertMarkdownLinksResolve(Path.Combine(projectRoot, "reports", fuzzy.RunId));

        await File.WriteAllTextAsync(secondSource,
            WrapDuplicateMethod("SecondContainer", "RunChanged", BuildAlternateDuplicateBody()));
        await WriteDuplicateConfigAsync(configPath, "exact");
        var empty = await RunProductionDuplicateCodeAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        var emptyIndex = await File.ReadAllTextAsync(Path.Combine(emptyRunDirectory, "index.md"));
        Assert.Contains("No findings were found.", emptyIndex, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "rules", "duplicate-code-candidates.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot, "reports", exact.RunId, "index.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot, "reports", fuzzy.RunId, "index.md")));

        var publishedRuns = Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length;
        await File.WriteAllTextAsync(secondSource, "public static class Broken { public static int Run( { }");
        var (failureExitCode, failureError) = await InvokeProductionDuplicateCodeAsync(configPath);
        Assert.Equal(3, failureExitCode);
        using (var failureResponse = JsonDocument.Parse(failureError))
        {
            Assert.Equal("ANALYSIS_FAILED", failureResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);

        await File.WriteAllTextAsync(secondSource,
            WrapDuplicateMethod("SecondContainer", "RunChanged", BuildAlternateDuplicateBody()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var (cancelledExitCode, cancelledError) = await InvokeProductionDuplicateCodeAsync(configPath, cancellation.Token);
        Assert.Equal(130, cancelledExitCode);
        using (var cancelledResponse = JsonDocument.Parse(cancelledError))
        {
            Assert.Equal("CANCELLED", cancelledResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);
    }

    [Fact]
    public async Task ReviewCommand_ProductionDeadCodeRulePublishesRepeatedAndEmptyAudits()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-dead-code-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var sourcePath = Path.Combine(projectDirectory, "Class1.cs");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; internal sealed class UnusedType { public void HiddenMethod() { } } public sealed class PublicApi { public void Entry() { } }");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{}}}");

        var first = await RunProductionDeadCodeAsync(configPath);
        var firstReport = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", first.RunId, "rules", "dead-code-candidates.md"));
        Assert.Equal(1, first.Detected);
        Assert.Contains("Type without known use", firstReport, StringComparison.Ordinal);
        Assert.Contains("| Source | Signal | Other Locations |", firstReport, StringComparison.Ordinal);
        Assert.Contains("reflection", firstReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external_library", firstReport, StringComparison.Ordinal);

        var second = await RunProductionDeadCodeAsync(configPath);
        var secondReport = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", second.RunId, "rules", "dead-code-candidates.md"));
        Assert.Equal(1, second.Detected);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Contains("Type without known use", secondReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class PublicApi { public void Entry() { } }");
        var empty = await RunProductionDeadCodeAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        var emptyIndex = await File.ReadAllTextAsync(Path.Combine(emptyRunDirectory, "index.md"));
        Assert.Contains("No findings were found.", emptyIndex, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "rules", "dead-code-candidates.md")));

        var publishedRunsBeforeCancellation = Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var cancelledOutput = new StringWriter();
        using var cancelledError = new StringWriter();
        await using var cancelledProvider = BuildProductionServices();
        var cancelledExitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", configPath],
            cancelledProvider,
            cancelledOutput,
            cancelledError,
            cancellation.Token);
        Assert.Equal(130, cancelledExitCode);
        Assert.Empty(cancelledOutput.ToString());
        Assert.Equal(publishedRunsBeforeCancellation, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);
    }

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
        var thirdRunDirectory = Path.Combine(projectRoot, "reports", third.RunId);
        var thirdIndex = await File.ReadAllTextAsync(Path.Combine(thirdRunDirectory, "index.md"));
        Assert.Contains("No findings were found.", thirdIndex, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(thirdRunDirectory, "rules")));

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
        Assert.Contains("Fixture scenario 'base' requires review of FixtureCaseA.", report, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "dead-code-candidates", "duplicate-code-candidates", "fixture-finding", "method-control-flow-outliers" },
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

    private static async Task<(string RunId, int Detected)> RunProductionDeadCodeAsync(string configPath)
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(["review", "--config", configPath], provider, output, error);
        Assert.True(exitCode == 0, $"Production dead-code audit failed: {error}");
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static async Task WriteDuplicateConfigAsync(string configPath, string minimumSimilarity)
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = "Sample.slnx",
            outputDirectory = "reports",
            rules = new Dictionary<string, object>
            {
                ["duplicate-code-candidates"] = new { minTokens = 30, minimumSimilarity },
            },
        });
        await File.WriteAllTextAsync(configPath, json);
    }

    private static async Task<(string RunId, int Detected)> RunProductionDuplicateCodeAsync(string configPath)
    {
        var (exitCode, output, error) = await InvokeProductionDuplicateCodeCoreAsync(configPath, CancellationToken.None);
        Assert.True(exitCode == 0, $"Production duplicate-code audit failed: {error}");
        Assert.Empty(error);
        using var response = JsonDocument.Parse(output);
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static async Task<(int ExitCode, string Error)> InvokeProductionDuplicateCodeAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, _, error) = await InvokeProductionDuplicateCodeCoreAsync(configPath, cancellationToken);
        return (exitCode, error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> InvokeProductionDuplicateCodeCoreAsync(
        string configPath,
        CancellationToken cancellationToken)
    {
        await using var provider = BuildProductionServices();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", configPath], provider, output, error, cancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static Task<string> ReadDuplicateCodeReportAsync(string projectRoot, string runId) =>
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "rules", "duplicate-code-candidates.md"));

    private static void AssertMarkdownLinksResolve(string runDirectory)
    {
        foreach (var reportPath in Directory.GetFiles(runDirectory, "*.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(reportPath);
            var linkStart = 0;
            while ((linkStart = content.IndexOf("](", linkStart, StringComparison.Ordinal)) >= 0)
            {
                var targetStart = linkStart + 2;
                var targetEnd = content.IndexOf(')', targetStart);
                if (targetEnd < 0)
                {
                    break;
                }

                var target = Uri.UnescapeDataString(content[targetStart..targetEnd]);
                linkStart = targetEnd + 1;
                var fragmentIndex = target.IndexOf('#');
                var relativePath = fragmentIndex < 0 ? target : target[..fragmentIndex];
                var resolved = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(reportPath)!,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(resolved), $"Markdown link does not resolve: '{target}' from '{reportPath}'.");
            }
        }
    }

    private static string BuildDuplicateBody() => string.Join(" ", Enumerable.Range(1, 20).Select(index =>
        $"var v{index} = {(index == 1 ? "value" : $"v{index - 1}")} + {index};")) + " return v20;";

    private static string BuildAlternateDuplicateBody() => string.Join(" ", Enumerable.Range(1, 20).Select(index =>
        $"var w{index} = {(index == 1 ? "value" : $"w{index - 1}")} * {index};")) + " return w20;";

    private static string WrapDuplicateMethod(string className, string methodName, string body) =>
        $"namespace {className} {{ public static class {className} {{ public static int {methodName}(int value) {{ {body} }} }} }}\n";

    private static ServiceProvider BuildProductionServices()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
