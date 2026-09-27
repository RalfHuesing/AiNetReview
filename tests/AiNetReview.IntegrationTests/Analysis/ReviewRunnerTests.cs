namespace AiNetReview.IntegrationTests.Analysis;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;
using AiNetReview.Core.Storage;
using AiNetReview.IntegrationTests.FixtureRules;

public sealed class ReviewRunnerTests
{
    [Fact]
    public async Task RunAsync_ReconcilesTwoIndependentFindingsAndHashesSourceBytes()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp);
        var store = new InMemoryFindingStore();
        var firstRule = new FixtureFindingRule();
        var config = CreateConfig(root, [firstRule], "base");
        using var loaded = await new SolutionLoader().LoadAsync(config);
        var runner = new ReviewRunner(store);

        var firstRun = await runner.RunAsync(config, loaded);
        var originalIds = firstRun.Observations.Select(static observation => observation.FindingId).ToArray();
        var repeated = await runner.RunAsync(config, loaded);

        Assert.Equal(2, firstRun.Observations.Count);
        Assert.Equal(2, firstRun.Events.Count);
        Assert.All(firstRun.Events, change => Assert.Equal(FindingEventType.New, change.EventType));
        Assert.Equal(2, originalIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, repeated.Observations.Count);
        Assert.Empty(repeated.Events);
        Assert.Equal(originalIds.Order(StringComparer.Ordinal), repeated.Observations.Select(static item => item.FindingId).Order(StringComparer.Ordinal));
        var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(root, "Sample", "FixtureCases.cs"));
        var expectedHash = "sha256:" + Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        Assert.All(firstRun.Observations, observation => Assert.Equal(expectedHash, Assert.Single(observation.SourceFiles).Sha256));
        Assert.All(firstRun.Observations, observation => Assert.DoesNotContain('\r', observation.Snapshot));

        var templateOnly = CreateConfig(root, [new TemplateNoOpRule()]);
        var inactiveRuleRun = await runner.RunAsync(templateOnly, loaded);
        Assert.Empty(inactiveRuleRun.Events);
        Assert.Equal(2, inactiveRuleRun.CurrentFindings.Values.Count(static finding => finding.Identity.RuleId == "fixture-finding"));
        Assert.All(inactiveRuleRun.CurrentFindings.Values.Where(static finding => finding.Identity.RuleId == "fixture-finding"),
            finding => Assert.Equal(FindingState.Open, finding.State));

        var optionChangedConfig = CreateConfig(root, [new FixtureFindingRule()], "variant");
        var optionChanged = await runner.RunAsync(optionChangedConfig, loaded);
        Assert.Equal(2, optionChanged.Events.Count);
        Assert.All(optionChanged.Events, change => Assert.Equal(FindingEventType.Updated, change.EventType));
        Assert.Equal(originalIds.Order(StringComparer.Ordinal), optionChanged.Observations.Select(static item => item.FindingId).Order(StringComparer.Ordinal));

        var versionChangedConfig = CreateConfig(root, [new FixtureFindingRule(behaviorVersion: 2)], "variant");
        var versionChanged = await runner.RunAsync(versionChangedConfig, loaded);
        Assert.Equal(2, versionChanged.Events.Count);
        Assert.All(versionChanged.Events, change => Assert.Equal(FindingEventType.Updated, change.EventType));

        var changedRule = new FixtureFindingRule(comparisonTextSuffix: "changed behavior text");
        var changedConfig = CreateConfig(root, [changedRule], "variant");
        var changed = await runner.RunAsync(changedConfig, loaded);
        Assert.Equal(2, changed.Events.Count);
        Assert.All(changed.Events, change => Assert.Equal(FindingEventType.Updated, change.EventType));
        Assert.Equal(originalIds.Order(StringComparer.Ordinal), changed.Observations.Select(static item => item.FindingId).Order(StringComparer.Ordinal));

        var emptyConfig = CreateConfig(root, [new FixtureFindingRule()], "none");
        var resolved = await runner.RunAsync(emptyConfig, loaded);
        Assert.Empty(resolved.Observations);
        Assert.Equal(2, resolved.Events.Count);
        Assert.All(resolved.Events, change => Assert.Equal(FindingEventType.Resolved, change.EventType));
        var stillResolved = await runner.RunAsync(emptyConfig, loaded);
        Assert.Empty(stillResolved.Events);
    }

    [Fact]
    public async Task RunAsync_OrdersRulesAndDoesNotCommitIncompleteRuns()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp);
        var calls = new List<string>();
        var alpha = new TestRule("alpha-rule", calls);
        var zeta = new TestRule("zeta-rule", calls);
        var config = CreateConfig(root, [zeta, alpha]);
        using var loaded = await new SolutionLoader().LoadAsync(config);
        var store = new CountingStore();
        var runner = new ReviewRunner(store);

        var result = await runner.RunAsync(config, loaded);

        Assert.Equal(new[] { "alpha-rule", "zeta-rule" }, calls);
        Assert.Empty(result.Observations);
        Assert.Empty(result.Events);
        Assert.Equal(1, store.CommitCount);

        var failure = new TestRule("failure-rule", calls, throwOnRun: true);
        var failureConfig = CreateConfig(root, [failure]);
        await Assert.ThrowsAsync<AnalysisFailedException>(() => runner.RunAsync(failureConfig, loaded));
        Assert.Equal(1, store.CommitCount);

        using var cancellation = new CancellationTokenSource();
        var cancelling = new TestRule("cancelling-rule", calls, cancel: cancellation);
        var cancellationConfig = CreateConfig(root, [cancelling]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellationConfig, loaded, cancellation.Token));
        Assert.Equal(1, store.CommitCount);

        var templateConfig = CreateConfig(root, [new TemplateNoOpRule()]);
        var noop = await runner.RunAsync(templateConfig, loaded);
        Assert.Empty(noop.Observations);
        Assert.Empty(noop.Events);
        Assert.Equal(2, store.CommitCount);
    }

    private static ReviewConfig CreateConfig(string root, IEnumerable<IReviewRule> rules, string? scenario = null)
    {
        var registry = new RuleRegistry(rules);
        var ruleJson = string.Join(",", registry.Rules.Select(rule => scenario is null
            ? $"\"{rule.Descriptor.RuleId}\":{{}}"
            : $"\"{rule.Descriptor.RuleId}\":{{\"scenario\":\"{scenario}\"}}"));
        var json = $$"""
            {
              "schemaVersion": 1,
              "solution": "Sample.slnx",
              "outputDirectory": "reports",
              "storageDirectory": ".review-store",
              "rules": { {{ruleJson}} }
            }
            """;
        return new ReviewConfigValidator(registry).Validate(root, json);
    }

    private static async Task<string> CreateProjectAsync(TestTempDirectory temp)
    {
        var root = temp.GetPath("runner-project");
        var projectDirectory = Path.Combine(root, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "FixtureCases.cs"), """
            namespace Sample;
            public sealed class FixtureCases
            {
                public int FixtureCaseA() => 1;
                public int FixtureCaseB() => 2;
            }
            """);
        await RestoreAsync(projectFile, projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        return root;
    }

    private static async Task RestoreAsync(string projectFile, string workingDirectory)
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

    private sealed class TestRule : IReviewRule
    {
        private readonly List<string> calls;
        private readonly bool throwOnRun;
        private readonly CancellationTokenSource? cancel;

        internal TestRule(string ruleId, List<string> calls, bool throwOnRun = false, CancellationTokenSource? cancel = null)
        {
            this.calls = calls;
            this.throwOnRun = throwOnRun;
            this.cancel = cancel;
            Descriptor = new RuleDescriptor(ruleId, "Test Rule", 1, "Test purpose.", "Test measurement.", ["Is this test rule registered?"]);
        }

        public RuleDescriptor Descriptor { get; }

        public async Task<RuleResult> ExecuteAsync(ReviewContext context, RuleOptions options, CancellationToken cancellationToken)
        {
            calls.Add(Descriptor.RuleId);
            if (throwOnRun)
            {
                throw new InvalidOperationException("fixture rule failed");
            }

            if (cancel is not null)
            {
                await cancel.CancelAsync();
            }

            return RuleResult.Empty;
        }
    }

    private sealed class CountingStore : IFindingStore
    {
        private readonly InMemoryFindingStore inner = new();

        internal int CommitCount { get; private set; }

        public Task<FindingStoreSnapshot> ReadAsync(string projectRoot, CancellationToken cancellationToken) =>
            inner.ReadAsync(projectRoot, cancellationToken);

        public Task CommitCompletedRunAsync(string projectRoot, FindingRunCommit run, CancellationToken cancellationToken)
        {
            CommitCount++;
            return inner.CommitCompletedRunAsync(projectRoot, run, cancellationToken);
        }
    }
}
