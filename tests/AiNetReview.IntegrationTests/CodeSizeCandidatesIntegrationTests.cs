namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using Microsoft.Extensions.DependencyInjection;

public sealed class CodeSizeCandidatesIntegrationTests
{
    [Fact]
    public async Task ReviewCommand_AppliesIndependentTestSizeThresholdsAndKeepsOptionsOutOfSourceSelection()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-code-size-test-options-");
        var projectRoot = tempDirectory.GetPath("review-project");
        var productionDirectory = Path.Combine(projectRoot, "Sample");
        var testDirectory = Path.Combine(projectRoot, "Tests");
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(testDirectory);
        var projectXml = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        var productionProject = Path.Combine(productionDirectory, "Sample.csproj");
        var testProject = Path.Combine(testDirectory, "Sample.Tests.csproj");
        await File.WriteAllTextAsync(productionProject, projectXml);
        await File.WriteAllTextAsync(testProject, projectXml);
        const string source = "namespace Sample; public sealed class Cases { public void Run() { var value = 1; value++; } }";
        await File.WriteAllTextAsync(Path.Combine(productionDirectory, "Class1.cs"), source);
        await File.WriteAllTextAsync(Path.Combine(testDirectory, "Class1.cs"), source);
        await RestoreProjectAsync(productionProject, productionDirectory);
        await RestoreProjectAsync(testProject, testDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"Sample/Sample.csproj\" /><Project Path=\"Tests/Sample.Tests.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        var lowOptionsJson = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"code-size-candidates\":{\"extremeMemberCodeLines\":1,\"testOptions\":{\"extremeMemberCodeLines\":1}}}}";
        await File.WriteAllTextAsync(configPath, lowOptionsJson);
        await using var services = BuildServices();

        var baseline = await InvokeAsync(["baseline", projectRoot], services);
        Assert.Equal(0, baseline.ExitCode);
        Assert.Empty(baseline.Error);
        using var baselineDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", "baseline.json")));
        var baselineFiles = baselineDocument.RootElement.GetProperty("files").GetRawText();

        var low = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(0, low.ExitCode);
        Assert.Empty(low.Error);
        var lowRunId = GetRunId(low.Output);
        var lowDirectory = Path.Combine(projectRoot, "reports", lowRunId);
        var lowReport = await File.ReadAllTextAsync(Path.Combine(lowDirectory, "production", "all-findings", "code-size-candidates.md"));
        var lowTestReport = await File.ReadAllTextAsync(Path.Combine(lowDirectory, "tests", "all-findings", "code-size-candidates.md"));
        Assert.Contains("Total findings: 1", lowReport, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Class1.cs (1 findings)", lowReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", lowTestReport, StringComparison.Ordinal);
        Assert.Contains("#### File: Tests/Class1.cs (1 findings)", lowTestReport, StringComparison.Ordinal);
        Assert.Contains("Test option sources:", lowReport, StringComparison.Ordinal);
        Assert.Contains("`extremeMemberCodeLines` explicitly configured", lowReport, StringComparison.Ordinal);
        Assert.Contains("`minMemberCodeLines` inherited", lowReport, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(lowDirectory, "production", "changed-files", "code-size-candidates.md")));
        Assert.Equal(lowOptionsJson, await File.ReadAllTextAsync(configPath));

