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
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using AiNetReview.IntegrationTests.FixtureAnalyses;
using Microsoft.Extensions.DependencyInjection;

public sealed class HostAdapterIntegrationTests
{
    [Fact]
    public async Task ReviewCommand_ProductionIndirectionAnalysisPublishesOrderedPathsAndNoPartialRuns()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-indirection-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var projectDirectory = Path.Combine(projectRoot, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var apiPath = Path.Combine(projectDirectory, "ZApi.cs");
        var servicePath = Path.Combine(projectDirectory, "BService.cs");
        var repositoryPath = Path.Combine(projectDirectory, "ARepository.cs");
        const string projectContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        const string apiSource = "public static class ZApi { public static int Run(int value) { return BService.Run(value); } }";
        const string forwardingServiceSource = "public static class BService { public static int Run(int value) { return ARepository.Run(value); } }";
        const string endpointSource = "public static class ARepository { public static int Run(int value) { return value; } }";
        await File.WriteAllTextAsync(projectFile, projectContent);
        await File.WriteAllTextAsync(apiPath, apiSource);
        await File.WriteAllTextAsync(servicePath, forwardingServiceSource);
        await File.WriteAllTextAsync(repositoryPath, endpointSource);
        await RestoreProjectAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"indirection-drift-candidates\":{\"enabled\":true}}}");

