namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Text.Json;
using AiNetReview.Core.ReviewAnalyses;

public sealed class ReviewAnalysisDescriptorTests
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
    [InlineData("analysis_id")]
    [InlineData("analysis--id")]
    [InlineData("-analysis")]
    [InlineData("analysis-")]
    [InlineData("Review analysis")]
    [InlineData("rulé")]
    public void Constructor_RejectsAnalysisIdsThatAreNotSafeFileNames(string analysisId)
    {
        Assert.Throws<ArgumentException>(() => CreateDescriptor(analysisId: analysisId));
    }

    [Fact]
    public void Constructor_RejectsAnalysisIdsWhoseMarkdownFileNameExceedsCommonSegmentLimit()
    {
        Assert.Throws<ArgumentException>(() => CreateDescriptor(analysisId: new string('a', 253)));
    }

    [Fact]
    public void Constructor_AcceptsSafeAnalysisIdAtMaximumMarkdownFileNameLength()
    {
        var descriptor = CreateDescriptor(analysisId: new string('a', 252));

        Assert.Equal(252, descriptor.AnalysisId.Length);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveBehaviorVersionAndIncompleteAnalysisMetadata()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDescriptor(behaviorVersion: 0));
        Assert.Throws<ArgumentException>(() => new ReviewAnalysisDescriptor(
            " test-analysis",
            "Test Review analysis",
            1,
            "Test purpose.",
            "Test measurement.",
            ["Is this test descriptor valid?"]));
        Assert.Throws<ArgumentException>(() => CreateDescriptor(purpose: " "));
        Assert.Throws<ArgumentException>(() => CreateDescriptor(reviewQuestions: []));
        Assert.Throws<ArgumentException>(() => CreateDescriptor(reviewQuestions: [" "]));
    }

    [Fact]
    public void Constructor_ProvidesEnabledAnalysisDefault()
    {
        Assert.True(CreateDescriptor().DefaultEnabled);
        Assert.False(CreateDescriptor(defaultEnabled: false).DefaultEnabled);
    }

    [Fact]
    public void Constructor_RejectsDuplicateOptionNames()
    {
        var option = ReviewAnalysisOptionDescriptor.String("scenario", "Scenario", "base");

        var exception = Assert.Throws<ArgumentException>(() => CreateDescriptor(options: [option, option]));

        Assert.Contains("Duplicate option name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOptions_AppliesDefaultsAndReturnsOptionsSortedByName()
    {
        var descriptor = CreateDescriptor(options:
        [
            ReviewAnalysisOptionDescriptor.String("zeta", "Zeta mode", "last"),
            ReviewAnalysisOptionDescriptor.String("alpha", "Scenario", "base", static value => value is "base" or "alternate"),
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
            ReviewAnalysisOptionDescriptor.String("scenario", "Scenario", "base", static value => value is "base" or "alternate"),
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
        Assert.Throws<ArgumentException>(() => ReviewAnalysisOptionDescriptor.String(
            "scenario",
            "Scenario",
            "unsupported",
            static value => value is "base" or "alternate"));
    }

    private static ReviewAnalysisDescriptor CreateDescriptor(
        string analysisId = "test-analysis",
        int behaviorVersion = 1,
        string purpose = "Test purpose.",
        IReadOnlyList<string>? reviewQuestions = null,
        IReadOnlyList<ReviewAnalysisOptionDescriptor>? options = null,
        bool defaultEnabled = true) => new(
            analysisId: analysisId,
            title: "Test Review analysis",
            behaviorVersion: behaviorVersion,
            purpose: purpose,
            measurement: "Test measurement.",
            reviewQuestions: reviewQuestions ?? ["Is this test descriptor valid?"],
            options: options,
            defaultEnabled: defaultEnabled);
}
