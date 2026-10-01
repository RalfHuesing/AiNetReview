namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using AiNetReview.Core.Findings;

internal static class ReviewFindingBuilder
{
    internal static IReadOnlyList<ReviewFinding> Build(
        IReadOnlyList<ReviewAnalysisRunResult> analyses,
        IReadOnlyList<SourceFileSnapshot> sourceFiles,
        IReadOnlyDictionary<string, string>? baselineFiles,
        IReadOnlyList<ProjectClassification> projectClassifications)
    {
        var projectRoles = projectClassifications.ToDictionary(static project => project.ProjectPath, static project => project.Role,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var changedPaths = new HashSet<string>(pathComparer);
        foreach (var sourceFile in sourceFiles)
        {
            if (baselineFiles is null
                || !baselineFiles.TryGetValue(sourceFile.Path, out var baselineHash)
                || !string.Equals(sourceFile.Sha256, baselineHash, StringComparison.OrdinalIgnoreCase))
            {
                changedPaths.Add(sourceFile.Path);
            }
        }

        var entries = analyses
            .SelectMany(analysis => analysis.Result.Findings.Select(finding => new FindingEntry(analysis.AnalysisId, finding)))
            .ToArray();
        var symbolGroups = new Dictionary<SymbolKey, List<FindingReference>>(new SymbolKeyComparer(pathComparer));
        foreach (var entry in entries)
        {
            foreach (var symbol in entry.Finding.RelatedSymbols)
            {
                var reference = new FindingReference(entry.AnalysisId, entry.Finding, symbol);
                var key = new SymbolKey(symbol.ProjectPath, symbol.SymbolId);
                if (!symbolGroups.TryGetValue(key, out var references))
                {
                    references = [];
                    symbolGroups.Add(key, references);
                }

                if (!references.Contains(reference, FindingReferenceComparer.Instance))
                {
                    references.Add(reference);
                }
            }
        }

        var result = new List<ReviewFinding>(entries.Length);
        foreach (var entry in entries)
        {
            var sourcePaths = entry.Finding.Evidence
                .Select(static evidence => evidence.SourcePath)
                .Append(entry.Finding.SourcePath)
                .Distinct(pathComparer)
                .OrderBy(static path => path, pathComparer)
                .ToArray();
            var related = entry.Finding.RelatedSymbols
                .SelectMany(symbol => symbolGroups[new SymbolKey(symbol.ProjectPath, symbol.SymbolId)])
                .Where(reference => reference.AnalysisId != entry.AnalysisId
                    || !ReferenceEquals(reference.Finding, entry.Finding))
                .Distinct(FindingReferenceComparer.Instance)
                .Select(static reference => new ReviewFindingReference(
                    reference.AnalysisId,
                    reference.Finding.ProjectPath,
                    reference.Finding.SourcePath,
                    reference.Finding.SubjectId,
                    reference.Finding.Discriminator,
                    reference.Symbol.SourcePath,
                    reference.Symbol.SymbolId,
                    reference.Symbol.Line))
                .OrderBy(static reference => reference.AnalysisId, StringComparer.Ordinal)
                .ThenBy(static reference => reference.ProjectPath, pathComparer)
                .ThenBy(static reference => reference.SourcePath, pathComparer)
                .ThenBy(static reference => reference.SubjectId, StringComparer.Ordinal)
                .ThenBy(static reference => reference.Discriminator, StringComparer.Ordinal)
                .ThenBy(static reference => reference.SymbolSourcePath, pathComparer)
                .ThenBy(static reference => reference.SymbolId, StringComparer.Ordinal)
                .ThenBy(static reference => reference.SymbolLine)
                .ToArray();
            var changedFindingPaths = sourcePaths.Where(changedPaths.Contains).ToArray();
            result.Add(new ReviewFinding(
                entry.AnalysisId,
                entry.Finding,
                Array.AsReadOnly(sourcePaths),
                Array.AsReadOnly(related),
                Array.AsReadOnly(changedFindingPaths))
            {
                Occurrences = Array.AsReadOnly(entry.Finding.RelatedSymbols
                    .Select(symbol => new ReviewFindingOccurrence(symbol, projectRoles[symbol.ProjectPath]))
                    .ToArray()),
            });
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private sealed record FindingEntry(string AnalysisId, FindingDraft Finding);

    private sealed record FindingReference(string AnalysisId, FindingDraft Finding, FindingSymbol Symbol);

    private readonly record struct SymbolKey(string ProjectPath, string SymbolId);

    private sealed class SymbolKeyComparer(StringComparer pathComparer) : IEqualityComparer<SymbolKey>
    {
        public bool Equals(SymbolKey left, SymbolKey right) =>
            pathComparer.Equals(left.ProjectPath, right.ProjectPath)
            && StringComparer.Ordinal.Equals(left.SymbolId, right.SymbolId);

        public int GetHashCode(SymbolKey value) =>
            HashCode.Combine(pathComparer.GetHashCode(value.ProjectPath), StringComparer.Ordinal.GetHashCode(value.SymbolId));
    }

    private sealed class FindingReferenceComparer : IEqualityComparer<FindingReference>
    {
        internal static FindingReferenceComparer Instance { get; } = new();

        public bool Equals(FindingReference? left, FindingReference? right) =>
            ReferenceEquals(left, right)
            || (left is not null && right is not null
                && left.AnalysisId == right.AnalysisId
                && ReferenceEquals(left.Finding, right.Finding)
                && left.Symbol.ProjectPath == right.Symbol.ProjectPath
                && left.Symbol.SourcePath == right.Symbol.SourcePath
                && left.Symbol.SymbolId == right.Symbol.SymbolId
                && left.Symbol.Line == right.Symbol.Line);

        public int GetHashCode(FindingReference value) => HashCode.Combine(
            value.AnalysisId,
            value.Finding,
            value.Symbol.ProjectPath,
            value.Symbol.SourcePath,
            value.Symbol.SymbolId,
            value.Symbol.Line);
    }
}
