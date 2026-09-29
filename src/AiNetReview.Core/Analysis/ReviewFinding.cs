namespace AiNetReview.Core.Analysis;

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
    public bool IsChanged => ChangedSourcePaths.Count > 0;
}

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
