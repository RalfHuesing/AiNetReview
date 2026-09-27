namespace AiNetReview.FastTests.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Rules;

public sealed class RuleRegistryTests
{
    [Fact]
    public void Constructor_RejectsDuplicateRuleIds()
    {
        var first = new TestRule("duplicate-rule");
        var second = new TestRule("duplicate-rule");

        var exception = Assert.Throws<ArgumentException>(() => new RuleRegistry([first, second]));

        Assert.Contains("Duplicate rule ID 'duplicate-rule'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_AreSortedByIdAndCanBeResolved()
    {
        var alpha = new TestRule("alpha-rule");
        var zeta = new TestRule("zeta-rule");
        var registry = new RuleRegistry([zeta, alpha]);

        Assert.Equal(new[] { "alpha-rule", "zeta-rule" }, registry.Rules.Select(static rule => rule.Descriptor.RuleId));
        Assert.Same(alpha, registry.GetRequired("alpha-rule"));
        Assert.True(registry.TryGet("zeta-rule", out var resolved));
        Assert.Same(zeta, resolved);
        Assert.False(registry.TryGet("missing-rule", out _));
        Assert.Throws<KeyNotFoundException>(() => registry.GetRequired("missing-rule"));
    }

    private sealed class TestRule : IReviewRule
    {
        internal TestRule(string ruleId)
        {
            Descriptor = new RuleDescriptor(
                ruleId,
                "Test Rule",
                1,
                "Test purpose.",
                "Test measurement.",
                ["Is this test rule registered?"]);
        }

        public RuleDescriptor Descriptor { get; }

        public Task<RuleResult> ExecuteAsync(
            ReviewContext context,
            RuleOptions options,
            CancellationToken cancellationToken) => Task.FromResult(RuleResult.Empty);
    }
}
