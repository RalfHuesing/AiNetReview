namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses;

public sealed class ReviewAnalysisRegistryTests
{
    [Fact]
    public void Constructor_RejectsDuplicateAnalysisIds()
    {
        var first = new TestAnalysis("duplicate-analysis");
        var second = new TestAnalysis("duplicate-analysis");

        var exception = Assert.Throws<ArgumentException>(() => new ReviewAnalysisRegistry([first, second]));

        Assert.Contains("Duplicate analysis ID 'duplicate-analysis'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyses_AreSortedByIdAndCanBeResolved()
    {
        var alpha = new TestAnalysis("alpha-analysis");
        var zeta = new TestAnalysis("zeta-analysis");
        var registry = new ReviewAnalysisRegistry([zeta, alpha]);

        Assert.Equal(new[] { "alpha-analysis", "zeta-analysis" }, registry.Analyses.Select(static analysis => analysis.Descriptor.AnalysisId));
        Assert.Same(alpha, registry.GetRequired("alpha-analysis"));
        Assert.True(registry.TryGet("zeta-analysis", out var resolved));
        Assert.Same(zeta, resolved);
        Assert.False(registry.TryGet("missing-analysis", out _));
        Assert.Throws<KeyNotFoundException>(() => registry.GetRequired("missing-analysis"));
    }

    private sealed class TestAnalysis : IReviewAnalysis
    {
        internal TestAnalysis(string analysisId)
        {
            Descriptor = new ReviewAnalysisDescriptor(
                analysisId,
                "Test Review analysis",
                1,
                "Test purpose.",
                "Test measurement.",
                ["Is this test analysis registered?"]);
        }

        public ReviewAnalysisDescriptor Descriptor { get; }

        public Task<ReviewAnalysisResult> ExecuteAsync(
            ReviewContext context,
            ReviewAnalysisOptions options,
            CancellationToken cancellationToken) => Task.FromResult(ReviewAnalysisResult.Empty);
    }
}
