namespace AiNetReview.Core.Findings;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using Microsoft.CodeAnalysis;

public sealed class FindingObservationFactory
{
    private readonly FingerprintService fingerprintService;

    public FindingObservationFactory(FingerprintService fingerprintService)
    {
        this.fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
    }

    public FindingObservation Create(ReviewConfig config, Solution solution, ConfiguredRule rule, FindingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(draft);
        RequireText(rule.RuleId, "ruleId");
        RequireText(draft.ProjectPath, "projectPath");
        RequireText(draft.SourcePath, "sourcePath");
        RequireText(draft.SubjectId, "subjectId");
        RequireText(draft.Discriminator, "discriminator");
        RequireText(draft.Rationale, "rationale");
        RequireText(draft.Snapshot, "snapshot");
        RequireText(draft.ComparisonText, "comparisonText");
        if (draft.StartLine <= 0 || draft.FingerprintVersion <= 0)
        {
            throw new InvalidReviewInputException("Finding line and fingerprint version must be positive.");
        }

        var projectPath = ResolveRelativePath(config.ProjectRoot, draft.ProjectPath, ".csproj", "projectPath");
        var sourcePath = ResolveRelativePath(config.ProjectRoot, draft.SourcePath, ".cs", "sourcePath");
        var projectAbsolutePath = ProjectPathResolver.ResolveRelative(config.ProjectRoot, projectPath, "projectPath");
        var sourceAbsolutePath = ProjectPathResolver.ResolveRelative(config.ProjectRoot, sourcePath, "sourcePath");
        var ownerProject = solution.Projects.FirstOrDefault(project => project.FilePath is not null &&
            PathComparer.Equals(ProjectPathResolver.Canonicalize(project.FilePath), projectAbsolutePath));
        if (ownerProject is null || !ownerProject.Documents.Any(document => document.FilePath is not null &&
                PathComparer.Equals(ProjectPathResolver.Canonicalize(document.FilePath), sourceAbsolutePath)))
        {
            throw new InvalidReviewInputException("Finding projectPath and sourcePath must identify a source document in the loaded solution.");
        }

        if (draft.SourceFiles.Count == 0)
        {
            throw new InvalidReviewInputException("Finding sourceFiles must not be empty.");
        }

        var sourceFiles = draft.SourceFiles
            .Select(path => ResolveRelativePath(config.ProjectRoot, path, ".cs", "sourceFiles"))
            .Distinct(PathComparer)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (sourceFiles.Length != draft.SourceFiles.Count || !sourceFiles.Contains(sourcePath, PathComparer))
        {
            throw new InvalidReviewInputException("Finding sourceFiles must be unique and include sourcePath.");
        }

        if (draft.Evidence.Count == 0)
        {
            throw new InvalidReviewInputException("Finding evidence must not be empty.");
        }

        var evidence = draft.Evidence.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Line <= 0)
            {
                throw new InvalidReviewInputException("Evidence lines must be positive.");
            }

            var evidencePath = ResolveRelativePath(config.ProjectRoot, item.SourcePath, ".cs", "evidence.sourcePath");
            if (!sourceFiles.Contains(evidencePath, PathComparer))
            {
                throw new InvalidReviewInputException("Evidence sourcePath must be listed in sourceFiles.");
            }

            RequireText(item.Label, "evidence.label");
            RequireText(item.Detail, "evidence.detail");
            RequireText(item.Snippet, "evidence.snippet");
            return item with { SourcePath = evidencePath };
        }).OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Label, StringComparer.Ordinal)
            .ThenBy(static item => item.Snippet, StringComparer.Ordinal)
            .ToArray();

        var metrics = draft.Metrics.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        if (metrics.Any(static pair => string.IsNullOrWhiteSpace(pair.Key) || !double.IsFinite(pair.Value)))
        {
            throw new InvalidReviewInputException("Finding metric keys must be nonempty and values must be finite numbers.");
        }

        var fileHashes = sourceFiles.Select(path => new FindingFileHash(
                path,
                "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(config.ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar))))).ToLowerInvariant()))
            .ToArray();
        var snapshot = draft.Snapshot.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var identity = new FindingIdentity(rule.RuleId, projectPath, sourcePath, draft.SubjectId, draft.Discriminator);
        return new FindingObservation(
            string.Empty,
            identity,
            draft.StartLine,
            draft.FingerprintVersion,
            fingerprintService.Compute(draft.FingerprintVersion, draft.ComparisonText),
            rule.Rule.Descriptor.BehaviorVersion,
            rule.EffectiveOptions,
            draft.Rationale,
            new ReadOnlyDictionary<string, double>(metrics),
            Array.AsReadOnly(evidence),
            Array.AsReadOnly(fileHashes),
            snapshot);
    }

    private static string ResolveRelativePath(string projectRoot, string value, string extension, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(':'))
        {
            throw new InvalidReviewInputException($"Finding {fieldName} must be a project-relative path.");
        }

        var resolved = ProjectPathResolver.ResolveRelative(projectRoot, value, fieldName);
        if (!Path.GetExtension(resolved).Equals(extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            throw new InvalidReviewInputException($"Finding {fieldName} must identify an existing {extension} file.");
        }

        return ProjectPathResolver.ToRelativeForwardSlashes(projectRoot, resolved);
    }

    private static void RequireText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidReviewInputException($"Finding {fieldName} must be nonempty.");
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
