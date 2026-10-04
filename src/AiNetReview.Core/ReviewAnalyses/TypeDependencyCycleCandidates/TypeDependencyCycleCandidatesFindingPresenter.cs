namespace AiNetReview.Core.ReviewAnalyses.TypeDependencyCycleCandidates;

using System.Collections.Generic;
using System.Linq;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;

internal sealed class TypeDependencyCycleCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(["\nSelection: A finding represents one maximal strongly connected component in the production type graph. It must contain at least three distinct types and at least three distinct canonical declaration source files; both floors are inclusive. Every internal directed edge and retained source witness is listed, along with every participating type declaration and one genuine deterministic cycle as an example. Counts use distinct type pairs and canonical declaration paths. Test, generated, metadata, dynamic, and implicit compiler-created types are outside the graph. This is a static dependency signal, not proof of runtime recursion or an architectural violation.\n"], SubjectScope: ReviewAnalysisSubjectScope.Production);

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        new(
        [
            new ReviewFindingReportTextBlock("Dependency group", Signal(finding.Finding)),
            new ReviewFindingReportTextBlock("Example cycle and review question", finding.Finding.Rationale),
            new ReviewFindingReportEvidenceBlock("Participating declarations and internal edge witnesses:", finding.Finding.Evidence
                .Select(static evidence => ReviewFindingPresentationFormatting.Evidence(evidence)).ToArray()),
        ], EvidenceIsRepresented: true);

    private static string Signal(AiNetReview.Core.Findings.FindingDraft finding) =>
        $"{ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "typeCount"))} production types in "
        + $"{ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "declarationFileCount"))} declaration files across "
        + $"{ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "projectCount"))} projects; "
        + $"{ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "internalEdgeCount"))} directed dependencies";
}
