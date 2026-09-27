namespace AiNetReview.IntegrationTests.Bootstrap;

using System.Linq;
using AiNetReview.Bootstrap;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;
using AiNetReview.IntegrationTests.FixtureRules;
using Microsoft.Extensions.DependencyInjection;

public sealed class RuleServiceRegistrationTests
{
    [Fact]
    public void ProductionRuleRegistration_ContainsOnlyTemplateNoOp()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        using var provider = services.BuildServiceProvider();

        var rules = provider.GetServices<IReviewRule>().ToArray();
        var registry = provider.GetRequiredService<RuleRegistry>();

        var rule = Assert.Single(rules);
        Assert.IsType<TemplateNoOpRule>(rule);
        Assert.Single(registry.Rules);
        Assert.Same(rule, registry.GetRequired("template-noop"));
        Assert.False(registry.TryGet("fixture-finding", out _));
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

        Assert.Equal(new[] { "fixture-finding", "template-noop" }, registry.Rules.Select(static rule => rule.Descriptor.RuleId));
        Assert.IsType<FixtureFindingRule>(registry.GetRequired("fixture-finding"));
    }
}