        var highOptionsJson = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"code-size-candidates\":{\"extremeMemberCodeLines\":1,\"testOptions\":{\"extremeMemberCodeLines\":1000}}}}";
        await File.WriteAllTextAsync(configPath, highOptionsJson);
        var high = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(0, high.ExitCode);
        Assert.Empty(high.Error);
        var highRunId = GetRunId(high.Output);
        var highDirectory = Path.Combine(projectRoot, "reports", highRunId);
        var highReport = await File.ReadAllTextAsync(Path.Combine(highDirectory, "production", "all-findings", "code-size-candidates.md"));
        Assert.Contains("Total findings: 1", highReport, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Class1.cs (1 findings)", highReport, StringComparison.Ordinal);
        Assert.DoesNotContain("#### File: Tests/Class1.cs", highReport, StringComparison.Ordinal);
        Assert.Contains("Effective options (production projects)", highReport, StringComparison.Ordinal);
        Assert.Contains("Effective options (test projects)", highReport, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(highDirectory, "production", "changed-files", "code-size-candidates.md")));
        Assert.False(File.Exists(Path.Combine(highDirectory, "tests", "all-findings", "code-size-candidates.md")));
        Assert.Equal(highOptionsJson, await File.ReadAllTextAsync(configPath));
        using var baselineAfter = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", "baseline.json")));
        Assert.Equal(baselineFiles, baselineAfter.RootElement.GetProperty("files").GetRawText());
    }

    [Fact]
    public async Task ReviewCommand_ReportsAllSizeKindsReasonsAndPartialEvidenceWhilePreservingControlFlow()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-code-size-");
        var projectRoot = tempDirectory.GetPath("review-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var firstPartPath = Path.Combine(projectDirectory, "Shared.Part1.cs");
        var secondPartPath = Path.Combine(projectDirectory, "Shared.Part2.cs");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(firstPartPath,
            "namespace Sample;\n"
            + "public partial class Shared\n"
            + "{\n"
            + "    public int LongOperation(int value)\n"
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
            + "}\n");
        await File.WriteAllTextAsync(secondPartPath,
            "namespace Sample;\n"
            + "public partial class Shared\n"
            + "{\n"
            + "    public int Other(int value) => value;\n"
            + "}\n");
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        await WriteConfigAsync(projectRoot, "\"code-size-candidates\":{\"percentile\":50,\"minMemberCodeLines\":1,\"extremeMemberCodeLines\":1,\"minTypeCodeLines\":1,\"extremeTypeCodeLines\":1,\"extremeFileLines\":1,\"extremeFileUtf8Bytes\":1,\"testOptions\":{\"percentile\":50,\"minMemberCodeLines\":1,\"extremeMemberCodeLines\":1,\"minTypeCodeLines\":1,\"extremeTypeCodeLines\":1,\"extremeFileLines\":1,\"extremeFileUtf8Bytes\":1}},\"method-control-flow-outliers\":{\"testOptions\":{\"percentile\":90}}");
        await using var services = BuildServices();

        var initial = await InvokeAsync(["review", projectRoot], services);

        Assert.Equal(0, initial.ExitCode);
        Assert.Empty(initial.Error);
        var firstRunId = GetRunId(initial.Output);
        var reportDirectory = Path.Combine(projectRoot, "reports", firstRunId, "production", "all-findings");
        var sizeReportPath = Path.Combine(reportDirectory, "code-size-candidates.md");
        var flowReportPath = Path.Combine(reportDirectory, "method-control-flow-outliers.md");
        Assert.True(File.Exists(sizeReportPath));
        Assert.True(File.Exists(flowReportPath));
        var sizeReport = await File.ReadAllTextAsync(sizeReportPath);
        Assert.Contains("same for production and test projects", sizeReport, StringComparison.Ordinal);
        Assert.Contains("`extremeMemberCodeLines` explicitly configured", sizeReport, StringComparison.Ordinal);
        Assert.Contains("LongOperation", sizeReport, StringComparison.Ordinal);
        Assert.Contains("Member: ", sizeReport, StringComparison.Ordinal);
        Assert.Contains("relative length\\-and\\-control\\-flow criterion", sizeReport, StringComparison.Ordinal);
        Assert.Contains("extreme member\\-size threshold", sizeReport, StringComparison.Ordinal);
        Assert.Contains("Related: [method-control-flow-outliers (production/all-findings)](../../production/all-findings/method-control-flow-outliers.md)", sizeReport, StringComparison.Ordinal);
        Assert.Equal(1, sizeReport.Split("Is this executable body cohesive, and are its paths and tests easy to review?", StringSplitOptions.None).Length - 1);
        Assert.Contains("Class: ", sizeReport, StringComparison.Ordinal);
        Assert.Contains("relative type\\-size criterion", sizeReport, StringComparison.Ordinal);
        Assert.Contains("extreme type\\-size threshold", sizeReport, StringComparison.Ordinal);
        Assert.Equal(1, sizeReport.Split("Do the members of this class serve one cohesive responsibility?", StringSplitOptions.None).Length - 1);
        Assert.Contains("File: ", sizeReport, StringComparison.Ordinal);
        Assert.Contains("line\\-count threshold", sizeReport, StringComparison.Ordinal);
        Assert.Contains("UTF\\-8 byte\\-count threshold", sizeReport, StringComparison.Ordinal);
        Assert.Contains("decisionCount` counts each `if`, conditional expression, loop, and `catch` once", sizeReport, StringComparison.Ordinal);
        Assert.Contains("`&&`, `||`, and `??` do not add decisions", sizeReport, StringComparison.Ordinal);
        Assert.Contains("Member code lines are distinct physical source lines with a non-missing C# token start", sizeReport, StringComparison.Ordinal);
        Assert.Contains("A multiline literal counts its token-start line; continuation lines count only if another token starts there.", sizeReport, StringComparison.Ordinal);
        Assert.Contains("Type code lines sum the same token-start line counts across each non-generated part of an explicit class or record class symbol, excluding nested types and delegates.", sizeReport, StringComparison.Ordinal);
        Assert.Contains("memberCodeLines >= max(minMemberCodeLines, project nearest-rank memberCodeLines value at percentile)", sizeReport, StringComparison.Ordinal);
        Assert.Contains("((decisionCount >= 8 AND decisionConstructCount >= 2) OR maxDecisionNesting >= 4)", sizeReport, StringComparison.Ordinal);
        Assert.Contains("typeCodeLines >= max(minTypeCodeLines, project nearest-rank typeCodeLines value at percentile)", sizeReport, StringComparison.Ordinal);
        Assert.Contains("fileLines >= extremeFileLines` or `fileUtf8Bytes >= extremeFileUtf8Bytes", sizeReport, StringComparison.Ordinal);
        Assert.Equal(1, sizeReport.Split("Can relevant code in this file be located and edited with focused context?", StringSplitOptions.None).Length - 1);
        var flowReport = await File.ReadAllTextAsync(flowReportPath);
        Assert.Contains("same for production and test projects", flowReport, StringComparison.Ordinal);
        Assert.Contains("`percentile` explicitly configured", flowReport, StringComparison.Ordinal);
        Assert.Contains("8 decisions across 8 constructs (cutoff 8)", flowReport, StringComparison.Ordinal);
        Assert.Contains("decision-count and maximum-nesting populations have separate nearest-rank values at the effective `percentile`", flowReport, StringComparison.Ordinal);
        Assert.Contains("Inclusive cutoffs are `max(8, decision percentile)` and `max(4, nesting percentile)`", flowReport, StringComparison.Ordinal);
        Assert.Contains("decisionCount >= decision cutoff AND decisionConstructCount >= 2", flowReport, StringComparison.Ordinal);
        Assert.Contains("or `maxDecisionNesting >= nesting cutoff`", flowReport, StringComparison.Ordinal);
        Assert.Contains("LongOperation", flowReport, StringComparison.Ordinal);
        var repeated = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(0, repeated.ExitCode);
        var repeatedRunId = GetRunId(repeated.Output);
        Assert.Equal(sizeReport, await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", repeatedRunId, "production", "all-findings", "code-size-candidates.md")));
        Assert.Equal(flowReport, await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", repeatedRunId, "production", "all-findings", "method-control-flow-outliers.md")));

        var baseline = await InvokeAsync(["baseline", projectRoot], services);
        Assert.Equal(0, baseline.ExitCode);
        Assert.Empty(baseline.Error);
        await File.AppendAllTextAsync(secondPartPath, "// changed non-representative partial declaration\n");
        var changed = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(0, changed.ExitCode);
        Assert.Empty(changed.Error);
        var changedRunId = GetRunId(changed.Output);
        var changedSizeReport = Path.Combine(projectRoot, "reports", changedRunId, "production", "changed-files", "code-size-candidates.md");
        Assert.True(File.Exists(changedSizeReport));
        Assert.Contains("Shared", await File.ReadAllTextAsync(changedSizeReport), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewCommand_UsesGeneratedDefaultsAndRejectsInvalidSizeOptions()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-code-size-config-");
        var projectRoot = await CreateProjectAsync(tempDirectory.DirectoryPath);
        await using var services = BuildServices();

        var generated = await InvokeAsync(["review", projectRoot], services);

        Assert.Equal(0, generated.ExitCode);
        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "ainetreview.json"))))
        {
            var codeSize = document.RootElement.GetProperty("analyses").GetProperty("code-size-candidates");
            Assert.True(codeSize.GetProperty("enabled").GetBoolean());
            Assert.Equal(90, codeSize.GetProperty("percentile").GetInt32());
            Assert.Equal(80, codeSize.GetProperty("minMemberCodeLines").GetInt32());
            Assert.Equal(300, codeSize.GetProperty("extremeMemberCodeLines").GetInt32());
            Assert.Equal(300, codeSize.GetProperty("minTypeCodeLines").GetInt32());
            Assert.Equal(800, codeSize.GetProperty("extremeTypeCodeLines").GetInt32());
            Assert.Equal(1000, codeSize.GetProperty("extremeFileLines").GetInt32());
            Assert.Equal(131072, codeSize.GetProperty("extremeFileUtf8Bytes").GetInt32());
            Assert.True(document.RootElement.GetProperty("analyses").GetProperty("structural-duplication-candidates").GetProperty("enabled").GetBoolean());
            Assert.Equal(8, document.RootElement.GetProperty("analyses").EnumerateObject().Count());
        }

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "ainetreview.json"),
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"code-size-candidates\":{\"percentile\":100}}}");
        var invalid = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(2, invalid.ExitCode);
        using var error = JsonDocument.Parse(invalid.Error);
        Assert.Equal("INVALID_INPUT", error.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("\"code-size-candidates\":{\"enabled\":false}")]
    [InlineData("\"code-size-candidates\":{\"percentile\":99,\"minMemberCodeLines\":2147483647,\"extremeMemberCodeLines\":2147483647,\"minTypeCodeLines\":2147483647,\"extremeTypeCodeLines\":2147483647,\"extremeFileLines\":2147483647,\"extremeFileUtf8Bytes\":2147483647}")]
    public async Task ReviewCommand_WhenSizeAnalysisIsDisabledOrHasNoCandidatesPublishesNoSizeReport(string sizeConfiguration)
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-code-size-empty-");
        var projectRoot = await CreateProjectAsync(tempDirectory.DirectoryPath);
        await WriteConfigAsync(projectRoot, sizeConfiguration);
        await using var services = BuildServices();

        var result = await InvokeAsync(["review", projectRoot], services);

        Assert.Equal(0, result.ExitCode);
        var runId = GetRunId(result.Output);
        foreach (var area in new[] { "production", "tests", "mixed" })
        foreach (var view in new[] { "all-findings", "changed-files" })
        {
            var viewDirectory = Path.Combine(projectRoot, "reports", runId, area, view);
            Assert.False(File.Exists(Path.Combine(viewDirectory, "code-size-candidates.md")));
            Assert.DoesNotContain(Directory.GetFiles(viewDirectory), static path => Path.GetFileName(path) == "code-size-candidates.md");
        }
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

    private static Task WriteConfigAsync(string projectRoot, string analyses) => File.WriteAllTextAsync(
        Path.Combine(projectRoot, "ainetreview.json"),
        "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{" + analyses + "}}");

    private static async Task<(int ExitCode, string Output, string Error)> InvokeAsync(string[] args, ServiceProvider services)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await new ReviewCommand().InvokeAsync(args, services, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string GetRunId(string output)
    {
        using var response = JsonDocument.Parse(output);
        return response.RootElement.GetProperty("runId").GetString()!;
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        return services.BuildServiceProvider();
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
