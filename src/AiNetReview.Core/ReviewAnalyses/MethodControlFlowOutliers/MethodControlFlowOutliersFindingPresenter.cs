namespace AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;

using System.Collections.Generic;
using System.Text;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;

internal sealed class MethodControlFlowOutliersFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings)
    {
        var notes = new List<string>
        {
            ReviewFindingPresentationFormatting.ControlFlowCountingNote,
            "\nSelection: Within each C# project, decision-count and maximum-nesting populations have separate nearest-rank values at the effective `percentile`. Inclusive cutoffs are `max(8, decision percentile)` and `max(4, nesting percentile)`. A method is selected when `decisionCount >= decision cutoff AND decisionConstructCount >= 2`, or `maxDecisionNesting >= nesting cutoff`. Test projects use effective test options shown above.\n",
        };
        return new ReviewAnalysisPresentation(notes);
    }

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty)
    {
        var item = finding.Finding;
        var signal = new StringBuilder();
        var decisions = ReviewFindingPresentationFormatting.Metric(item, "decisionCount");
        var decisionCutoff = ReviewFindingPresentationFormatting.Metric(item, "decisionCutoff");
        var constructs = ReviewFindingPresentationFormatting.Metric(item, "decisionConstructCount");
        if (decisions >= decisionCutoff && constructs >= 2)
        {
            signal.Append(ReviewFindingPresentationFormatting.Number(decisions)).Append(" decisions across ")
                .Append(ReviewFindingPresentationFormatting.Number(constructs)).Append(" constructs (cutoff ")
                .Append(ReviewFindingPresentationFormatting.Number(decisionCutoff)).Append(')');
        }

        var nesting = ReviewFindingPresentationFormatting.Metric(item, "maxDecisionNesting");
        var nestingCutoff = ReviewFindingPresentationFormatting.Metric(item, "nestingCutoff");
        if (nesting >= nestingCutoff)
        {
            if (signal.Length > 0) signal.Append("; ");
            signal.Append("nesting ").Append(ReviewFindingPresentationFormatting.Number(nesting)).Append(" (cutoff ")
                .Append(ReviewFindingPresentationFormatting.Number(nestingCutoff)).Append(')');
        }

        return ReviewFindingPresentationFormatting.SignalOrRelatedSymbols(finding, signal.ToString());
    }
}
