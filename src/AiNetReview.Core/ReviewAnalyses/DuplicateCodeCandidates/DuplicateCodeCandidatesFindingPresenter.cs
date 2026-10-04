namespace AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;

using System.Collections.Generic;
using System.Linq;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;

internal sealed class DuplicateCodeCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(["\nSimilarity presets: `exact` = 0.95, `near` = 0.80, and `fuzzy` = 0.65; `exact` is the strictest preset, not exact identity. Similarity is Jaccard over distinct fixed five-token n-gram sets from method bodies. Whitespace and comments are ignored; identifier and literal token text is retained, with no identifier or local-name normalization.\n"]);

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty)
    {
        if (finding.Finding.RelatedSymbols.Count <= 1)
        {
            return new ReviewFindingPresentation([new ReviewFindingReportTextBlock("Signal", Signal(finding.Finding))]);
        }

        var renderedSymbols = new HashSet<FindingSymbol>();
        var rows = new List<ReviewFindingReportEvidence>();
        foreach (var evidence in finding.Finding.Evidence)
        {
            var symbol = evidence.RelatedSymbol
                ?? finding.Finding.RelatedSymbols.FirstOrDefault(candidate =>
                    candidate.SourcePath == evidence.SourcePath && candidate.Line == evidence.Line
                    && (candidate.SymbolId == evidence.Label || evidence.Label == "Duplicate method"));
            if (symbol is not null) renderedSymbols.Add(symbol);
            var projectPath = symbol?.ProjectPath ?? finding.Finding.ProjectPath;
            var identity = symbol?.SymbolId ?? evidence.Label;
            rows.Add(new ReviewFindingReportEvidence(evidence.SourcePath, evidence.Line, identity, evidence.Detail, projectPath,
                ShowProjectPathWhenDifferent: true, IncludeOccurrenceRole: true));
        }

        rows.AddRange(finding.Finding.RelatedSymbols.Where(symbol => !renderedSymbols.Contains(symbol))
            .Select(static symbol => new ReviewFindingReportEvidence(symbol.SourcePath, symbol.Line, symbol.SymbolId,
                ProjectPath: symbol.ProjectPath, ShowProjectPathWhenDifferent: true, IncludeOccurrenceRole: true)));
        return new ReviewFindingPresentation(
        [
            new ReviewFindingReportTextBlock("Cluster", Signal(finding.Finding)),
            new ReviewFindingReportEvidenceBlock(null, rows),
        ], OmitSubjectLine: true, EvidenceIsRepresented: true);
    }

    private static string Signal(FindingDraft finding) =>
        $"{ReviewFindingPresentationFormatting.Number(ReviewFindingPresentationFormatting.Metric(finding, "memberCount"))} methods; "
        + $"{ReviewFindingPresentationFormatting.Percent(ReviewFindingPresentationFormatting.Metric(finding, "similarityScore"))} similarity (minimum "
        + $"{ReviewFindingPresentationFormatting.Percent(ReviewFindingPresentationFormatting.Metric(finding, "minimumSimilarityThreshold"))})";
}
