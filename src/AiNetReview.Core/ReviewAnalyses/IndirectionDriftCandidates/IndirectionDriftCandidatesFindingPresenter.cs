namespace AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;

using System.Collections.Generic;
using System.Linq;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;

internal sealed class IndirectionDriftCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(Array.Empty<string>());

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        new(
        [
            new ReviewFindingReportTextBlock("Forwarding path", Signal(finding.Finding)),
            new ReviewFindingReportEvidenceBlock(null, finding.Finding.Evidence
                .Select(static evidence => new ReviewFindingReportEvidence(evidence.SourcePath, evidence.Line, evidence.Label,
                    IncludeOccurrenceRole: true)).ToArray()),
        ], OmitSubjectLine: true, EvidenceIsRepresented: true);

    private static string Signal(AiNetReview.Core.Findings.FindingDraft finding) =>
        ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "forwardingEdgeCount"))
        + " forwarding edges across "
        + ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "distinctTypeCount"))
        + " types and "
        + ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "distinctFileCount"))
        + " files";
}
