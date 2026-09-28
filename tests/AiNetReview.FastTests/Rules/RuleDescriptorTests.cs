namespace AiNetReview.FastTests.Rules;

using System;
using System.Collections.Generic;
using System.Text.Json;
using AiNetReview.Core.Rules;

public sealed class RuleDescriptorTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("prn")]
    [InlineData("aux")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("lpt9")]
    [InlineData("rule_id")]
    [InlineData("rule--id")]
    [InlineData("-rule")]
    [InlineData("rule-")]
    [InlineData("Rule")]
    [InlineData("rulé")]
    public void Constructor_RejectsRuleIdsThatAreNotSafeFileNames(string ruleId)
    {
        Assert.Throws<ArgumentException>(() => CreateDescriptor(ruleId: ruleId));
    }

    [Fact]
    public void Constructor_RejectsRuleIdsWhoseMarkdownFileNameExceedsCommonSegmentLimit()
    {
        Assert.Throws<ArgumentException>(() => CreateDescriptor(ruleId: new string('a', 253)));
    }

    [Fact]
    public void Constructor_AcceptsSafeRuleIdAtMaximumMarkdownFileNameLength()
    {
        var descriptor = CreateDescriptor(ruleId: new string('a', 252));

        Assert.Equal(252, descriptor.RuleId.Length);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveBehaviorVersionAndIncompleteRuleMetadata()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDescriptor(behaviorVersion: 0));
        Assert.Throws<ArgumentException>(() => new RuleDescriptor(
            " test-rule",
            "Test Rule",
            1,
            "Test purpose.",
            "Test measurement.",
            ["Is this test descriptor valid?"]));
        Assert.Throws<ArgumentException>(() => CreateDescriptor(purpose: " "));
        Assert.Throws<ArgumentException>(() => CreateDescriptor(reviewQuestions: []));
        Assert.Throws<ArgumentException>(() => CreateDescriptor(reviewQuestions: [" "]));
    }

    [Fact]
    public void Constructor_ProvidesEnabledRuleDefault()
    {
        Assert.True(CreateDescriptor().DefaultEnabled);
        Assert.False(CreateDescriptor(defaultEnabled: false).DefaultEnabled);
    }

    [Fact]
    public void Constructor_RejectsDuplicateOptionNames()
    {
        var option = RuleOptionDescriptor.String("scenario", "Scenario", "base");

        var exception = Assert.Throws<ArgumentException>(() => CreateDescriptor(options: [option, option]));

        Assert.Contains("Duplicate option name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOptions_AppliesDefaultsAndReturnsOptionsSortedByName()
    {
        var descriptor = CreateDescriptor(options:
        [
            RuleOptionDescriptor.String("zeta", "Zeta mode", "last"),
            RuleOptionDescriptor.String("alpha", "Scenario", "base", static value => value is "base" or "alternate"),
        ]);

        var defaults = descriptor.ResolveOptions();
        var configured = descriptor.ResolveOptions(
        [
            KeyValuePair.Create("alpha", JsonSerializer.SerializeToElement("alternate")),
        ]);

        Assert.Equal(new[] { "alpha", "zeta" }, defaults.Values.Keys);
        Assert.Equal("base", defaults["alpha"].GetString());
        Assert.Equal("last", defaults["zeta"].GetString());
        Assert.Equal("alternate", configured["alpha"].GetString());
        Assert.Equal("last", configured["zeta"].GetString());
    }

    [Fact]
    public void ResolveOptions_RejectsUnknownDuplicateAndInvalidValues()
    {
        var descriptor = CreateDescriptor(options:
        [
            RuleOptionDescriptor.String("scenario", "Scenario", "base", static value => value is "base" or "alternate"),
        ]);

        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions(
        [
            KeyValuePair.Create("unknown", JsonSerializer.SerializeToElement("value")),
        ]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions(
        [
            KeyValuePair.Create("scenario", JsonSerializer.SerializeToElement(3)),
        ]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions(
        [
            KeyValuePair.Create("scenario", JsonSerializer.SerializeToElement("unsupported")),
        ]));
        Assert.Throws<ArgumentException>(() => descriptor.ResolveOptions(
        [
            KeyValuePair.Create("scenario", JsonSerializer.SerializeToElement("base")),
            KeyValuePair.Create("scenario", JsonSerializer.SerializeToElement("alternate")),
        ]));
    }

    [Fact]
    public void StringOption_RejectsDefaultOutsideItsValidator()
    {
        Assert.Throws<ArgumentException>(() => RuleOptionDescriptor.String(
            "scenario",
            "Scenario",
            "unsupported",
            static value => value is "base" or "alternate"));
    }

    private static RuleDescriptor CreateDescriptor(
        string ruleId = "test-rule",
        int behaviorVersion = 1,
        string purpose = "Test purpose.",
        IReadOnlyList<string>? reviewQuestions = null,
        IReadOnlyList<RuleOptionDescriptor>? options = null,
        bool defaultEnabled = true) => new(
            ruleId: ruleId,
            title: "Test Rule",
            behaviorVersion: behaviorVersion,
            purpose: purpose,
            measurement: "Test measurement.",
            reviewQuestions: reviewQuestions ?? ["Is this test descriptor valid?"],
            options: options,
            defaultEnabled: defaultEnabled);
}