        var first = await RunProductionIndirectionAsync(configPath);
        Assert.Equal(1, first.Detected);
        var runDirectory = Path.Combine(projectRoot, "reports", first.RunId);
        var allReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "all-findings", "indirection-drift-candidates.md"));
        var changedReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "changed-files", "indirection-drift-candidates.md"));
        AssertViewPolicies(allReport, changedReport);
        Assert.Equal(NormalizeForChangedView(allReport), changedReport);
        Assert.Contains("Forwarding path: 2 forwarding edges across 3 types and 3 files", allReport, StringComparison.Ordinal);
        Assert.True(allReport.IndexOf("`Sample/ZApi.cs`", StringComparison.Ordinal)
            < allReport.IndexOf("`Sample/BService.cs`", StringComparison.Ordinal));
        Assert.True(allReport.IndexOf("`Sample/BService.cs`", StringComparison.Ordinal)
            < allReport.IndexOf("`Sample/ARepository.cs`", StringComparison.Ordinal));
        Assert.Contains("What responsibility does each forwarding layer add", allReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(servicePath, "public static class BService { public static int Run(int value) { return value; } }");
        var empty = await RunProductionIndirectionAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "all-findings", "indirection-drift-candidates.md")));
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "changed-files", "indirection-drift-candidates.md")));

        var publishedRuns = Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length;
        await File.WriteAllTextAsync(repositoryPath, "public static class ARepository { public static int Run(int value) { return value; ");
        var failed = await InvokeProductionIndirectionAsync(configPath);
        Assert.Equal(3, failed.ExitCode);
        using (var failureResponse = JsonDocument.Parse(failed.Error))
        {
            Assert.Equal("ANALYSIS_FAILED", failureResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);

        await File.WriteAllTextAsync(repositoryPath, endpointSource);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = await InvokeProductionIndirectionAsync(configPath, cancellation.Token);
        Assert.Equal(130, cancelled.ExitCode);
        using (var cancelledResponse = JsonDocument.Parse(cancelled.Error))
        {
            Assert.Equal("CANCELLED", cancelledResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);

        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        await using var provider = services.BuildServiceProvider();
        Assert.IsType<IndirectionDriftCandidatesAnalysis>(provider.GetRequiredService<ReviewAnalysisRegistry>().GetRequired("indirection-drift-candidates"));
    }

    [Fact]
    public async Task ReviewCommand_ProductionDuplicateCodeAnalysisPublishesCurrentCrossProjectClusters()
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
        Assert.Contains("Total findings: 1", exactReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", exactReport, StringComparison.Ordinal);
        Assert.Contains("### Project: ProductA/ProductA.csproj (1 files, 1 findings)", exactReport, StringComparison.Ordinal);
        Assert.Contains("#### File: ProductA/First.cs (1 findings)", exactReport, StringComparison.Ordinal);
        Assert.Matches("[0-9]+(?:\\.[0-9]+)?% similarity \\(minimum [0-9]+(?:\\.[0-9]+)?%\\)", exactReport);
        Assert.Contains("`ProductA/First.cs`: ", exactReport, StringComparison.Ordinal);
        Assert.Contains("`ProductB/Second.cs`: ", exactReport, StringComparison.Ordinal);
        Assert.Contains("## Findings", exactReport, StringComparison.Ordinal);
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
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "all-findings", "duplicate-code-candidates.md")));
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
    public async Task ReviewCommand_ProductionStructuralDuplicationCoexistsAndHonorsConfigurationAndBaselineSelection()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-structural-duplicate-");
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
        var body = BuildDuplicateBody();
        await File.WriteAllTextAsync(firstSource, WrapDuplicateMethod("FirstContainer", "RunFirst", body));
        await File.WriteAllTextAsync(secondSource, WrapDuplicateMethod("SecondContainer", "RunSecond", body));
        await RestoreProjectAsync(firstProjectFile, firstProject);
        await RestoreProjectAsync(secondProjectFile, secondProject);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"ProductA/ProductA.csproj\" /><Project Path=\"ProductB/ProductB.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: false);
        var duplicateOnly = await RunStructuralReviewAsync(projectRoot);
        var duplicateOnlyReport = await ReadDuplicateCodeReportAsync(projectRoot, duplicateOnly.RunId);
        Assert.Equal(1, duplicateOnly.Detected);
        Assert.False(File.Exists(Path.Combine(projectRoot, "reports", duplicateOnly.RunId, "all-findings", "structural-duplication-candidates.md")));

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: true);
        var together = await RunStructuralReviewAsync(projectRoot);
        Assert.Equal(2, together.Detected);
        Assert.Equal(WithoutRelatedFindingLines(duplicateOnlyReport),
            WithoutRelatedFindingLines(await ReadDuplicateCodeReportAsync(projectRoot, together.RunId)));
        var structuralReport = await ReadStructuralDuplicateReportAsync(projectRoot, together.RunId, "all-findings");
        Assert.Contains("Structural duplicate: 2 occurrences in 2 executable members", structuralReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", structuralReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", structuralReport, StringComparison.Ordinal);
        Assert.Contains("### Project: ProductA/ProductA.csproj (1 files, 1 findings)", structuralReport, StringComparison.Ordinal);
        Assert.Contains("#### File: ProductA/First.cs (1 findings)", structuralReport, StringComparison.Ordinal);
        Assert.Contains("`ProductA/First.cs`", structuralReport, StringComparison.Ordinal);
        Assert.Contains("`ProductB/Second.cs`", structuralReport, StringComparison.Ordinal);
        AssertMarkdownLinksResolve(Path.Combine(projectRoot, "reports", together.RunId));

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: true, structuralEnabled: false);
        var disabled = await RunStructuralReviewAsync(projectRoot);
        Assert.Equal(1, disabled.Detected);
        Assert.False(File.Exists(Path.Combine(projectRoot, "reports", disabled.RunId, "all-findings", "structural-duplication-candidates.md")));

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: true, invalidStructuralOptions: true);
        var publishedRunsBeforeInvalidInput = Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length;
        var invalid = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(2, invalid.ExitCode);
        using (var invalidResponse = JsonDocument.Parse(invalid.Error))
        {
            Assert.Equal("INVALID_INPUT", invalidResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRunsBeforeInvalidInput, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: true);
        var baseline = await InvokeProductionCommandAsync(["baseline", projectRoot]);
        Assert.Equal(0, baseline.ExitCode);
        Assert.Empty(baseline.Error);
        await File.AppendAllTextAsync(secondSource, "// a changed source snapshot\n");
        var changed = await RunStructuralReviewAsync(projectRoot);
        Assert.Equal(2, changed.Detected);
        var changedStructuralReport = await ReadStructuralDuplicateReportAsync(projectRoot, changed.RunId, "changed-files");
        Assert.Contains("Total findings: 1", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("### Project: ProductA/ProductA.csproj (1 files, 1 findings)", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("#### File: ProductA/First.cs (1 findings)", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("`ProductA/First.cs`", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("`ProductB/Second.cs`", changedStructuralReport, StringComparison.Ordinal);

    }

    [Fact]
    public async Task ReviewCommand_ProductionDeadCodeAnalysisPublishesRepeatedAndEmptyAudits()
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
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{}}}");

        var first = await RunProductionDeadCodeAsync(configPath);
        var firstReport = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", first.RunId, "all-findings", "dead-code-candidates.md"));
        Assert.Equal(1, first.Detected);
        Assert.Contains("Type without known use", firstReport, StringComparison.Ordinal);
        Assert.Contains("## Findings", firstReport, StringComparison.Ordinal);
        Assert.Contains("reflection", firstReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external_library", firstReport, StringComparison.Ordinal);

        var second = await RunProductionDeadCodeAsync(configPath);
        var secondReport = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", second.RunId, "all-findings", "dead-code-candidates.md"));
        Assert.Equal(1, second.Detected);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Contains("Type without known use", secondReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class PublicApi { public void Entry() { } }");
        var empty = await RunProductionDeadCodeAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        var emptyIndex = await File.ReadAllTextAsync(Path.Combine(emptyRunDirectory, "index.md"));
        Assert.Contains("No findings were found.", emptyIndex, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "all-findings", "dead-code-candidates.md")));

        var publishedRunsBeforeCancellation = Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var cancelledOutput = new StringWriter();
        using var cancelledError = new StringWriter();
        await using var cancelledProvider = BuildProductionServices();
        var cancelledExitCode = await new ReviewCommand().InvokeAsync(
            ["review", Path.GetDirectoryName(configPath)!],
            cancelledProvider,
            cancelledOutput,
            cancelledError,
            cancellation.Token);
        Assert.Equal(130, cancelledExitCode);
        Assert.Empty(cancelledOutput.ToString());
        Assert.Equal(publishedRunsBeforeCancellation, Directory.GetDirectories(Path.Combine(projectRoot, "reports")).Length);
    }

    [Fact]
    public async Task ReviewCommand_MissingTestEvidenceUsesSnapshotWideChangedFilesAndPublishesOnlyCompleteRuns()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-missing-test-evidence-");
        var projectRoot = tempDirectory.GetPath("adapter-project");
        var productionDirectory = Path.Combine(projectRoot, "Sample.Core");
        var testsDirectory = Path.Combine(projectRoot, "Sample.Tests");
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(testsDirectory);
        var productionProject = Path.Combine(productionDirectory, "Sample.Core.csproj");
        var testProject = Path.Combine(testsDirectory, "Sample.Tests.csproj");
        var apiPath = Path.Combine(productionDirectory, "Api.cs");
        var workerPath = Path.Combine(productionDirectory, "Worker.cs");
        var extraPath = Path.Combine(productionDirectory, "Extra.cs");
        const string projectProperties = "<PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>";
        await File.WriteAllTextAsync(productionProject, $"<Project Sdk=\"Microsoft.NET.Sdk\">{projectProperties}</Project>");
        await File.WriteAllTextAsync(testProject,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">{projectProperties}<ItemGroup><ProjectReference Include=\"..\\Sample.Core\\Sample.Core.csproj\" /><PackageReference Include=\"xunit.v3.core\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(apiPath, "namespace Sample; public static class Api { public static int Run(int value) => Worker.Run(value); }");
        await File.WriteAllTextAsync(workerPath,
            "namespace Sample; public static class Worker { public static int Run(int value) => value switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => 5 }; public static int Uncovered(int value) => value switch { 0 => 0, 1 => 1, 2 => 2, _ => 3 }; }");
        await File.WriteAllTextAsync(Path.Combine(testsDirectory, "ApiTests.cs"),
            "using Xunit; using Sample; public sealed class ApiTests { [Fact] public void CallsApi() => _ = Api.Run(1); }");
        await RestoreProjectAsync(testProject, testsDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"Sample.Core/Sample.Core.csproj\" /><Project Path=\"Sample.Tests/Sample.Tests.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        const string validConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"missing-test-evidence-candidates\":{}}}";
        await File.WriteAllTextAsync(configPath, validConfig);

        var first = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, first.ExitCode);
        using var firstResponse = JsonDocument.Parse(first.Output);
        var firstRunId = firstResponse.RootElement.GetProperty("runId").GetString()!;
        Assert.Equal(2, firstResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var firstRunDirectory = Path.Combine(projectRoot, "reports", firstRunId);
        var allReport = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "all-findings", "missing-test-evidence-candidates.md"));
        var changedReport = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "changed-files", "missing-test-evidence-candidates.md"));
        var index = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "index.md"));
        Assert.Contains("no static test path", allReport, StringComparison.Ordinal);
        Assert.Contains("indirect test path only", allReport, StringComparison.Ordinal);
        Assert.Contains("Shortest resolved test path:", allReport, StringComparison.Ordinal);
        Assert.Contains("attribution uncertain` marker means the static test association may be incomplete", allReport, StringComparison.Ordinal);
        Assert.Contains("It does not assess test assertion quality.", allReport, StringComparison.Ordinal);
        Assert.Contains("Changed-files selection is snapshot-wide because changes to test roots or the call graph can alter associations in unchanged production files. Without a baseline, every current source file is treated as new. With a baseline, any added, changed, or deleted C# path selects all current findings; an unchanged C# snapshot selects none. The source status in each file heading describes only that representative file relative to the baseline; an unchanged status does not mean unaffected.", changedReport, StringComparison.Ordinal);
        Assert.Contains("source new or changed", changedReport, StringComparison.Ordinal);
        AssertViewPolicies(allReport, changedReport);
        Assert.Equal(allReport, NormalizeNoBaselineChangedView(changedReport));
        Assert.Contains("any C# path was added, changed, or deleted", index, StringComparison.Ordinal);

        var baseline = await InvokeProductionCommandAsync(["baseline", projectRoot]);
        Assert.Equal(0, baseline.ExitCode);
        var unchanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, unchanged.ExitCode);
        using var unchangedResponse = JsonDocument.Parse(unchanged.Output);
        var unchangedRunId = unchangedResponse.RootElement.GetProperty("runId").GetString()!;
        Assert.NotEqual(firstRunId, unchangedRunId);
        var unchangedDirectory = Path.Combine(projectRoot, "reports", unchangedRunId);
        Assert.Equal(2, unchangedResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        Assert.False(File.Exists(Path.Combine(unchangedDirectory, "changed-files", "missing-test-evidence-candidates.md")));
        Assert.True(File.Exists(Path.Combine(unchangedDirectory, "all-findings", "missing-test-evidence-candidates.md")));

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "notes.md"), "non-C# change");
        var nonCSharpOnly = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, nonCSharpOnly.ExitCode);
        using var nonCSharpResponse = JsonDocument.Parse(nonCSharpOnly.Output);
        var nonCSharpDirectory = Path.Combine(projectRoot, "reports", nonCSharpResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(nonCSharpDirectory, "changed-files", "missing-test-evidence-candidates.md")));

        await File.AppendAllTextAsync(Path.Combine(testsDirectory, "ApiTests.cs"), " // changed test-only C# path");
        var testOnlyChanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, testOnlyChanged.ExitCode);
        using var testOnlyResponse = JsonDocument.Parse(testOnlyChanged.Output);
        var testOnlyDirectory = Path.Combine(projectRoot, "reports", testOnlyResponse.RootElement.GetProperty("runId").GetString()!);
        var testOnlyReportPath = Path.Combine(testOnlyDirectory, "changed-files", "missing-test-evidence-candidates.md");
        Assert.True(File.Exists(testOnlyReportPath));
        Assert.Contains("source unchanged; included snapshot-wide", await File.ReadAllTextAsync(testOnlyReportPath), StringComparison.Ordinal);

        await InvokeProductionCommandAsync(["baseline", projectRoot]);
        await File.WriteAllTextAsync(extraPath, "namespace Sample; public sealed class Extra { }");
        var added = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, added.ExitCode);
        using var addedResponse = JsonDocument.Parse(added.Output);
        var addedDirectory = Path.Combine(projectRoot, "reports", addedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(addedDirectory, "changed-files", "missing-test-evidence-candidates.md")));
        Assert.Contains("source unchanged; included snapshot-wide",
            await File.ReadAllTextAsync(Path.Combine(addedDirectory, "changed-files", "missing-test-evidence-candidates.md")), StringComparison.Ordinal);

        await InvokeProductionCommandAsync(["baseline", projectRoot]);
        await File.AppendAllTextAsync(apiPath, " // changed C# snapshot path");
        var changed = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, changed.ExitCode);
        using var changedResponse = JsonDocument.Parse(changed.Output);
        var changedDirectory = Path.Combine(projectRoot, "reports", changedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(changedDirectory, "changed-files", "missing-test-evidence-candidates.md")));

        await InvokeProductionCommandAsync(["baseline", projectRoot]);
        File.Delete(extraPath);
        var deleted = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, deleted.ExitCode);
        using var deletedResponse = JsonDocument.Parse(deleted.Output);
        var deletedDirectory = Path.Combine(projectRoot, "reports", deletedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(deletedDirectory, "changed-files", "missing-test-evidence-candidates.md")));
        Assert.Contains("source unchanged; included snapshot-wide",
            await File.ReadAllTextAsync(Path.Combine(deletedDirectory, "changed-files", "missing-test-evidence-candidates.md")), StringComparison.Ordinal);

        var publishedRuns = Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length;
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"missing-test-evidence-candidates\":{},\"zzzz-failing-analysis\":{}}}");
        var failed = await InvokeProductionCommandAsync(["review", projectRoot], serviceProvider: BuildProductionServices(new FailingAnalysis()));
        Assert.Equal(3, failed.ExitCode);
        using (var failureResponse = JsonDocument.Parse(failed.Error))
        {
            Assert.Equal("ANALYSIS_FAILED", failureResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length);

        using var cancellation = new CancellationTokenSource();
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"missing-test-evidence-candidates\":{},\"zzzz-cancelling-analysis\":{}}}");
        var cancelled = await InvokeProductionCommandAsync(
            ["review", projectRoot], cancellation.Token, BuildProductionServices(new CancellingAnalysis(cancellation)));
        Assert.Equal(130, cancelled.ExitCode);
        using (var cancelledResponse = JsonDocument.Parse(cancelled.Error))
        {
            Assert.Equal("CANCELLED", cancelledResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length);

        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"missing-test-evidence-candidates\":{\"minDecisionCount\":2147483647,\"minDecisionNesting\":2147483647,\"minIndirectDecisionCount\":2147483647,\"minIndirectDecisionNesting\":2147483647}}}");
        var empty = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, empty.ExitCode);
        using var emptyResponse = JsonDocument.Parse(empty.Output);
        var emptyDirectory = Path.Combine(projectRoot, "reports", emptyResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.Equal(0, emptyResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        Assert.False(File.Exists(Path.Combine(emptyDirectory, "changed-files", "missing-test-evidence-candidates.md")));
        Assert.False(File.Exists(Path.Combine(emptyDirectory, "all-findings", "missing-test-evidence-candidates.md")));
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
        var firstReport = await ReadAnalysisReportAsync(projectRoot, first.RunId);
        Assert.Contains("FixtureCaseA", firstReport, StringComparison.Ordinal);
        Assert.Contains("FixtureCaseB", firstReport, StringComparison.Ordinal);
        Assert.Contains("scenario 'base'", firstReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(sourcePath,
            "namespace Sample; public sealed class Sample { public void FixtureCaseC() { } }");
        var second = await RunFixtureAsync(projectRoot, configPath, "alternate");
        Assert.Equal(1, second.Detected);
        var secondReport = await ReadAnalysisReportAsync(projectRoot, second.RunId);
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
        Assert.Equal(new[] { "index.md" }, Directory.GetFiles(Path.Combine(thirdRunDirectory, "all-findings"), "*.md").Select(Path.GetFileName));
        Assert.Equal(new[] { "index.md" }, Directory.GetFiles(Path.Combine(thirdRunDirectory, "changed-files"), "*.md").Select(Path.GetFileName));

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
            ["review", "C:\\project", "--extra"],
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
            ["review", "C:\\project"],
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
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"fixture-finding\":{}}}");

        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddSingleton<IReviewAnalysis, FixtureFindingAnalysis>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", Path.GetDirectoryName(configPath)!],
            provider,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var runId = response.RootElement.GetProperty("runId").GetString();
        var report = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId!, "all-findings", "fixture-finding.md"));
        Assert.Contains("Fixture scenario 'base' requires review of FixtureCaseA.", report, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "code-size-candidates", "dead-code-candidates", "duplicate-code-candidates", "fixture-finding", "indirection-drift-candidates", "method-control-flow-outliers", "missing-test-evidence-candidates", "non-ascii-identifiers", "structural-duplication-candidates" },
            provider.GetRequiredService<ReviewAnalysisRegistry>().Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));
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
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{}}}");
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var locked = new FileStream(markupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", Path.GetDirectoryName(configPath)!],
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
            analyses = new Dictionary<string, object> { ["fixture-finding"] = new { scenario } },
        });
        await File.WriteAllTextAsync(configPath, config);
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddSingleton<IReviewAnalysis, FixtureFindingAnalysis>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", Path.GetDirectoryName(configPath)!], provider, output, error);
        Assert.True(exitCode == 0, $"Fixture scan '{scenario}' failed: {error}");
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static Task<string> ReadAnalysisReportAsync(string projectRoot, string runId) =>
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "all-findings", "fixture-finding.md"));

    private static async Task<(string RunId, int Detected)> RunProductionDeadCodeAsync(string configPath)
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new ReviewCommand().InvokeAsync(["review", Path.GetDirectoryName(configPath)!], provider, output, error);
        Assert.True(exitCode == 0, $"Production dead-code audit failed: {error}");
        Assert.Empty(error.ToString());
        using var response = JsonDocument.Parse(output.ToString());
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static async Task<(int ExitCode, string Output, string Error)> InvokeProductionCommandAsync(
        string[] arguments,
        CancellationToken cancellationToken = default,
        ServiceProvider? serviceProvider = null)
    {
        await using var provider = serviceProvider ?? BuildProductionServices();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await new ReviewCommand().InvokeAsync(arguments, provider, output, error, cancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static async Task<(string RunId, int Detected)> RunProductionIndirectionAsync(string configPath)
    {
        var result = await InvokeProductionIndirectionAsync(configPath);
        Assert.True(result.ExitCode == 0, $"Production indirection audit failed: {result.Error}");
        Assert.Empty(result.Error);
        using var response = JsonDocument.Parse(result.Output);
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static async Task<(int ExitCode, string Output, string Error)> InvokeProductionIndirectionAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        await using var provider = BuildProductionServices();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await new ReviewCommand().InvokeAsync(
            ["review", Path.GetDirectoryName(configPath)!], provider, output, error, cancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static async Task WriteDuplicateConfigAsync(string configPath, string minimumSimilarity)
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = "Sample.slnx",
            outputDirectory = "reports",
            analyses = new Dictionary<string, object>
            {
                ["duplicate-code-candidates"] = new { minTokens = 30, minimumSimilarity },
            },
        });
        await File.WriteAllTextAsync(configPath, json);
    }

    private static async Task WriteStructuralDuplicateConfigAsync(
        string configPath,
        bool includeStructural,
        bool structuralEnabled = true,
        bool invalidStructuralOptions = false)
    {
        var analyses = new Dictionary<string, object>
        {
            ["duplicate-code-candidates"] = new { minTokens = 30, minimumSimilarity = "exact" },
        };
        if (includeStructural)
        {
            if (invalidStructuralOptions)
            {
                analyses["structural-duplication-candidates"] = new { enabled = structuralEnabled, minTokens = 60 };
            }
            else
            {
                analyses["structural-duplication-candidates"] = new { enabled = structuralEnabled };
            }
        }

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = "Sample.slnx",
            outputDirectory = "reports",
            analyses,
        });
        await File.WriteAllTextAsync(configPath, json);
    }

    private static async Task<(string RunId, int Detected)> RunStructuralReviewAsync(string projectRoot)
    {
        var result = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.True(result.ExitCode == 0, $"Production structural duplication audit failed: {result.Error}");
        Assert.Empty(result.Error);
        using var response = JsonDocument.Parse(result.Output);
        return (
            response.RootElement.GetProperty("runId").GetString()!,
            response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
    }

    private static Task<string> ReadStructuralDuplicateReportAsync(string projectRoot, string runId, string view) =>
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, view, "structural-duplication-candidates.md"));

    private static string WithoutRelatedFindingLines(string markdown) => string.Join("\n",
        markdown.Split('\n').Where(static line => !line.StartsWith("  - Related: ", StringComparison.Ordinal)));

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
            ["review", Path.GetDirectoryName(configPath)!], provider, output, error, cancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static Task<string> ReadDuplicateCodeReportAsync(string projectRoot, string runId) =>
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "all-findings", "duplicate-code-candidates.md"));

    private static void AssertViewPolicies(string allFindingsReport, string changedFilesReport)
    {
        Assert.Contains(
            "This is the reference-only `all-findings/` view; inspect or report it only when the user explicitly requests a full repository audit.",
            allFindingsReport,
            StringComparison.Ordinal);
        Assert.Contains("Use `changed-files/` as the primary review set;", changedFilesReport, StringComparison.Ordinal);
    }

    private static string NormalizeForChangedView(string report) => report.Replace(
        "Review policy: These potential signals do not require changes. This is the reference-only `all-findings/` view; inspect or report it only when the user explicitly requests a full repository audit. See the [root index's Review guidance](../index.md#review-guidance).",
        "Review policy: These potential signals do not require changes. Use `changed-files/` as the primary review set; see the [root index's Review guidance](../index.md#review-guidance).",
        StringComparison.Ordinal);

    private static string NormalizeNoBaselineChangedView(string report)
    {
        var normalized = report.Replace(
                "Review policy: These potential signals do not require changes. Use `changed-files/` as the primary review set; see the [root index's Review guidance](../index.md#review-guidance).",
                "Review policy: These potential signals do not require changes. This is the reference-only `all-findings/` view; inspect or report it only when the user explicitly requests a full repository audit. See the [root index's Review guidance](../index.md#review-guidance).",
                StringComparison.Ordinal)
            .Replace("; source new or changed)", ")", StringComparison.Ordinal);
        var paragraphStart = normalized.IndexOf("\n\nChanged-files selection is snapshot-wide", StringComparison.Ordinal);
        if (paragraphStart < 0)
        {
            return normalized;
        }

        var nextSection = normalized.IndexOf("\n\n## Summary", paragraphStart, StringComparison.Ordinal);
        return nextSection < 0 ? normalized : normalized.Remove(paragraphStart, nextSection - paragraphStart);
    }

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

    private static ServiceProvider BuildProductionServices(IReviewAnalysis? additionalAnalysis = null)
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        if (additionalAnalysis is not null)
        {
            services.AddSingleton<IReviewAnalysis>(additionalAnalysis);
        }

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private sealed class FailingAnalysis : IReviewAnalysis
    {
        public ReviewAnalysisDescriptor Descriptor { get; } = new(
            "zzzz-failing-analysis", "Failing analysis", 1, "Failure fixture.", "Failure fixture.", ["Does failure prevent publication?"]);

        public Task<ReviewAnalysisResult> ExecuteAsync(ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fixture failure after missing-test analysis.");
    }

    private sealed class CancellingAnalysis(CancellationTokenSource cancellationSource) : IReviewAnalysis
    {
        public ReviewAnalysisDescriptor Descriptor { get; } = new(
            "zzzz-cancelling-analysis", "Cancelling analysis", 1, "Cancellation fixture.", "Cancellation fixture.", ["Does cancellation prevent publication?"]);

        public async Task<ReviewAnalysisResult> ExecuteAsync(ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken)
        {
            await cancellationSource.CancelAsync();
            return ReviewAnalysisResult.Empty;
        }
    }
}
