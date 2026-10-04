namespace AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;

using System.Collections.Generic;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;

internal sealed class NonAsciiIdentifiersFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(Array.Empty<string>());

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        ReviewFindingPresentationFormatting.SignalOrRelatedSymbols(finding, finding.Finding.Rationale);
}
