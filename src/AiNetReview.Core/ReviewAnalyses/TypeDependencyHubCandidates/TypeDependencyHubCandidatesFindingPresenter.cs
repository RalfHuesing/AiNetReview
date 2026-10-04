namespace AiNetReview.Core.ReviewAnalyses.TypeDependencyHubCandidates;

using System.Collections.Generic;
using System.Linq;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;

internal sealed class TypeDependencyHubCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(["\nSelection: A production type is reported when its distinct direct production consumer count meets `minFanIn` **and** its distinct direct production dependency count meets `minFanOut`; both inclusive thresholds default to 10 and are configured independently. Neighbor file counts use distinct canonical declaration paths, while project counts retain project-specific types. Every direct production neighbor and retained edge witness is listed. Direct test consumers are separate context and do not affect production counts or finding origin. Generated, metadata, dynamic, and implicit compiler-created types are excluded. This static neighborhood does not prove responsibility concentration or runtime behavior.\n"], SubjectScope: ReviewAnalysisSubjectScope.Production);

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        new(
        [
            new ReviewFindingReportTextBlock("Dependency hub", Signal(finding.Finding)),
            new ReviewFindingReportTextBlock("Neighborhood and review question", finding.Finding.Rationale),
            new ReviewFindingReportEvidenceBlock("Type declarations, direct production neighbors, and separate test consumers:", finding.Finding.Evidence
                .Select(static evidence => ReviewFindingPresentationFormatting.Evidence(evidence)).ToArray()),
        ], EvidenceIsRepresented: true);

    private static string Signal(AiNetReview.Core.Findings.FindingDraft finding) =>
        $"{N(finding, "fanIn")} production consumer types (minimum {N(finding, "minFanIn")}; {N(finding, "fanInNeighborFileCount")} files / {N(finding, "fanInNeighborProjectCount")} projects) "
        + $"and {N(finding, "fanOut")} production dependency types (minimum {N(finding, "minFanOut")}; {N(finding, "fanOutNeighborFileCount")} files / {N(finding, "fanOutNeighborProjectCount")} projects); {N(finding, "testConsumerCount")} direct test consumer types separately";

    private static string N(AiNetReview.Core.Findings.FindingDraft finding, string metric) =>
        ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, metric));
}
