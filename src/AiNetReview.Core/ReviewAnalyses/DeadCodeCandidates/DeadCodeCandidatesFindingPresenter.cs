namespace AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;

using System.Collections.Generic;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;

internal sealed class DeadCodeCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(Array.Empty<string>());

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        ReviewFindingPresentationFormatting.SignalOrRelatedSymbols(
            finding,
            finding.Finding.Discriminator == "type-candidate" ? "Type without known use" : "Method without known use");
}
