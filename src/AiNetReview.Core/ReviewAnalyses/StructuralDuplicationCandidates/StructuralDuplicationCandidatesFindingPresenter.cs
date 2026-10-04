namespace AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;

using System.Collections.Generic;
using System.Linq;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;

internal sealed class StructuralDuplicationCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(["\nContainment suppression removes a smaller fragment only when every occurrence is contained in an occurrence of a larger qualifying group; a smaller group with any additional occurrence remains reportable.\n"]);

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        new(
        [
            new ReviewFindingReportTextBlock("Structural duplicate", Signal(finding.Finding)),
            new ReviewFindingReportEvidenceBlock(null, finding.Finding.Evidence.Select(static evidence =>
            {
                if (evidence.SourceRange is null)
                {
                    throw new InvalidOperationException("Structural duplicate evidence must include a typed source range.");
                }

                return new ReviewFindingReportEvidence(evidence.SourcePath, evidence.Line, evidence.Label,
                    SourceRange: evidence.SourceRange, ProjectPath: evidence.SourceRange.ProjectPath,
                    ShowProjectPathWhenDifferent: true, ShowSourcePathWhenDifferent: true, IncludeOccurrenceRole: true);
            }).ToArray()),
        ], OmitSubjectLine: true, EvidenceIsRepresented: true);

    private static string Signal(FindingDraft finding) =>
        ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "memberCount"))
        + " occurrences in "
        + ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "executableCount"))
        + " executable members; "
        + ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "statementCount"))
        + " statements / "
        + ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "tokenCount"))
        + " tokens; identical after local/parameter normalization.";
}
