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
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;
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

        Assert.Equal(8, analyses.Length);
        Assert.Contains(analyses, static analysis => analysis is MethodControlFlowOutliersAnalysis);
        Assert.Contains(analyses, static analysis => analysis is DeadCodeCandidatesAnalysis);
        Assert.Contains(analyses, static analysis => analysis is DuplicateCodeCandidatesAnalysis);
        Assert.Contains(analyses, static analysis => analysis is NonAsciiIdentifiersAnalysis);
        Assert.Contains(analyses, static analysis => analysis is IndirectionDriftCandidatesAnalysis);
        Assert.Contains(analyses, static analysis => analysis is MissingTestEvidenceCandidatesAnalysis);
        Assert.Contains(analyses, static analysis => analysis is CodeSizeCandidatesAnalysis);
        Assert.Contains(analyses, static analysis => analysis is StructuralDuplicationCandidatesAnalysis);
        Assert.Equal(new[] { "code-size-candidates", "dead-code-candidates", "duplicate-code-candidates", "indirection-drift-candidates", "method-control-flow-outliers", "missing-test-evidence-candidates", "non-ascii-identifiers", "structural-duplication-candidates" }, registry.Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));
        Assert.Same(analyses.Single(static analysis => analysis is MethodControlFlowOutliersAnalysis), registry.GetRequired("method-control-flow-outliers"));
        Assert.Same(analyses.Single(static analysis => analysis is DeadCodeCandidatesAnalysis), registry.GetRequired("dead-code-candidates"));
        Assert.Same(analyses.Single(static analysis => analysis is DuplicateCodeCandidatesAnalysis), registry.GetRequired("duplicate-code-candidates"));
        Assert.Same(analyses.Single(static analysis => analysis is NonAsciiIdentifiersAnalysis), registry.GetRequired("non-ascii-identifiers"));
        Assert.Same(analyses.Single(static analysis => analysis is IndirectionDriftCandidatesAnalysis), registry.GetRequired("indirection-drift-candidates"));
        Assert.Same(analyses.Single(static analysis => analysis is MissingTestEvidenceCandidatesAnalysis), registry.GetRequired("missing-test-evidence-candidates"));
        Assert.Same(analyses.Single(static analysis => analysis is CodeSizeCandidatesAnalysis), registry.GetRequired("code-size-candidates"));
        Assert.Same(analyses.Single(static analysis => analysis is StructuralDuplicationCandidatesAnalysis), registry.GetRequired("structural-duplication-candidates"));
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
            foreach (var option in analysis.Descriptor.Options)
            {
                Assert.True(options.TryGetProperty(option.Name, out var configuredOption),
                    $"Production analysis '{analysis.Descriptor.AnalysisId}' is missing default option '{option.Name}'.");
                Assert.Equal(option.DefaultValue.GetRawText(), configuredOption.GetRawText());
            }

            if (analysis.Descriptor.TestOptions.Count == 0)
            {
                Assert.False(options.TryGetProperty("testOptions", out _));
            }
            else
            {
                Assert.True(options.TryGetProperty("testOptions", out var testOptions));
                Assert.Equal(analysis.Descriptor.TestOptions.Count, testOptions.EnumerateObject().Count());
                foreach (var option in analysis.Descriptor.TestOptions)
                {
                    Assert.Equal(option.DefaultValue.GetRawText(), testOptions.GetProperty(option.Name).GetRawText());
                }
            }
        }

        var missingTestEvidence = configuredAnalyses.GetProperty("missing-test-evidence-candidates");
        Assert.Equal(3, missingTestEvidence.GetProperty("minDecisionCount").GetInt32());
        Assert.Equal(2, missingTestEvidence.GetProperty("minDecisionNesting").GetInt32());
        Assert.Equal(5, missingTestEvidence.GetProperty("minIndirectDecisionCount").GetInt32());
        Assert.Equal(3, missingTestEvidence.GetProperty("minIndirectDecisionNesting").GetInt32());
        var codeSize = configuredAnalyses.GetProperty("code-size-candidates");
        Assert.True(codeSize.GetProperty("enabled").GetBoolean());
        Assert.Equal(90, codeSize.GetProperty("percentile").GetInt32());
        Assert.Equal(80, codeSize.GetProperty("minMemberCodeLines").GetInt32());
        Assert.Equal(300, codeSize.GetProperty("extremeMemberCodeLines").GetInt32());
        Assert.Equal(300, codeSize.GetProperty("minTypeCodeLines").GetInt32());
        Assert.Equal(800, codeSize.GetProperty("extremeTypeCodeLines").GetInt32());
        Assert.Equal(1000, codeSize.GetProperty("extremeFileLines").GetInt32());
        Assert.Equal(131072, codeSize.GetProperty("extremeFileUtf8Bytes").GetInt32());
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

        Assert.Equal(new[] { "code-size-candidates", "dead-code-candidates", "duplicate-code-candidates", "fixture-finding", "indirection-drift-candidates", "method-control-flow-outliers", "missing-test-evidence-candidates", "non-ascii-identifiers", "structural-duplication-candidates" }, registry.Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));
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
