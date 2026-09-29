namespace AiNetReview.IntegrationTests.Bootstrap;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiNetReview.Bootstrap;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;
using AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;
using AiNetReview.IntegrationTests.FixtureAnalyses;
using Microsoft.Extensions.DependencyInjection;

public sealed class AnalysisServiceRegistrationTests
{
    [Fact]
    public void ProductionAnalysisRegistration_ContainsConfiguredProductAnalyses()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        using var provider = services.BuildServiceProvider();

        var analyses = provider.GetServices<IReviewAnalysis>().ToArray();
        var registry = provider.GetRequiredService<ReviewAnalysisRegistry>();

        Assert.Equal(3, analyses.Length);
        Assert.Contains(analyses, static analysis => analysis is MethodControlFlowOutliersAnalysis);
        Assert.Contains(analyses, static analysis => analysis is DeadCodeCandidatesAnalysis);
        Assert.Contains(analyses, static analysis => analysis is DuplicateCodeCandidatesAnalysis);
        Assert.Equal(new[] { "dead-code-candidates", "duplicate-code-candidates", "method-control-flow-outliers" }, registry.Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));
        Assert.Same(analyses.Single(static analysis => analysis is MethodControlFlowOutliersAnalysis), registry.GetRequired("method-control-flow-outliers"));
        Assert.Same(analyses.Single(static analysis => analysis is DeadCodeCandidatesAnalysis), registry.GetRequired("dead-code-candidates"));
        Assert.Same(analyses.Single(static analysis => analysis is DuplicateCodeCandidatesAnalysis), registry.GetRequired("duplicate-code-candidates"));
        Assert.False(registry.TryGet("fixture-finding", out _));
    }

    [Fact]
    public void RepositoryConfiguration_ListsEveryProductionAnalysis()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "ainetreview.json")));
        var configuredAnalyses = document.RootElement.GetProperty("analyses");
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ReviewAnalysisRegistry>();

        foreach (var analysis in registry.Analyses)
        {
            Assert.True(configuredAnalyses.TryGetProperty(analysis.Descriptor.AnalysisId, out var options),
                $"Production analysis '{analysis.Descriptor.AnalysisId}' is missing from the repository ainetreview.json.");
            Assert.True(options.TryGetProperty("enabled", out var enabled));
            Assert.Equal(JsonValueKind.True, enabled.ValueKind);
        }
    }

    [Fact]
    public void TestAnalysis_CanBeAddedThroughAnExplicitTestRegistration()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddSingleton<IReviewAnalysis, FixtureFindingAnalysis>();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ReviewAnalysisRegistry>();

        Assert.Equal(new[] { "dead-code-candidates", "duplicate-code-candidates", "fixture-finding", "method-control-flow-outliers" }, registry.Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));
        Assert.IsType<FixtureFindingAnalysis>(registry.GetRequired("fixture-finding"));
    }

    [Fact]
    public void FixtureAnalysis_UsesBaseScenarioByDefaultAndRejectsEmptyScenarios()
    {
        var descriptor = new FixtureFindingAnalysis().Descriptor;

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
