namespace AiNetReview.Core.Findings;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>Validates and orders findings against the loaded source snapshot for one run.</summary>
public sealed class CurrentFindingValidator
{
    public async Task<IReadOnlyList<FindingDraft>> ValidateAndSortAsync(
        string analysisId,
        ReviewContext context,
        IEnumerable<FindingDraft> findings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(findings);

        var sourceIndex = await context.GetFindingSourceIndexAsync(cancellationToken).ConfigureAwait(false);

        var uniqueKeys = new HashSet<FindingKey>();
        var validated = new List<FindingDraft>();
        foreach (var finding in findings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finding is null)
            {
                throw Invalid("An analysis returned a null finding.");
            }

            RequireText(finding.ProjectPath, "projectPath");
            RequireText(finding.SourcePath, "sourcePath");
            RequireText(finding.SubjectId, "subjectId");
            RequireText(finding.Discriminator, "discriminator");
            RequireText(finding.Rationale, "rationale");
            if (finding.StartLine < 1)
            {
                throw Invalid("Finding startLine must be a positive, one-based line number.");
            }

            if (!sourceIndex.TryGetProjectOwnedSource(finding.ProjectPath, finding.SourcePath, out var sourceText))
            {
                throw Invalid($"Finding source '{finding.SourcePath}' is not a C# document owned by '{finding.ProjectPath}' in the loaded solution.");
            }

            ValidateLine(finding.StartLine, sourceText.Text.Lines.Count, "Finding startLine");
            if (finding.Metrics is null || finding.Metrics.Any(static pair => string.IsNullOrWhiteSpace(pair.Key) || !double.IsFinite(pair.Value)))
            {
                throw Invalid("Finding metrics must have non-empty keys and finite numeric values.");
            }

            if (finding.Evidence is null || finding.Evidence.Count == 0)
            {
                throw Invalid("Each finding must contain at least one evidence item.");
            }

            foreach (var evidence in finding.Evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evidence is null)
                {
                    throw Invalid("Finding evidence cannot contain null values.");
                }

                RequireText(evidence.SourcePath, "evidence.sourcePath");
                RequireText(evidence.Label, "evidence.label");
                RequireText(evidence.Detail, "evidence.detail");
                RequireText(evidence.Snippet, "evidence.snippet");
                if (!sourceIndex.TryGetSolutionSources(evidence.SourcePath, out var evidenceSources))
                {
                    throw Invalid($"Evidence source '{evidence.SourcePath}' is not a C# document in the loaded solution.");
                }

                if (!evidenceSources.Any(candidate => MatchesEvidence(candidate.Text, evidence)))
                {
                    throw Invalid("Evidence line or snippet does not match the loaded source snapshot.");
                }

                if (evidence.SourceRange is { } sourceRange)
                {
                    if (!sourceIndex.TryGetProjectOwnedSource(sourceRange.ProjectPath, evidence.SourcePath, out var rangeSource))
                    {
                        throw Invalid("Finding evidence source range does not identify a loaded C# document owned by its project.");
                    }

                    if (!MatchesEvidence(rangeSource.Text, evidence))
                    {
                        throw Invalid("Finding evidence snippet does not match the source document named by its source range.");
                    }

                    ValidateSourceRange(sourceRange, evidence.Line, rangeSource.Text);
                }

                if (evidence.RelatedSymbol is { } evidenceSymbol)
                {
                    if (finding.RelatedSymbols?.Contains(evidenceSymbol) != true
                        || !PathComparer.Equals(evidenceSymbol.SourcePath, evidence.SourcePath)
                        || evidenceSymbol.Line != evidence.Line
                        || string.IsNullOrWhiteSpace(evidenceSymbol.SymbolId)
                        || !sourceIndex.TryGetProjectOwnedSource(evidenceSymbol.ProjectPath, evidenceSymbol.SourcePath, out var relatedSource))
                    {
                        throw Invalid("Finding evidence related symbol must identify a loaded related symbol at the evidence source and line.");
                    }

                    ValidateLine(evidenceSymbol.Line, relatedSource.Text.Lines.Count, "Finding evidence related symbol line");
                }
            }

            if (finding.RelatedSymbols is null || finding.RelatedSymbols.Count == 0)
            {
                throw Invalid("Each finding must identify at least one source symbol.");
            }

            foreach (var symbol in finding.RelatedSymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (symbol is null
                    || string.IsNullOrWhiteSpace(symbol.SymbolId)
                    || !sourceIndex.TryGetProjectOwnedSource(symbol.ProjectPath, symbol.SourcePath, out var symbolSource))
                {
                    throw Invalid("Finding related symbols must identify a loaded C# source declaration in their project.");
                }

                ValidateLine(symbol.Line, symbolSource.Text.Lines.Count, "Finding related symbol line");
            }

            if (finding.SubjectSymbols is null || finding.SubjectSymbols.Count == 0
                || finding.SubjectSymbols.Any(symbol => !finding.RelatedSymbols.Contains(symbol)))
            {
                throw Invalid("Each finding must identify at least one subject symbol from its validated related symbols.");
            }

            var key = new FindingKey(analysisId, finding.ProjectPath, finding.SourcePath, finding.SubjectId, finding.Discriminator);
            if (!uniqueKeys.Add(key))
            {
                throw Invalid("Finding identity tuple (analysisId, projectPath, sourcePath, subjectId, discriminator) must be unique within a run.");
            }

            validated.Add(finding);
        }

        return Array.AsReadOnly(validated
            .OrderBy(static finding => finding.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.SourcePath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.StartLine)
            .ThenBy(static finding => finding.SubjectId, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Discriminator, StringComparer.Ordinal)
            .ToArray());
    }

    private static bool MatchesEvidence(SourceText text, FindingEvidence evidence)
    {
        if (evidence.Line < 1 || evidence.Line > text.Lines.Count)
        {
            return false;
        }

        return text.Lines[evidence.Line - 1].ToString().Contains(evidence.Snippet, StringComparison.Ordinal);
    }

    private static void ValidateLine(int line, int lineCount, string name)
    {
        if (line < 1 || line > lineCount)
        {
            throw Invalid($"{name} must refer to a line in the loaded source text.");
        }
    }

    private static void ValidateSourceRange(FindingSourceRange range, int evidenceLine, SourceText source)
    {
        if (range.StartLine != evidenceLine
            || range.StartLine < 1
            || range.EndLine < range.StartLine
            || range.EndLine > source.Lines.Count
            || range.StartColumn < 1
            || range.EndColumn < 1)
        {
            throw Invalid("Finding evidence source range must use valid one-based lines and columns and begin on its evidence line.");
        }

        var startLine = source.Lines[range.StartLine - 1];
        var endLine = source.Lines[range.EndLine - 1];
        if (range.StartColumn > startLine.Span.Length + 1
            || range.EndColumn > endLine.Span.Length + 1
            || (range.StartLine == range.EndLine && range.EndColumn < range.StartColumn))
        {
            throw Invalid("Finding evidence source range columns must refer to positions in the loaded source lines.");
        }
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid($"Finding {name} must be non-empty.");
        }
    }

    private static AnalysisFailedException Invalid(string message) => new(message);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly record struct FindingKey(string AnalysisId, string ProjectPath, string SourcePath, string SubjectId, string Discriminator);
}
