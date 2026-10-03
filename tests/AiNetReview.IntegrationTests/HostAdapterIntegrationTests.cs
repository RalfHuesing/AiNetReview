namespace AiNetReview.IntegrationTests;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    public async Task ReviewCommand_ProductionTypeDependencyCyclesPublishStableEvidenceAndSnapshotWideSelections()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-type-cycle-");
        var projectRoot = tempDirectory.GetPath("cycle-project");
        var productionDirectory = Path.Combine(projectRoot, "src", "Sample");
        var testDirectory = Path.Combine(projectRoot, "tests", "Sample.Tests");
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(testDirectory);
        const string projectContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        var productionProject = Path.Combine(productionDirectory, "Sample.csproj");
        var testProject = Path.Combine(testDirectory, "Sample.Tests.csproj");
        var aPath = Path.Combine(productionDirectory, "A.cs");
        var bPath = Path.Combine(productionDirectory, "B.cs");
        var cPath = Path.Combine(productionDirectory, "C.cs");
        var testPath = Path.Combine(testDirectory, "CycleTests.cs");
        const string aSource = "namespace Sample; public class A { public B? Value; }";
        const string bSource = "namespace Sample; public class B { public C? Value; }";
        const string cSource = "namespace Sample; public class C { public A? Value; }";
        await File.WriteAllTextAsync(productionProject, projectContent);
        await File.WriteAllTextAsync(testProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/Sample/Sample.csproj\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(aPath, aSource);
        await File.WriteAllTextAsync(bPath, bSource);
        await File.WriteAllTextAsync(cPath, cSource);
        await File.WriteAllTextAsync(testPath, "using Sample; public class CycleTests { public void Read() { _ = new A(); } }");
        await RestoreProjectAsync(productionProject, productionDirectory);
        await RestoreProjectAsync(testProject, testDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"src/Sample/Sample.csproj\" /><Project Path=\"tests/Sample.Tests/Sample.Tests.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"type-dependency-cycle-candidates\":{}}}");

        var first = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, first.ExitCode);
        using var firstResponse = JsonDocument.Parse(first.Output);
        var firstRunId = firstResponse.RootElement.GetProperty("runId").GetString()!;
        Assert.Equal(1, firstResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var firstRunDirectory = Path.Combine(projectRoot, "reports", firstRunId);
        var allReportPath = Path.Combine(firstRunDirectory, "production", "all-findings", "type-dependency-cycle-candidates.md");
        var allReport = await File.ReadAllTextAsync(allReportPath);
        var firstMap = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "audit-map", "all-findings", "index.md"));
        Assert.Contains("3 production types in 3 distinct declaration files", allReport, StringComparison.Ordinal);
        Assert.Contains("Example:", allReport, StringComparison.Ordinal);
        Assert.Contains("L1", allReport, StringComparison.Ordinal);
        Assert.Contains("src/Sample/B.cs:1", allReport, StringComparison.Ordinal);
        Assert.Contains("src/Sample/C.cs:1", allReport, StringComparison.Ordinal);
        Assert.DoesNotContain("<a id=", allReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Subject occurrences:", firstMap, StringComparison.Ordinal);
        Assert.Contains("src/Sample/A.cs", firstMap, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Sample/B.cs", firstMap, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Sample/C.cs", firstMap, StringComparison.Ordinal);
        Assert.False(Directory.EnumerateFileSystemEntries(firstRunDirectory, "changed-files", SearchOption.AllDirectories).Any());
        var firstId = GetFirstAuditMapFindingId(firstMap);
        Assert.NotEmpty(firstId);

        var repeated = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, repeated.ExitCode);
        using var repeatedResponse = JsonDocument.Parse(repeated.Output);
        var repeatedDirectory = Path.Combine(projectRoot, "reports", repeatedResponse.RootElement.GetProperty("runId").GetString()!);
        var repeatedMap = await File.ReadAllTextAsync(Path.Combine(repeatedDirectory, "audit-map", "all-findings", "index.md"));
        Assert.Equal(firstId, GetFirstAuditMapFindingId(repeatedMap));

        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        var unchanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, unchanged.ExitCode);
        using var unchangedResponse = JsonDocument.Parse(unchanged.Output);
        var unchangedDirectory = Path.Combine(projectRoot, "reports", unchangedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(unchangedDirectory, "production", "changed-files", "type-dependency-cycle-candidates.md")));
        Assert.Contains("Findings: **0**", await File.ReadAllTextAsync(Path.Combine(unchangedDirectory, "audit-map", "changed-files", "index.md")), StringComparison.Ordinal);

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "notes.md"), "non-C# edit");
        var nonCSharp = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, nonCSharp.ExitCode);
        using var nonCSharpResponse = JsonDocument.Parse(nonCSharp.Output);
        var nonCSharpDirectory = Path.Combine(projectRoot, "reports", nonCSharpResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(nonCSharpDirectory, "production", "changed-files", "type-dependency-cycle-candidates.md")));

        await File.AppendAllTextAsync(testPath, " // test-only snapshot change");
        var testOnly = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, testOnly.ExitCode);
        using var testOnlyResponse = JsonDocument.Parse(testOnly.Output);
        var testOnlyDirectory = Path.Combine(projectRoot, "reports", testOnlyResponse.RootElement.GetProperty("runId").GetString()!);
        var testOnlyReport = await File.ReadAllTextAsync(Path.Combine(testOnlyDirectory, "production", "changed-files", "type-dependency-cycle-candidates.md"));
        Assert.Contains("source unchanged; included snapshot-wide", testOnlyReport, StringComparison.Ordinal);

        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        var addedPath = Path.Combine(productionDirectory, "Unrelated.cs");
        await File.WriteAllTextAsync(addedPath, "namespace Sample; public class Unrelated { }");
        var added = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, added.ExitCode);
        using var addedResponse = JsonDocument.Parse(added.Output);
        var addedDirectory = Path.Combine(projectRoot, "reports", addedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(addedDirectory, "production", "changed-files", "type-dependency-cycle-candidates.md")));

        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        File.Delete(addedPath);
        var deleted = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, deleted.ExitCode);
        using var deletedResponse = JsonDocument.Parse(deleted.Output);
        var deletedDirectory = Path.Combine(projectRoot, "reports", deletedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(deletedDirectory, "production", "changed-files", "type-dependency-cycle-candidates.md")));

        var publishedRuns = Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length;
        await File.WriteAllTextAsync(aPath, "namespace Sample; public class A { public Missing? Value; }");
        var failed = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(3, failed.ExitCode);
        using (var failedResponse = JsonDocument.Parse(failed.Error))
        {
            Assert.Equal("ANALYSIS_FAILED", failedResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length);
        await File.WriteAllTextAsync(aPath, aSource);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = await InvokeProductionCommandAsync(["review", projectRoot], cancellation.Token);
        Assert.Equal(130, cancelled.ExitCode);
        using (var cancelledResponse = JsonDocument.Parse(cancelled.Error))
        {
            Assert.Equal("CANCELLED", cancelledResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length);

        await File.WriteAllTextAsync(cPath, "namespace Sample; public class C { }");
        var empty = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, empty.ExitCode);
        using var emptyResponse = JsonDocument.Parse(empty.Output);
        var emptyDirectory = Path.Combine(projectRoot, "reports", emptyResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.Equal(0, emptyResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        Assert.False(File.Exists(Path.Combine(emptyDirectory, "production", "all-findings", "type-dependency-cycle-candidates.md")));
    }

    [Fact]
    public async Task ReviewCommand_ProductionTypeDependencyHubsPublishCompleteNeighborhoodAndSnapshotWideSelections()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-type-hub-");
        var projectRoot = tempDirectory.GetPath("hub-project");
        var productionDirectory = Path.Combine(projectRoot, "src", "Sample");
        var testDirectory = Path.Combine(projectRoot, "tests", "Sample.Tests");
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(testDirectory);
        const string projectContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        var productionProject = Path.Combine(productionDirectory, "Sample.csproj");
        var testProject = Path.Combine(testDirectory, "Sample.Tests.csproj");
        var hubPath = Path.Combine(productionDirectory, "Hub.cs");
        var hubPartialPath = Path.Combine(productionDirectory, "Hub.Partial.cs");
        var testPath = Path.Combine(testDirectory, "HubTests.cs");
        var hubSource = "namespace Sample; public partial class Hub { "
            + string.Join(" ", Enumerable.Range(0, 10).Select(index => $"public Dependency{index}? D{index};")) + " }";
        await File.WriteAllTextAsync(productionProject, projectContent);
        await File.WriteAllTextAsync(testProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/Sample/Sample.csproj\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(hubPath, hubSource);
        await File.WriteAllTextAsync(hubPartialPath, "namespace Sample; public partial class Hub { }");
        for (var index = 0; index < 10; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(productionDirectory, $"Consumer{index}.cs"),
                $"namespace Sample; public class Consumer{index} {{ public Hub? Value; }}");
            await File.WriteAllTextAsync(Path.Combine(productionDirectory, $"Dependency{index}.cs"),
                index == 0
                    ? "namespace Sample; public class Dependency0 { public Consumer0? Next; }"
                    : $"namespace Sample; public class Dependency{index} {{ }}");
        }
        await File.WriteAllTextAsync(testPath, "namespace Sample.Tests; public class HubTests { public Sample.Hub? Value; }");
        await RestoreProjectAsync(productionProject, productionDirectory);
        await RestoreProjectAsync(testProject, testDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"src/Sample/Sample.csproj\" /><Project Path=\"tests/Sample.Tests/Sample.Tests.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        var validConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"type-dependency-cycle-candidates\":{},\"type-dependency-hub-candidates\":{\"minFanIn\":10,\"minFanOut\":10}}}";
        await File.WriteAllTextAsync(configPath, validConfig);

        var first = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, first.ExitCode);
        using var firstResponse = JsonDocument.Parse(first.Output);
        Assert.Equal(2, firstResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var firstRunId = firstResponse.RootElement.GetProperty("runId").GetString()!;
        var firstRunDirectory = Path.Combine(projectRoot, "reports", firstRunId);
        var hubReportPath = Path.Combine(firstRunDirectory, "production", "all-findings", "type-dependency-hub-candidates.md");
        var hubReport = await File.ReadAllTextAsync(hubReportPath);
        var firstMap = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "audit-map", "all-findings", "index.md"));
        Assert.Contains("10 production consumer types (minimum 10", hubReport, StringComparison.Ordinal);
        Assert.Contains("10 production dependency types (minimum 10", hubReport, StringComparison.Ordinal);
        Assert.Contains("1 direct test consumer types are listed separately", hubReport, StringComparison.Ordinal);
        Assert.Contains("L", hubReport, StringComparison.Ordinal);
        Assert.Contains("src/Sample/Hub.Partial.cs:", hubReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Finding origin:", hubReport, StringComparison.Ordinal);
        Assert.Contains("tests/Sample.Tests/HubTests.cs", hubReport, StringComparison.Ordinal);
        foreach (var index in Enumerable.Range(0, 10))
        {
            Assert.Contains($"Consumer{index}", hubReport, StringComparison.Ordinal);
            Assert.Contains($"Dependency{index}", hubReport, StringComparison.Ordinal);
        }
        var relatedCycleId = GetAuditMapFindingId(firstMap, "type-dependency-cycle-candidates.md");
        Assert.NotEmpty(relatedCycleId);
        Assert.Contains(relatedCycleId, hubReport, StringComparison.Ordinal);
        Assert.Contains("type-dependency-cycle-candidates.md", hubReport, StringComparison.Ordinal);
        Assert.Contains("type-dependency-hub-candidates", firstMap, StringComparison.Ordinal);
        var firstId = GetAuditMapFindingId(firstMap, "type-dependency-hub-candidates");
        Assert.NotEmpty(firstId);

        var repeated = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, repeated.ExitCode);
        using var repeatedResponse = JsonDocument.Parse(repeated.Output);
        var repeatedDirectory = Path.Combine(projectRoot, "reports", repeatedResponse.RootElement.GetProperty("runId").GetString()!);
        var repeatedMap = await File.ReadAllTextAsync(Path.Combine(repeatedDirectory, "audit-map", "all-findings", "index.md"));
        Assert.Equal(firstId, GetAuditMapFindingId(repeatedMap, "type-dependency-hub-candidates"));

        var explicitDefaultConfig = await File.ReadAllTextAsync(configPath);
        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        var unchanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, unchanged.ExitCode);
        using var unchangedResponse = JsonDocument.Parse(unchanged.Output);
        var unchangedDirectory = Path.Combine(projectRoot, "reports", unchangedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(unchangedDirectory, "production", "changed-files", "type-dependency-hub-candidates.md")));

        await File.AppendAllTextAsync(testPath, " // test-only change");
        var testOnly = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, testOnly.ExitCode);
        using var testOnlyResponse = JsonDocument.Parse(testOnly.Output);
        var testOnlyDirectory = Path.Combine(projectRoot, "reports", testOnlyResponse.RootElement.GetProperty("runId").GetString()!);
        var testOnlyReport = await File.ReadAllTextAsync(Path.Combine(testOnlyDirectory, "production", "changed-files", "type-dependency-hub-candidates.md"));
        Assert.Contains("source unchanged; included snapshot-wide", testOnlyReport, StringComparison.Ordinal);
        var testOnlyMap = await File.ReadAllTextAsync(Path.Combine(testOnlyDirectory, "audit-map", "changed-files", "index.md"));
        Assert.Contains("Selected changed-file findings.", testOnlyMap, StringComparison.Ordinal);
        Assert.Contains("dependency-hub findings use snapshot-wide selection", testOnlyMap, StringComparison.Ordinal);

        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "notes.md"), "non-C# edit");
        var nonCSharp = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, nonCSharp.ExitCode);
        using var nonCSharpResponse = JsonDocument.Parse(nonCSharp.Output);
        var nonCSharpDirectory = Path.Combine(projectRoot, "reports", nonCSharpResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(nonCSharpDirectory, "production", "changed-files", "type-dependency-hub-candidates.md")));

        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        var addedPath = Path.Combine(productionDirectory, "Unrelated.cs");
        await File.WriteAllTextAsync(addedPath, "namespace Sample; public class Unrelated { }");
        var added = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, added.ExitCode);
        using var addedResponse = JsonDocument.Parse(added.Output);
        var addedDirectory = Path.Combine(projectRoot, "reports", addedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(addedDirectory, "production", "changed-files", "type-dependency-hub-candidates.md")));

        Assert.Equal(0, (await InvokeProductionCommandAsync(["baseline", projectRoot])).ExitCode);
        File.Delete(addedPath);
        var deleted = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, deleted.ExitCode);
        using var deletedResponse = JsonDocument.Parse(deleted.Output);
        var deletedDirectory = Path.Combine(projectRoot, "reports", deletedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(deletedDirectory, "production", "changed-files", "type-dependency-hub-candidates.md")));

        await File.WriteAllTextAsync(configPath, explicitDefaultConfig.Replace("\"minFanIn\":10", "\"minFanIn\":11", StringComparison.Ordinal));
        var configuredThreshold = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, configuredThreshold.ExitCode);
        using var configuredThresholdResponse = JsonDocument.Parse(configuredThreshold.Output);
        var configuredThresholdDirectory = Path.Combine(projectRoot, "reports", configuredThresholdResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(configuredThresholdDirectory, "production", "all-findings", "type-dependency-hub-candidates.md")));

        var hubOnlyConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"type-dependency-hub-candidates\":{}}}";
        await File.WriteAllTextAsync(configPath, hubOnlyConfig);
        var publishedRuns = Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length;
        await File.WriteAllTextAsync(hubPath, "namespace Sample; public partial class Hub { public MissingDependency? Value; }");
        var failed = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(3, failed.ExitCode);
        using (var failedResponse = JsonDocument.Parse(failed.Error))
        {
            Assert.Equal("ANALYSIS_FAILED", failedResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length);

        await File.WriteAllTextAsync(hubPath, hubSource);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = await InvokeProductionCommandAsync(["review", projectRoot], cancellation.Token);
        Assert.Equal(130, cancelled.ExitCode);
        using (var cancelledResponse = JsonDocument.Parse(cancelled.Error))
        {
            Assert.Equal("CANCELLED", cancelledResponse.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(publishedRuns, Directory.GetDirectories(Path.Combine(projectRoot, "reports"), "20*", SearchOption.TopDirectoryOnly).Length);

        await File.WriteAllTextAsync(configPath, "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"type-dependency-hub-candidates\":{\"minFanIn\":11,\"minFanOut\":10}}}");
        var empty = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, empty.ExitCode);
        using var emptyResponse = JsonDocument.Parse(empty.Output);
        var emptyDirectory = Path.Combine(projectRoot, "reports", emptyResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.Equal(0, emptyResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        Assert.False(File.Exists(Path.Combine(emptyDirectory, "production", "all-findings", "type-dependency-hub-candidates.md")));

        await File.WriteAllTextAsync(configPath, explicitDefaultConfig.Replace("\"minFanOut\":10", "\"minFanOut\":0", StringComparison.Ordinal));
        var invalidOptions = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(2, invalidOptions.ExitCode);
        using (var invalidResponse = JsonDocument.Parse(invalidOptions.Error))
        {
            Assert.Equal("INVALID_INPUT", invalidResponse.RootElement.GetProperty("code").GetString());
        }
    }

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
        var allReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "indirection-drift-candidates.md"));
        Assert.False(Directory.Exists(Path.Combine(runDirectory, "production", "changed-files")));
        Assert.Contains("Forwarding path: 2 forwarding edges across 3 types and 3 files", allReport, StringComparison.Ordinal);
        Assert.True(allReport.IndexOf("Sample/ZApi.cs:", StringComparison.Ordinal)
            < allReport.IndexOf("Sample/BService.cs:", StringComparison.Ordinal));
        Assert.True(allReport.IndexOf("Sample/BService.cs:", StringComparison.Ordinal)
            < allReport.IndexOf("Sample/ARepository.cs:", StringComparison.Ordinal));
        Assert.Contains("What responsibility does each forwarding layer add", allReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(servicePath, "public static class BService { public static int Run(int value) { return value; } }");
        var empty = await RunProductionIndirectionAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "production", "all-findings", "indirection-drift-candidates.md")));
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "production", "changed-files", "indirection-drift-candidates.md")));

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
        Assert.Contains("`exact` = 0.95, `near` = 0.80, and `fuzzy` = 0.65", exactReport, StringComparison.Ordinal);
        Assert.Contains("`exact` is the strictest preset, not exact identity.", exactReport, StringComparison.Ordinal);
        Assert.Contains("Jaccard over distinct fixed five-token n-gram sets from method bodies", exactReport, StringComparison.Ordinal);
        Assert.Contains("Whitespace and comments are ignored; identifier and literal token text is retained, with no identifier or local-name normalization.", exactReport, StringComparison.Ordinal);
        Assert.Contains("2 methods;", exactReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", exactReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", exactReport, StringComparison.Ordinal);
        Assert.Contains("ProductA/ProductA.csproj", exactReport, StringComparison.Ordinal);
        Assert.Contains("ProductA/First.cs", exactReport, StringComparison.Ordinal);
        Assert.Matches("[0-9]+(?:\\.[0-9]+)?% similarity \\(minimum [0-9]+(?:\\.[0-9]+)?%\\)", exactReport);
        Assert.Contains("L", exactReport, StringComparison.Ordinal);
        Assert.Contains("ProductB/Second.cs:", exactReport, StringComparison.Ordinal);
        Assert.Contains("## Findings", exactReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Metrics", exactReport, StringComparison.Ordinal);

        await WriteDuplicateConfigAsync(configPath, "fuzzy");
        var fuzzy = await RunProductionDuplicateCodeAsync(configPath);
        var fuzzyReport = await ReadDuplicateCodeReportAsync(projectRoot, fuzzy.RunId);
        Assert.Equal(1, fuzzy.Detected);
        Assert.Contains("\"minimumSimilarity\": \"fuzzy\"", fuzzyReport, StringComparison.Ordinal);
        Assert.Contains("3 methods;", fuzzyReport, StringComparison.Ordinal);
        Assert.Contains("similarity (minimum 65.0%)", fuzzyReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(secondSource,
            WrapDuplicateMethod("SecondContainer", "RunChanged", BuildAlternateDuplicateBody()));
        await WriteDuplicateConfigAsync(configPath, "exact");
        var empty = await RunProductionDuplicateCodeAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        var emptyIndex = await File.ReadAllTextAsync(Path.Combine(emptyRunDirectory, "index.md"));
        Assert.Contains("No findings were found.", emptyIndex, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "production", "all-findings", "duplicate-code-candidates.md")));
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
        Assert.False(File.Exists(Path.Combine(projectRoot, "reports", duplicateOnly.RunId, "production", "all-findings", "structural-duplication-candidates.md")));

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: true);
        var together = await RunStructuralReviewAsync(projectRoot);
        Assert.Equal(2, together.Detected);
        Assert.Equal(WithoutRelatedFindingLines(duplicateOnlyReport),
            WithoutRelatedFindingLines(await ReadDuplicateCodeReportAsync(projectRoot, together.RunId)));
        var structuralReport = await ReadStructuralDuplicateReportAsync(projectRoot, together.RunId, "all-findings");
        Assert.Contains("Structural duplicate: 2 occurrences in 2 executable members", structuralReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", structuralReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", structuralReport, StringComparison.Ordinal);
        Assert.Contains("ProductA/ProductA.csproj", structuralReport, StringComparison.Ordinal);
        Assert.Contains("(production; 1 files, 1 findings)", structuralReport, StringComparison.Ordinal);
        Assert.Contains("ProductA/First.cs", structuralReport, StringComparison.Ordinal);
        Assert.Contains("L", structuralReport, StringComparison.Ordinal);
        var structuralSecondOccurrence = Assert.Single(structuralReport.Split('\n').Where(static line => line.Contains("ProductB/Second.cs", StringComparison.Ordinal)));
        Assert.Matches(@"\[`1:\d+`–`1:\d+`\)", structuralSecondOccurrence);
        var structuralRunDirectory = Path.Combine(projectRoot, "reports", together.RunId);
        var mapIndex = await File.ReadAllTextAsync(Path.Combine(structuralRunDirectory, "audit-map", "all-findings", "index.md"));
        Assert.Contains("Findings: **2**", mapIndex, StringComparison.Ordinal);
        var packageReports = Directory.GetFiles(Path.Combine(structuralRunDirectory, "audit-map", "all-findings"), "*.md")
            .Where(path => !Path.GetFileName(path).Equals("index.md", StringComparison.Ordinal))
            .Select(File.ReadAllText).ToArray();
        Assert.Empty(packageReports);
        Assert.Contains("structural-duplication-candidates.md", mapIndex, StringComparison.Ordinal);
        Assert.Contains("ProductA/First.cs", mapIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductB/Second.cs", mapIndex, StringComparison.Ordinal);
        var structuralId = GetAuditMapFindingId(mapIndex, "structural-duplication-candidates.md");
        Assert.NotEmpty(structuralId);
        Assert.Contains(structuralId, structuralReport, StringComparison.Ordinal);
        Assert.DoesNotContain("<a id=", structuralReport, StringComparison.Ordinal);

        await WriteStructuralDuplicateConfigAsync(configPath, includeStructural: true, structuralEnabled: false);
        var disabled = await RunStructuralReviewAsync(projectRoot);
        Assert.Equal(1, disabled.Detected);
        Assert.False(File.Exists(Path.Combine(projectRoot, "reports", disabled.RunId, "production", "all-findings", "structural-duplication-candidates.md")));

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
        Assert.Contains("ProductA/ProductA.csproj", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("ProductA/First.cs", changedStructuralReport, StringComparison.Ordinal);
        Assert.Contains("L", changedStructuralReport, StringComparison.Ordinal);
        var changedSecondOccurrence = Assert.Single(changedStructuralReport.Split('\n').Where(static line => line.Contains("ProductB/Second.cs", StringComparison.Ordinal)));
        Assert.Matches(@"\[`1:\d+`–`1:\d+`\)", changedSecondOccurrence);

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
        var firstReport = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", first.RunId, "production", "all-findings", "dead-code-candidates.md"));
        Assert.Equal(1, first.Detected);
        Assert.Contains("Type without known use", firstReport, StringComparison.Ordinal);
        Assert.Contains("## Findings", firstReport, StringComparison.Ordinal);
        Assert.Contains("reflection", firstReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external_library", firstReport, StringComparison.Ordinal);

        var second = await RunProductionDeadCodeAsync(configPath);
        var secondReport = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", second.RunId, "production", "all-findings", "dead-code-candidates.md"));
        Assert.Equal(1, second.Detected);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Contains("Type without known use", secondReport, StringComparison.Ordinal);

        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class PublicApi { public void Entry() { } }");
        var empty = await RunProductionDeadCodeAsync(configPath);
        Assert.Equal(0, empty.Detected);
        var emptyRunDirectory = Path.Combine(projectRoot, "reports", empty.RunId);
        var emptyIndex = await File.ReadAllTextAsync(Path.Combine(emptyRunDirectory, "index.md"));
        Assert.Contains("No findings were found.", emptyIndex, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(emptyRunDirectory, "production", "all-findings", "dead-code-candidates.md")));

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
        var allReport = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "production", "all-findings", "missing-test-evidence-candidates.md"));
        var index = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "index.md"));
        Assert.Contains("no static test path", allReport, StringComparison.Ordinal);
        Assert.Contains("indirect test path only", allReport, StringComparison.Ordinal);
        Assert.Contains("; tests)", allReport, StringComparison.Ordinal);
        Assert.Contains("Shortest resolved test path:", allReport, StringComparison.Ordinal);
        Assert.Contains("attribution uncertain` marker means the static test association may be incomplete", allReport, StringComparison.Ordinal);
        Assert.Contains("It does not assess test assertion quality.", allReport, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(firstRunDirectory, "production", "changed-files")));
        Assert.DoesNotContain("changed-files", index, StringComparison.OrdinalIgnoreCase);

        var baseline = await InvokeProductionCommandAsync(["baseline", projectRoot]);
        Assert.Equal(0, baseline.ExitCode);
        var unchanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, unchanged.ExitCode);
        using var unchangedResponse = JsonDocument.Parse(unchanged.Output);
        var unchangedRunId = unchangedResponse.RootElement.GetProperty("runId").GetString()!;
        Assert.NotEqual(firstRunId, unchangedRunId);
        var unchangedDirectory = Path.Combine(projectRoot, "reports", unchangedRunId);
        Assert.Equal(2, unchangedResponse.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        Assert.False(File.Exists(Path.Combine(unchangedDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));
        Assert.True(File.Exists(Path.Combine(unchangedDirectory, "production", "all-findings", "missing-test-evidence-candidates.md")));

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "notes.md"), "non-C# change");
        var nonCSharpOnly = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, nonCSharpOnly.ExitCode);
        using var nonCSharpResponse = JsonDocument.Parse(nonCSharpOnly.Output);
        var nonCSharpDirectory = Path.Combine(projectRoot, "reports", nonCSharpResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.False(File.Exists(Path.Combine(nonCSharpDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));

        await File.AppendAllTextAsync(Path.Combine(testsDirectory, "ApiTests.cs"), " // changed test-only C# path");
        var testOnlyChanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, testOnlyChanged.ExitCode);
        using var testOnlyResponse = JsonDocument.Parse(testOnlyChanged.Output);
        var testOnlyDirectory = Path.Combine(projectRoot, "reports", testOnlyResponse.RootElement.GetProperty("runId").GetString()!);
        var testOnlyReportPath = Path.Combine(testOnlyDirectory, "production", "changed-files", "missing-test-evidence-candidates.md");
        Assert.True(File.Exists(testOnlyReportPath));
        Assert.Contains("source unchanged; included snapshot-wide", await File.ReadAllTextAsync(testOnlyReportPath), StringComparison.Ordinal);

        await InvokeProductionCommandAsync(["baseline", projectRoot]);
        await File.WriteAllTextAsync(extraPath, "namespace Sample; public sealed class Extra { }");
        var added = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, added.ExitCode);
        using var addedResponse = JsonDocument.Parse(added.Output);
        var addedDirectory = Path.Combine(projectRoot, "reports", addedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(addedDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));
        Assert.Contains("source unchanged; included snapshot-wide",
            await File.ReadAllTextAsync(Path.Combine(addedDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")), StringComparison.Ordinal);

        await InvokeProductionCommandAsync(["baseline", projectRoot]);
        await File.AppendAllTextAsync(apiPath, " // changed C# snapshot path");
        var changed = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, changed.ExitCode);
        using var changedResponse = JsonDocument.Parse(changed.Output);
        var changedDirectory = Path.Combine(projectRoot, "reports", changedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(changedDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));

        await InvokeProductionCommandAsync(["baseline", projectRoot]);
        File.Delete(extraPath);
        var deleted = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, deleted.ExitCode);
        using var deletedResponse = JsonDocument.Parse(deleted.Output);
        var deletedDirectory = Path.Combine(projectRoot, "reports", deletedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(deletedDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));
        Assert.Contains("source unchanged; included snapshot-wide",
            await File.ReadAllTextAsync(Path.Combine(deletedDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")), StringComparison.Ordinal);

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
        Assert.False(File.Exists(Path.Combine(emptyDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));
        Assert.False(File.Exists(Path.Combine(emptyDirectory, "production", "all-findings", "missing-test-evidence-candidates.md")));
    }

    [Fact]
    public async Task ReviewCommand_AllAnalysesKeepBaselineAndMixedPartialTypeContractsAcrossTheHost()
    {
        using var tempDirectory = TestTempDirectory.Create("ainet-host-complete-contracts-");
        var projectRoot = tempDirectory.GetPath("complete-contracts");
        var productionDirectory = Path.Combine(projectRoot, "src", "Sample");
        var testDirectory = Path.Combine(projectRoot, "tests", "Sample.Tests");
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(testDirectory);
        const string projectFileContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        var productionProject = Path.Combine(productionDirectory, "Sample.csproj");
        var testProject = Path.Combine(testDirectory, "Sample.Tests.csproj");
        await File.WriteAllTextAsync(productionProject, projectFileContent);
        await File.WriteAllTextAsync(testProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/Sample/Sample.csproj\" /></ItemGroup></Project>");
        var duplicateBody = BuildDuplicateBody();
        await File.WriteAllTextAsync(Path.Combine(productionDirectory, "Production.cs"),
            $"namespace Sample; public partial class Shared {{ public int ProductionClone(int value) {{ {duplicateBody} }} }} public sealed class UnusedHelpers {{ private void Helper() {{ var unused = 1; }} }}");
        await File.WriteAllTextAsync(Path.Combine(testDirectory, "CasesA.cs"),
            $"namespace Sample; public partial class Shared {{ public int TestCloneA(int value) {{ {duplicateBody} }} public void Prüfe() {{ }} }}");
        var secondPartialPath = Path.Combine(testDirectory, "CasesB.cs");
        await File.WriteAllTextAsync(secondPartialPath,
            $"namespace Sample; public partial class Shared {{ public int TestCloneB(int value) {{ {duplicateBody} }} }}");
        await RestoreProjectAsync(productionProject, productionDirectory);
        await RestoreProjectAsync(testProject, testDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Sample.slnx"),
            "<Solution><Project Path=\"src/Sample/Sample.csproj\" /><Project Path=\"tests/Sample.Tests/Sample.Tests.csproj\" /></Solution>");
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        const string fullConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"code-size-candidates\":{},\"dead-code-candidates\":{},\"duplicate-code-candidates\":{},\"indirection-drift-candidates\":{},\"method-control-flow-outliers\":{},\"missing-test-evidence-candidates\":{},\"non-ascii-identifiers\":{},\"structural-duplication-candidates\":{},\"type-dependency-cycle-candidates\":{},\"type-dependency-hub-candidates\":{}}}";
        await File.WriteAllTextAsync(configPath, fullConfig);

        var first = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, first.ExitCode);
        using var firstResponse = JsonDocument.Parse(first.Output);
        var firstRunDirectory = Path.Combine(projectRoot, "reports", firstResponse.RootElement.GetProperty("runId").GetString()!);
        var rootIndex = await File.ReadAllTextAsync(Path.Combine(firstRunDirectory, "index.md"));
        Assert.False(Directory.EnumerateFileSystemEntries(firstRunDirectory, "changed-files", SearchOption.AllDirectories).Any());
        foreach (var markdownPath in Directory.EnumerateFiles(firstRunDirectory, "*.md", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("changed-files", await File.ReadAllTextAsync(markdownPath), StringComparison.OrdinalIgnoreCase);
        }
        foreach (var analysisId in new[]
        {
            "code-size-candidates", "dead-code-candidates", "duplicate-code-candidates", "indirection-drift-candidates",
            "method-control-flow-outliers", "missing-test-evidence-candidates", "non-ascii-identifiers", "structural-duplication-candidates", "type-dependency-cycle-candidates", "type-dependency-hub-candidates",
        })
        {
            Assert.Contains($"| `{analysisId}` | yes |", rootIndex, StringComparison.Ordinal);
        }

        foreach (var area in new[] { "production", "tests", "mixed" })
        {
            Assert.False(Directory.Exists(Path.Combine(firstRunDirectory, area, "changed-files")));
            Assert.True(File.Exists(Path.Combine(firstRunDirectory, area, "all-findings", "index.md")));
        }

        var mixedAll = Path.Combine(firstRunDirectory, "mixed", "all-findings", "duplicate-code-candidates.md");
        Assert.True(File.Exists(mixedAll));
        var mixedAllReport = await File.ReadAllTextAsync(mixedAll);
        Assert.Contains("Production.cs", mixedAllReport, StringComparison.Ordinal);
        Assert.Contains("CasesA.cs", mixedAllReport, StringComparison.Ordinal);
        Assert.Contains("CasesB.cs", mixedAllReport, StringComparison.Ordinal);
        var testFinding = Path.Combine(firstRunDirectory, "tests", "all-findings", "non-ascii-identifiers.md");
        Assert.True(File.Exists(testFinding));

        await File.WriteAllTextAsync(configPath,
            fullConfig.Replace("\"non-ascii-identifiers\":{}", "\"non-ascii-identifiers\":{\"enabled\":false}", StringComparison.Ordinal));
        var baseline = await InvokeProductionCommandAsync(["baseline", projectRoot]);
        Assert.Equal(0, baseline.ExitCode);
        var baselineBytes = await File.ReadAllBytesAsync(Path.Combine(projectRoot, "reports", "baseline.json"));
        await File.WriteAllTextAsync(configPath, fullConfig);
        var unchanged = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, unchanged.ExitCode);
        using var unchangedResponse = JsonDocument.Parse(unchanged.Output);
        var unchangedRunDirectory = Path.Combine(projectRoot, "reports", unchangedResponse.RootElement.GetProperty("runId").GetString()!);
        Assert.True(File.Exists(Path.Combine(unchangedRunDirectory, "tests", "all-findings", "non-ascii-identifiers.md")));
        Assert.False(File.Exists(Path.Combine(unchangedRunDirectory, "tests", "changed-files", "non-ascii-identifiers.md")));
        Assert.True(File.Exists(Path.Combine(unchangedRunDirectory, "mixed", "all-findings", "duplicate-code-candidates.md")));
        Assert.False(File.Exists(Path.Combine(unchangedRunDirectory, "mixed", "changed-files", "duplicate-code-candidates.md")));
        Assert.Equal(baselineBytes, await File.ReadAllBytesAsync(Path.Combine(projectRoot, "reports", "baseline.json")));

        await File.AppendAllTextAsync(secondPartialPath, " // changed test-only file");
        var changedTest = await InvokeProductionCommandAsync(["review", projectRoot]);
        Assert.Equal(0, changedTest.ExitCode);
        using var changedResponse = JsonDocument.Parse(changedTest.Output);
        var changedRunDirectory = Path.Combine(projectRoot, "reports", changedResponse.RootElement.GetProperty("runId").GetString()!);
        var changedMixedReport = Path.Combine(changedRunDirectory, "mixed", "changed-files", "duplicate-code-candidates.md");
        Assert.True(File.Exists(changedMixedReport));
        var changedReport = await File.ReadAllTextAsync(changedMixedReport);
        Assert.Contains("Production.cs", changedReport, StringComparison.Ordinal);
        Assert.Contains("CasesA.cs", changedReport, StringComparison.Ordinal);
        Assert.Contains("CasesB.cs", changedReport, StringComparison.Ordinal);
        Assert.Equal(fullConfig, await File.ReadAllTextAsync(configPath));
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
        Assert.Equal(new[] { "index.md" }, Directory.GetFiles(Path.Combine(thirdRunDirectory, "production", "all-findings"), "*.md").Select(Path.GetFileName));
        Assert.False(Directory.Exists(Path.Combine(thirdRunDirectory, "production", "changed-files")));

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
        var report = await File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId!, "production", "all-findings", "fixture-finding.md"));
        Assert.Contains("Fixture scenario 'base' requires review of FixtureCaseA.", report, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "code-size-candidates", "dead-code-candidates", "duplicate-code-candidates", "fixture-finding", "indirection-drift-candidates", "method-control-flow-outliers", "missing-test-evidence-candidates", "non-ascii-identifiers", "structural-duplication-candidates", "type-dependency-cycle-candidates", "type-dependency-hub-candidates" },
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
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "production", "all-findings", "fixture-finding.md"));

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

    private static string GetFirstAuditMapFindingId(string markdown)
    {
        return Regex.Match(markdown, @"finding-[a-f0-9]{24}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Value;
    }

    private static string GetAuditMapFindingId(string markdown, string analysisId)
    {
        var routeLine = markdown.Split('\n').FirstOrDefault(line => line.Contains(analysisId, StringComparison.Ordinal));
        return routeLine is null
            ? string.Empty
            : Regex.Match(routeLine, @"finding-[a-f0-9]{24}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Value;
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
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "production", view, "structural-duplication-candidates.md"));

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
        File.ReadAllTextAsync(Path.Combine(projectRoot, "reports", runId, "production", "all-findings", "duplicate-code-candidates.md"));

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
