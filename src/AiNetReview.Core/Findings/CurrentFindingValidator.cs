namespace AiNetReview.Core.Findings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;

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

        var sourceDocuments = new Dictionary<string, List<SourceDocument>>(PathComparer);
        var projectPaths = new Dictionary<ProjectId, string>();
        foreach (var project in context.Solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw Invalid("A C# project has no project file path.");
            }

            projectPaths.Add(project.Id, context.GetProjectRelativePath(project.FilePath));
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(document.FilePath) || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = context.GetProjectRelativePath(document.FilePath);
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (!sourceDocuments.TryGetValue(relativePath, out var documents))
                {
                    documents = [];
                    sourceDocuments.Add(relativePath, documents);
                }

                documents.Add(new SourceDocument(project.Id, text));
            }
        }

        var uniqueKeys = new HashSet<FindingKey>();
        var validated = new List<FindingDraft>();
        foreach (var finding in findings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finding is null)
            {
                throw Invalid("A analysis returned a null finding.");
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

            if (!TryGetProjectOwnedSource(finding.ProjectPath, finding.SourcePath, sourceDocuments, projectPaths, out var source))
            {
                throw Invalid($"Finding source '{finding.SourcePath}' is not a C# document owned by '{finding.ProjectPath}' in the loaded solution.");
            }

            ValidateLine(finding.StartLine, source.Text.Lines.Count, "Finding startLine");
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
                if (!TryGetSolutionSources(evidence.SourcePath, sourceDocuments, out var evidenceSources))
                {
                    throw Invalid($"Evidence source '{evidence.SourcePath}' is not a C# document in the loaded solution.");
                }

                if (!evidenceSources.Any(candidate => MatchesEvidence(candidate.Text, evidence)))
                {
                    throw Invalid("Evidence line or snippet does not match the loaded source snapshot.");
                }
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

    private static bool TryGetProjectOwnedSource(
        string projectPath,
        string sourcePath,
        IReadOnlyDictionary<string, List<SourceDocument>> sourceDocuments,
        IReadOnlyDictionary<ProjectId, string> projectPaths,
        out SourceDocument source)
    {
        source = null!;
        if (!IsCanonicalRelativePath(projectPath) || !IsCanonicalRelativePath(sourcePath)
            || !sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || !sourceDocuments.TryGetValue(sourcePath, out var documents))
        {
            return false;
        }

        foreach (var document in documents)
        {
            if (PathComparer.Equals(projectPaths[document.ProjectId], projectPath))
            {
                source = document;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSolutionSources(
        string sourcePath,
        IReadOnlyDictionary<string, List<SourceDocument>> sourceDocuments,
        out IReadOnlyList<SourceDocument> sources)
    {
        if (IsCanonicalRelativePath(sourcePath)
            && sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && sourceDocuments.TryGetValue(sourcePath, out var documents))
        {
            sources = documents;
            return true;
        }

        sources = Array.Empty<SourceDocument>();
        return false;
    }

    private static bool MatchesEvidence(Microsoft.CodeAnalysis.Text.SourceText text, FindingEvidence evidence)
    {
        if (evidence.Line < 1 || evidence.Line > text.Lines.Count)
        {
            return false;
        }

        return text.Lines[evidence.Line - 1].ToString().Contains(evidence.Snippet, StringComparison.Ordinal);
    }

    private static bool IsCanonicalRelativePath(string path) =>
        !Path.IsPathRooted(path)
        && !path.Contains('\\')
        && path.Split('/').All(static segment => segment.Length > 0 && segment is not "." and not "..");

    private static void ValidateLine(int line, int lineCount, string name)
    {
        if (line < 1 || line > lineCount)
        {
            throw Invalid($"{name} must refer to a line in the loaded source text.");
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

    private sealed record SourceDocument(ProjectId ProjectId, Microsoft.CodeAnalysis.Text.SourceText Text);
}
