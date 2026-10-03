namespace AiNetReview.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using AiNetReview.Core.ReviewAnalyses;
using Microsoft.Extensions.DependencyInjection;

public sealed class MethodControlFlowOutliersIntegrationTests
{
    [Fact]
    public async Task ReviewCommand_UsesIndependentTestPercentilePerProjectAndOptionsDoNotChangeSnapshotSelection()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-control-flow-test-options-");
        var projectRoot = tempDirectory.GetPath("review-project");
        var productionProject = Path.Combine(projectRoot, "Sample");
        var testProject = Path.Combine(projectRoot, "Tests");
        Directory.CreateDirectory(productionProject);
        Directory.CreateDirectory(testProject);
        var productionFile = Path.Combine(productionProject, "Sample.csproj");
        var testFile = Path.Combine(testProject, "Sample.Tests.csproj");
        const string projectXml = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        await File.WriteAllTextAsync(productionFile, projectXml);
        await File.WriteAllTextAsync(testFile, projectXml);
        var source = CreateMethodsWithDecisionCounts([8, 10, 11]);
        await File.WriteAllTextAsync(Path.Combine(productionProject, "Class1.cs"), source);
        await File.WriteAllTextAsync(Path.Combine(testProject, "Class1.cs"), source);
        await RestoreProjectAsync(productionFile, productionProject);
        await RestoreProjectAsync(testFile, testProject);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"Sample/Sample.csproj\" /><Project Path=\"Tests/Sample.Tests.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"method-control-flow-outliers\":{\"percentile\":50,\"testOptions\":{\"percentile\":99}}}}");
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var command = new ReviewCommand();
        var baselineResult = await InvokeAsync(command, projectRoot, provider, "baseline");
        Assert.Equal(0, baselineResult.ExitCode);
        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", "baseline.json")));
        var baselineFiles = baseline.RootElement.GetProperty("files").GetRawText();

        var p99 = await InvokeAsync(command, projectRoot, provider, "review");
        Assert.Equal(0, p99.ExitCode);
        Assert.Empty(p99.Error);
        var p99RunId = GetRunId(p99.Output);
        var p99ProductionDirectory = Path.Combine(projectRoot, "reports", p99RunId, "production", "all-findings");
        var p99TestDirectory = Path.Combine(projectRoot, "reports", p99RunId, "tests", "all-findings");
        var p99ReportPath = Path.Combine(p99ProductionDirectory, "method-control-flow-outliers.md");
        var p99TestReportPath = Path.Combine(p99TestDirectory, "method-control-flow-outliers.md");
        Assert.True(File.Exists(p99ReportPath));
        Assert.True(File.Exists(p99TestReportPath));
        var p99Report = await File.ReadAllTextAsync(p99ReportPath);
        var p99TestReport = await File.ReadAllTextAsync(p99TestReportPath);
        Assert.Contains("Total findings: 2", p99Report, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", p99TestReport, StringComparison.Ordinal);
        Assert.Contains("Effective options (production projects)", p99Report, StringComparison.Ordinal);
        Assert.Contains("Effective options (test projects)", p99Report, StringComparison.Ordinal);
        Assert.Contains("Test option sources: `percentile` explicitly configured.", p99Report, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Class1.cs (2 findings)", p99Report, StringComparison.Ordinal);
        Assert.Contains("#### File: Tests/Class1.cs (1 findings)", p99TestReport, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot, "reports", p99RunId, "production", "changed-files", "method-control-flow-outliers.md")));

        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"method-control-flow-outliers\":{\"percentile\":50,\"testOptions\":{\"percentile\":50}}}}");
        var p50 = await InvokeAsync(command, projectRoot, provider, "review");
        Assert.Equal(0, p50.ExitCode);
        Assert.Empty(p50.Error);
        var p50RunId = GetRunId(p50.Output);
        var p50Directory = Path.Combine(projectRoot, "reports", p50RunId, "production", "all-findings");
        var p50TestDirectory = Path.Combine(projectRoot, "reports", p50RunId, "tests", "all-findings");
        var p50Report = await File.ReadAllTextAsync(Path.Combine(p50Directory, "method-control-flow-outliers.md"));
        var p50TestReport = await File.ReadAllTextAsync(Path.Combine(p50TestDirectory, "method-control-flow-outliers.md"));
        Assert.Contains("Total findings: 2", p50Report, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2", p50TestReport, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Class1.cs (2 findings)", p50Report, StringComparison.Ordinal);
        Assert.Contains("#### File: Tests/Class1.cs (2 findings)", p50TestReport, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot, "reports", p50RunId, "production", "changed-files", "method-control-flow-outliers.md")));
        using var baselineAfter = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", "baseline.json")));
        Assert.Equal(baselineFiles, baselineAfter.RootElement.GetProperty("files").GetRawText());
    }

    [Fact]
    public async Task ReviewCommand_WithProductionAnalysis_ReportsComplexMethodWithEvidenceAndOmitsSimpleMethod()
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
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"method-control-flow-outliers\":{}}}");

        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", Path.GetDirectoryName(configPath)!], provider, output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var runId = response.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        Assert.Equal(
            new[] { "code-size-candidates", "dead-code-candidates", "duplicate-code-candidates", "indirection-drift-candidates", "method-control-flow-outliers", "missing-test-evidence-candidates", "non-ascii-identifiers", "structural-duplication-candidates", "type-dependency-cycle-candidates" },
            provider.GetRequiredService<ReviewAnalysisRegistry>().Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));

        var analysesDirectory = Path.Combine(projectRoot, "reports", runId!, "production", "all-findings");
        var reportPath = Path.Combine(analysesDirectory, "method-control-flow-outliers.md");
        Assert.True(File.Exists(reportPath));
        Assert.Contains("method-control-flow-outliers.md",
            Directory.GetFiles(analysesDirectory).Select(Path.GetFileName));

        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("8 decisions across", report, StringComparison.Ordinal);
        Assert.Contains("(cutoff 8)", report, StringComparison.Ordinal);
        Assert.Contains("each switch section or switch-expression arm once", report, StringComparison.Ordinal);
        Assert.Contains("each entire switch once", report, StringComparison.Ordinal);
        Assert.Contains("`&&`, `||`, and `??` do not add decisions", report, StringComparison.Ordinal);
        Assert.Contains("these measures are not cyclomatic complexity", report, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceSpanLines", report, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", report, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Class1.cs (1 findings)", report, StringComparison.Ordinal);
        Assert.Contains("## Findings", report, StringComparison.Ordinal);
        Assert.Contains("HighlyBranched", report, StringComparison.Ordinal);
        Assert.DoesNotContain("public int", report, StringComparison.Ordinal);
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

    private static async Task<(int ExitCode, string Output, string Error)> InvokeAsync(
        ReviewCommand command,
        string projectRoot,
        ServiceProvider services,
        string subcommand)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await command.InvokeAsync([subcommand, projectRoot], services, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateMethodsWithDecisionCounts(IReadOnlyList<int> counts)
    {
        var builder = new System.Text.StringBuilder("namespace Sample; public sealed class FactAttribute : System.Attribute { public string? Skip { get; set; } } public sealed class Cases {\n");
        for (var index = 0; index < counts.Count; index++)
        {
            if (index == counts.Count - 1)
            {
                builder.Append("[Fact(Skip = \"disabled case\")]\n");
            }

            builder.Append("public void Case").Append(index).Append("(bool value) {\n");
            for (var decision = 0; decision < counts[index]; decision++)
            {
                builder.Append("if (value) { var ignored").Append(decision).Append(" = ").Append(decision).Append("; }\n");
            }

            builder.Append("}\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    private static string GetRunId(string output)
    {
        using var response = JsonDocument.Parse(output);
        return response.RootElement.GetProperty("runId").GetString()!;
    }
}
