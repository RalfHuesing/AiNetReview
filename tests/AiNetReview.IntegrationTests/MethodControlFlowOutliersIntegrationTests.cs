namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using AiNetReview.Core.Rules;
using Microsoft.Extensions.DependencyInjection;

public sealed class MethodControlFlowOutliersIntegrationTests
{
    [Fact]
    public async Task ReviewCommand_WithProductionRule_ReportsComplexMethodWithEvidenceAndOmitsSimpleMethod()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-control-flow-");
        var projectRoot = tempDirectory.GetPath("review-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var sourcePath = Path.Combine(projectDirectory, "Class1.cs");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(sourcePath,
            "namespace Sample;\n"
            + "public sealed class Sample\n"
            + "{\n"
            + "    public int HighlyBranched(int value)\n"
            + "    {\n"
            + "        var result = 0;\n"
            + "        if (value == 0) result++;\n"
            + "        if (value == 1) result++;\n"
            + "        if (value == 2) result++;\n"
            + "        if (value == 3) result++;\n"
            + "        if (value == 4) result++;\n"
            + "        if (value == 5) result++;\n"
            + "        if (value == 6) result++;\n"
            + "        if (value == 7) result++;\n"
            + "        return result;\n"
            + "    }\n"
            + "\n"
            + "    public int Simple(int value) => value + 1;\n"
            + "}\n");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"method-control-flow-outliers\":{}}}");

        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", "--config", configPath], provider, output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var runId = response.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        Assert.Equal(
            new[] { "method-control-flow-outliers" },
            provider.GetRequiredService<RuleRegistry>().Rules.Select(static rule => rule.Descriptor.RuleId));

        var rulesDirectory = Path.Combine(projectRoot, "reports", runId!, "rules");
        var reportPath = Path.Combine(rulesDirectory, "method-control-flow-outliers.md");
        Assert.True(File.Exists(reportPath));
        Assert.Equal(new[] { "method-control-flow-outliers.md" },
            Directory.GetFiles(rulesDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("M:Sample.Sample.HighlyBranched(System.Int32)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("M:Sample.Sample.Simple(System.Int32)", report, StringComparison.Ordinal);
        Assert.Contains("decisionCount=8", report, StringComparison.Ordinal);
        Assert.Contains("maxDecisionNesting=1", report, StringComparison.Ordinal);
        Assert.Contains("sourceSpanLines=", report, StringComparison.Ordinal);
        Assert.Contains("[Sample/Class1.cs:4](../../../Sample/Class1.cs#L4)", report, StringComparison.Ordinal);
        Assert.Contains("**Method declaration**", report, StringComparison.Ordinal);
        Assert.Contains("code: `public int HighlyBranched(int value)`", report, StringComparison.Ordinal);
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
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {await stdout}{await stderr}");
    }
}
