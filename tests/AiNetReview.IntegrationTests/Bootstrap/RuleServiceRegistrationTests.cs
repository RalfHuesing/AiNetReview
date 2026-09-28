namespace AiNetReview.IntegrationTests.Bootstrap;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiNetReview.Bootstrap;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.MethodControlFlowOutliers;
using AiNetReview.Core.Rules.DeadCodeCandidates;
using AiNetReview.IntegrationTests.FixtureRules;
using Microsoft.Extensions.DependencyInjection;

public sealed class RuleServiceRegistrationTests
{
    [Fact]
    public void ProductionRuleRegistration_ContainsConfiguredProductRules()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        using var provider = services.BuildServiceProvider();

        var rules = provider.GetServices<IReviewRule>().ToArray();
        var registry = provider.GetRequiredService<RuleRegistry>();

        Assert.Equal(2, rules.Length);
        Assert.Contains(rules, static rule => rule is MethodControlFlowOutliersRule);
        Assert.Contains(rules, static rule => rule is DeadCodeCandidatesRule);
        Assert.Equal(new[] { "dead-code-candidates", "method-control-flow-outliers" }, registry.Rules.Select(static rule => rule.Descriptor.RuleId));
        Assert.Same(rules.Single(static rule => rule is MethodControlFlowOutliersRule), registry.GetRequired("method-control-flow-outliers"));
        Assert.Same(rules.Single(static rule => rule is DeadCodeCandidatesRule), registry.GetRequired("dead-code-candidates"));
        Assert.False(registry.TryGet("fixture-finding", out _));
    }

    [Fact]
    public void RepositoryConfiguration_ListsEveryProductionRule()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "ainetreview.json")));
        var configuredRules = document.RootElement.GetProperty("rules");
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<RuleRegistry>();

        foreach (var rule in registry.Rules)
        {
            Assert.True(configuredRules.TryGetProperty(rule.Descriptor.RuleId, out var options),
                $"Production rule '{rule.Descriptor.RuleId}' is missing from the repository ainetreview.json.");
            Assert.True(options.TryGetProperty("enabled", out var enabled));
            Assert.Equal(JsonValueKind.True, enabled.ValueKind);
        }
    }

    [Fact]
    public void TestRule_CanBeAddedThroughAnExplicitTestRegistration()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddSingleton<IReviewRule, FixtureFindingRule>();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<RuleRegistry>();

        Assert.Equal(new[] { "dead-code-candidates", "fixture-finding", "method-control-flow-outliers" }, registry.Rules.Select(static rule => rule.Descriptor.RuleId));
        Assert.IsType<FixtureFindingRule>(registry.GetRequired("fixture-finding"));
    }

    [Fact]
    public void FixtureRule_UsesBaseScenarioByDefaultAndRejectsEmptyScenarios()
    {
        var descriptor = new FixtureFindingRule().Descriptor;

        Assert.Equal("base", descriptor.ResolveOptions()["scenario"].GetString());
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions(
        [
            KeyValuePair.Create("scenario", JsonSerializer.SerializeToElement(string.Empty)),
        ]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions(
        [
            KeyValuePair.Create("scenario", JsonSerializer.SerializeToElement(" ")),
        ]));
    }
}
