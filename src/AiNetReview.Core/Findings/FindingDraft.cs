namespace AiNetReview.Core.Findings;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public sealed class FindingDraft
{
    public FindingDraft(
        string projectPath,
        string sourcePath,
        string subjectId,
        string discriminator,
        int startLine,
        string rationale,
        IReadOnlyDictionary<string, double> metrics,
        IEnumerable<FindingEvidence> evidence,
        IEnumerable<FindingSymbol>? relatedSymbols = null,
        IEnumerable<FindingSymbol>? subjectSymbols = null)
    {
        ProjectPath = projectPath;
        SourcePath = sourcePath;
        SubjectId = subjectId;
        Discriminator = discriminator;
        StartLine = startLine;
        Rationale = rationale;
        Metrics = new ReadOnlyDictionary<string, double>(new SortedDictionary<string, double>(metrics.ToDictionary(static pair => pair.Key, static pair => pair.Value), System.StringComparer.Ordinal));
        Evidence = Array.AsReadOnly(evidence.ToArray());
        var symbols = (relatedSymbols ?? [new FindingSymbol(projectPath, sourcePath, subjectId, startLine)])
            .Distinct()
            .OrderBy(static symbol => symbol.ProjectPath, System.StringComparer.Ordinal)
            .ThenBy(static symbol => symbol.SourcePath, System.StringComparer.Ordinal)
            .ThenBy(static symbol => symbol.SymbolId, System.StringComparer.Ordinal)
            .ThenBy(static symbol => symbol.Line)
            .ThenBy(static symbol => symbol.OccurrenceId, System.StringComparer.Ordinal)
            .ToArray();
        RelatedSymbols = Array.AsReadOnly(symbols);
        SubjectSymbols = Array.AsReadOnly((subjectSymbols ?? symbols)
            .Distinct()
            .OrderBy(static symbol => symbol.ProjectPath, System.StringComparer.Ordinal)
            .ThenBy(static symbol => symbol.SourcePath, System.StringComparer.Ordinal)
            .ThenBy(static symbol => symbol.SymbolId, System.StringComparer.Ordinal)
            .ThenBy(static symbol => symbol.Line)
            .ThenBy(static symbol => symbol.OccurrenceId, System.StringComparer.Ordinal)
            .ToArray());
    }

    public string ProjectPath { get; }

    public string SourcePath { get; }

    public string SubjectId { get; }

    public string Discriminator { get; }

    public int StartLine { get; }

    public string Rationale { get; }

    public IReadOnlyDictionary<string, double> Metrics { get; }

    public IReadOnlyList<FindingEvidence> Evidence { get; }

    /// <summary>Related symbols used for cluster display and cross-analysis relationships, including contextual symbols.</summary>
    public IReadOnlyList<FindingSymbol> RelatedSymbols { get; }

    /// <summary>Symbols that determine this finding's report area; evidence and context symbols are excluded.</summary>
    public IReadOnlyList<FindingSymbol> SubjectSymbols { get; }

}

public sealed record FindingEvidence(
    string SourcePath,
    int Line,
    string Label,
    string Detail,
    string Snippet,
    FindingSymbol? RelatedSymbol = null,
    FindingSourceRange? SourceRange = null,
    bool OmitWhenRedundantWithSubject = false);

/// <summary>A one-based, end-exclusive source range associated with evidence.</summary>
public sealed record FindingSourceRange(string ProjectPath, int StartLine, int StartColumn, int EndLine, int EndColumn);

/// <param name="OccurrenceId">Optional range identity for multiple occurrences of one symbol; it does not change symbol identity.</param>
public sealed record FindingSymbol(string ProjectPath, string SourcePath, string SymbolId, int Line, string? OccurrenceId = null);
