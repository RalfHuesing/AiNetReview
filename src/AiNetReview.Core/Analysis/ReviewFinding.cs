namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using AiNetReview.Core.Findings;

/// <summary>A validated finding together with the files and other findings it relates to.</summary>
public sealed record ReviewFinding(
    string AnalysisId,
    FindingDraft Finding,
    IReadOnlyList<string> SourcePaths,
    IReadOnlyList<ReviewFindingReference> RelatedFindings,
    IReadOnlyList<string> ChangedSourcePaths)
{
    /// <summary>Project roles of all represented symbols; evidence files do not affect finding origin.</summary>
    public IReadOnlyList<ReviewFindingOccurrence> Occurrences { get; init; } = Array.Empty<ReviewFindingOccurrence>();

    public bool IsChanged => ChangedSourcePaths.Count > 0;
}

/// <summary>A represented symbol or occurrence together with its centrally classified project role.</summary>
public sealed record ReviewFindingOccurrence(AiNetReview.Core.Findings.FindingSymbol Symbol, ProjectRole Role);

/// <summary>A stable identity for a finding in another analysis.</summary>
public sealed record ReviewFindingReference(
    string AnalysisId,
    string ProjectPath,
    string SourcePath,
    string SubjectId,
    string Discriminator,
    string SymbolSourcePath,
    string SymbolId,
    int SymbolLine);
