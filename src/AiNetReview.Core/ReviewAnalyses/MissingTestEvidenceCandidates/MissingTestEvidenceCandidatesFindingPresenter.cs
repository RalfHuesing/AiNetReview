namespace AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;

internal sealed class MissingTestEvidenceCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings)
    {
        var allUncertain = findings.Count > 0 && findings.All(static item =>
            ReviewFindingPresentationFormatting.Metric(item.Finding, "attributionUncertain") > 0);
        return new ReviewAnalysisPresentation(
        [
            allUncertain ? "\nAttribution uncertain: every finding in this report has uncertain static test attribution.\n" : string.Empty,
            ReviewFindingPresentationFormatting.ControlFlowCountingNote,
            "\nSelection: A production function meets the nontrivial gate when `decisionCount >= minDecisionCount OR maxDecisionNesting >= minDecisionNesting`. A direct resolved static test path suppresses a finding. A function with no resolved static test path is reported at the nontrivial gate; an indirect-path-only function must also meet `decisionCount >= minIndirectDecisionCount OR maxDecisionNesting >= minIndirectDecisionNesting`.\n\nThis is static test-path evidence from the loaded snapshot, not runtime coverage. The `attribution uncertain` marker means the static test association may be incomplete; it can result from reachable unresolved bindings, method groups, or virtual/interface dispatch, and may propagate to downstream methods over known calls. A reachable global uncertainty input can mark every function, so the marker alone neither means a test is missing nor that the marked function itself has an unresolved binding. The report does not identify which specific input caused a marker; markers on every finding do not by themselves prove a global uncertainty input. It does not assess test assertion quality. Reflection, dependency injection, external test projects, dynamic dispatch, branch execution, and custom test discovery can hide associations.\n",
        ], SuppressAttributionUncertainty: allUncertain, SubjectScope: ReviewAnalysisSubjectScope.ProductionWithTestEvidence);
    }

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty)
    {
        var item = finding.Finding;
        var category = item.Discriminator switch
        {
            "no-static-test-path" => "no static test path",
            "indirect-test-path-only" => "indirect test path only",
            _ => item.Discriminator,
        };
        var signal = new StringBuilder(category)
            .Append("; ").Append(ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(item, "decisionCount")))
            .Append(" decisions, nesting ").Append(ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(item, "maxDecisionNesting")));
        if (ReviewFindingPresentationFormatting.Metric(item, "attributionUncertain") > 0 && !suppressAttributionUncertainty)
        {
            signal.Append("; attribution uncertain");
        }

        var blocks = new List<ReviewFindingReportBlock> { new ReviewFindingReportTextBlock("Signal", signal.ToString()) };
        if (item.Discriminator == "indirect-test-path-only" && item.Evidence.Count > 1)
        {
            blocks.Add(new ReviewFindingReportPathBlock("Shortest resolved test path", item.Evidence.Skip(1)
                .Select(evidence => new ReviewFindingReportPathItem(evidence.Label, evidence.SourcePath, evidence.Line, IncludeOccurrenceRole: true)).ToArray()));
        }

        return new ReviewFindingPresentation(blocks, EvidenceIsRepresented: true);
    }
}
