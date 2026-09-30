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
        await WriteConfigAsync(projectRoot, "\"code-size-candidates\":{\"percentile\":50,\"minMemberCodeLines\":1,\"extremeMemberCodeLines\":1,\"minTypeCodeLines\":1,\"extremeTypeCodeLines\":1,\"extremeFileLines\":1,\"extremeFileUtf8Bytes\":1},\"method-control-flow-outliers\":{}");
        await using var services = BuildServices();

        var initial = await InvokeAsync(["review", projectRoot], services);

        Assert.Equal(0, initial.ExitCode);
        Assert.Empty(initial.Error);
        var firstRunId = GetRunId(initial.Output);
        var reportDirectory = Path.Combine(projectRoot, "reports", firstRunId, "all-findings");
        var sizeReportPath = Path.Combine(reportDirectory, "code-size-candidates.md");
        var flowReportPath = Path.Combine(reportDirectory, "method-control-flow-outliers.md");
        Assert.True(File.Exists(sizeReportPath));
        Assert.True(File.Exists(flowReportPath));
        var sizeReport = await File.ReadAllTextAsync(sizeReportPath);
        Assert.Contains("LongOperation", sizeReport, StringComparison.Ordinal);
        Assert.Contains("Member: ", sizeReport, StringComparison.Ordinal);
        Assert.Contains("relative length\\-and\\-control\\-flow path", sizeReport, StringComparison.Ordinal);
        Assert.Contains("extreme length path", sizeReport, StringComparison.Ordinal);
        Assert.Contains("Related: method-control-flow-outliers", sizeReport, StringComparison.Ordinal);
        Assert.Equal(1, sizeReport.Split("Is this executable body cohesive, and are its paths and tests easy to review?", StringSplitOptions.None).Length - 1);
        Assert.Contains("Class: ", sizeReport, StringComparison.Ordinal);
        Assert.Contains("relative type\\-size path", sizeReport, StringComparison.Ordinal);
        Assert.Contains("extreme type\\-size path", sizeReport, StringComparison.Ordinal);
        Assert.Equal(1, sizeReport.Split("Do the members of this class serve one cohesive responsibility?", StringSplitOptions.None).Length - 1);
        Assert.Contains("File: ", sizeReport, StringComparison.Ordinal);
        Assert.Contains("line\\-count path", sizeReport, StringComparison.Ordinal);
        Assert.Contains("UTF\\-8 byte path", sizeReport, StringComparison.Ordinal);
        Assert.Equal(1, sizeReport.Split("Can relevant code in this file be located and edited with focused context?", StringSplitOptions.None).Length - 1);
        var flowReport = await File.ReadAllTextAsync(flowReportPath);
        Assert.Contains("8 decisions across 8 constructs (cutoff 8)", flowReport, StringComparison.Ordinal);
        Assert.Contains("LongOperation", flowReport, StringComparison.Ordinal);
        var repeated = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(0, repeated.ExitCode);
        var repeatedRunId = GetRunId(repeated.Output);
        Assert.Equal(sizeReport, await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", repeatedRunId, "all-findings", "code-size-candidates.md")));
        Assert.Equal(flowReport, await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", repeatedRunId, "all-findings", "method-control-flow-outliers.md")));

        var baseline = await InvokeAsync(["baseline", projectRoot], services);
        Assert.Equal(0, baseline.ExitCode);
        Assert.Empty(baseline.Error);
        await File.AppendAllTextAsync(secondPartPath, "// changed non-representative partial declaration\n");
        var changed = await InvokeAsync(["review", projectRoot], services);
        Assert.Equal(0, changed.ExitCode);
        Assert.Empty(changed.Error);
        var changedRunId = GetRunId(changed.Output);
        var changedSizeReport = Path.Combine(projectRoot, "reports", changedRunId, "changed-files", "code-size-candidates.md");
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
        foreach (var view in new[] { "all-findings", "changed-files" })
        {
            var viewDirectory = Path.Combine(projectRoot, "reports", runId, view);
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
