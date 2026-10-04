namespace AiNetReview.Core.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;

/// <summary>Analysis-owned information used to write one analysis report.</summary>
public enum ReviewAnalysisSubjectScope
{
    ProductionAndTests,
    ProductionWithTestEvidence,
    Production,
}

public sealed record ReviewAnalysisPresentation(
    IReadOnlyList<string> MarkdownNotes,
    bool SuppressAttributionUncertainty = false,
    ReviewAnalysisSubjectScope SubjectScope = ReviewAnalysisSubjectScope.ProductionAndTests);

/// <summary>Report blocks describing one finding; the report writer owns Markdown layout and escaping.</summary>
public sealed record ReviewFindingPresentation(
    IReadOnlyList<ReviewFindingReportBlock> Blocks,
    bool OmitSubjectLine = false,
    bool EvidenceIsRepresented = false);

public abstract record ReviewFindingReportBlock;

public sealed record ReviewFindingReportTextBlock(string Label, string Text) : ReviewFindingReportBlock;

public sealed record ReviewFindingReportEvidenceBlock(
    string? Heading,
    IReadOnlyList<ReviewFindingReportEvidence> Items) : ReviewFindingReportBlock;

public sealed record ReviewFindingReportSymbolsBlock(IReadOnlyList<ReviewFindingReportSymbol> Items) : ReviewFindingReportBlock;

public sealed record ReviewFindingReportPathBlock(
    string Label,
    IReadOnlyList<ReviewFindingReportPathItem> Items) : ReviewFindingReportBlock;

public sealed record ReviewFindingReportEvidence(
    string SourcePath,
    int Line,
    string Label,
    string? Detail = null,
    string? ProjectPath = null,
    FindingSourceRange? SourceRange = null,
    bool ShowProjectPathWhenDifferent = false,
    bool ShowSourcePathWhenDifferent = false,
    bool IncludeOccurrenceRole = false);

public sealed record ReviewFindingReportSymbol(
    FindingSymbol Symbol,
    bool ShowProjectPathWhenDifferent = false,
    bool IncludeOccurrenceRole = false);

public sealed record ReviewFindingReportPathItem(string Label, string SourcePath, int Line, bool IncludeOccurrenceRole);

public interface IReviewFindingPresenter
{
    ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings);

    ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty);
}

/// <summary>Formatting-neutral helpers for report content produced by built-in analyses.</summary>
internal static class ReviewFindingPresentationFormatting
{
    internal const string ControlFlowCountingNote = "\nControl-flow counting: `decisionCount` counts each `if`, conditional expression, loop, and `catch` once, and each switch section or switch-expression arm once. `decisionConstructCount` counts each `if`, conditional expression, loop, and `catch` once and each entire switch once. Nesting is the maximum depth of counted decisions (`else if` chains stay at the same depth). Operators such as `&&`, `||`, and `??` do not add decisions; these measures are not cyclomatic complexity.\n";

    internal static double Metric(FindingDraft finding, string name) =>
        finding.Metrics.TryGetValue(name, out var value) ? value : 0;

    internal static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string Percent(double value) => (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    internal static ReviewFindingReportEvidence Evidence(FindingEvidence evidence, bool includeRole = false) =>
        new(evidence.SourcePath, evidence.Line, evidence.Label, evidence.Detail, evidence.RelatedSymbol?.ProjectPath,
            evidence.SourceRange, ShowProjectPathWhenDifferent: evidence.RelatedSymbol is not null, IncludeOccurrenceRole: includeRole);

    internal static ReviewFindingReportSymbol Symbol(FindingSymbol symbol, bool includeRole = true) =>
        new(symbol, ShowProjectPathWhenDifferent: true, IncludeOccurrenceRole: includeRole);

    internal static ReviewFindingPresentation SignalOrRelatedSymbols(
        ReviewFinding finding,
        string signal,
        bool omitSubjectLine = false)
    {
        if (finding.Finding.RelatedSymbols.Count > 1)
        {
            return new ReviewFindingPresentation(
            [
                new ReviewFindingReportTextBlock("Cluster", signal),
                new ReviewFindingReportSymbolsBlock(finding.Finding.RelatedSymbols
                    .Select(static symbol => Symbol(symbol)).ToArray()),
            ], OmitSubjectLine: omitSubjectLine);
        }

        return new ReviewFindingPresentation(
            [new ReviewFindingReportTextBlock("Signal", signal)], OmitSubjectLine: omitSubjectLine);
    }

    internal static string FormatSymbolIdentity(string symbolId)
    {
        var separator = symbolId.IndexOf(':');
        if (separator != 1 || symbolId.Length < 3)
        {
            return symbolId;
        }

        var kind = symbolId[0];
        if (kind is not ('M' or 'T' or 'P' or 'F' or 'E' or '!'))
        {
            return symbolId;
        }

        var identity = symbolId[(separator + 1)..];
        var returnTypeStart = identity.IndexOf('~');
        if (returnTypeStart >= 0)
        {
            identity = identity[..returnTypeStart];
        }

        var signature = identity.IndexOf('(');
        if (signature >= 0)
        {
            identity = identity[..signature];
        }

        identity = identity.Replace('+', '.');
        var parts = identity.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => symbolId,
            1 => parts[0],
            _ => parts[^2] + "." + parts[^1],
        };
    }
}

internal sealed class DefaultReviewFindingPresenter : IReviewFindingPresenter
{
    internal static DefaultReviewFindingPresenter Instance { get; } = new();

    private DefaultReviewFindingPresenter() { }

    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(Array.Empty<string>());

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty)
    {
        if (finding.Finding.RelatedSymbols.Count > 1)
        {
            return new ReviewFindingPresentation(
            [
                new ReviewFindingReportTextBlock("Cluster", finding.Finding.Rationale),
                new ReviewFindingReportSymbolsBlock(finding.Finding.RelatedSymbols
                    .Select(static symbol => ReviewFindingPresentationFormatting.Symbol(symbol)).ToArray()),
            ]);
        }

        return new ReviewFindingPresentation([new ReviewFindingReportTextBlock("Signal", finding.Finding.Rationale)]);
    }
}
