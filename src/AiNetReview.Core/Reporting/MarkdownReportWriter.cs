namespace AiNetReview.Core.Reporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;

/// <summary>Writes and atomically publishes one complete Markdown report set.</summary>
public sealed class MarkdownReportWriter
{
    private static readonly string[] FindingAreas = ["production", "tests", "mixed"];
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int HResultFacilityWin32 = unchecked((int)0x80070000);
    private const int HResultFacilityMask = unchecked((int)0xFFFF0000);
    private static readonly TimeSpan[] PublicationRetryDelays =
    [
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(160),
    ];
    private readonly Func<CancellationToken, ValueTask>? beforePublication;
    private readonly Action<string, string> moveDirectory;
    private readonly Action<string, bool> deleteDirectory;

    public MarkdownReportWriter()
    {
        moveDirectory = Directory.Move;
        deleteDirectory = Directory.Delete;
    }

    internal MarkdownReportWriter(Func<CancellationToken, ValueTask> beforePublication)
        : this(beforePublication ?? throw new ArgumentNullException(nameof(beforePublication)), Directory.Move, Directory.Delete)
    {
    }

    internal MarkdownReportWriter(
        Func<CancellationToken, ValueTask>? beforePublication,
        Action<string, string> moveDirectory,
        Action<string, bool> deleteDirectory)
    {
        this.beforePublication = beforePublication;
        this.moveDirectory = moveDirectory ?? throw new ArgumentNullException(nameof(moveDirectory));
        this.deleteDirectory = deleteDirectory ?? throw new ArgumentNullException(nameof(deleteDirectory));
    }

    public async Task<PublishedReport> WriteAsync(
        ReviewConfig config,
        ReviewRunResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(result);
        var analyses = config.Analyses.OrderBy(static analysis => analysis.AnalysisId, StringComparer.Ordinal).ToArray();
        if (result.Analyses.Count != analyses.Length || analyses.Any(analysis => result.Analyses.Count(run => run.AnalysisId == analysis.AnalysisId) != 1))
        {
            throw new ArgumentException("Run results must contain exactly one result for every configured analysis.", nameof(result));
        }

        var findings = GetFindings(result);
        ValidateFindingIds(findings);
        var findingIds = GetFindingIds(findings);
        Directory.CreateDirectory(config.ResolvedOutputDirectory);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = CreateRunId(DateTimeOffset.UtcNow);
            var temporaryPath = Path.Combine(config.ResolvedOutputDirectory, $".ainetreview-tmp-{runId}");
            var finalPath = Path.Combine(config.ResolvedOutputDirectory, runId);
            if (!TryCreateOwnedTemporaryDirectory(temporaryPath))
            {
                continue;
            }

            var published = false;
            try
            {
                foreach (var configuredAnalysis in analyses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var area in FindingAreas)
                    {
                        var areaFindings = findings.Where(finding => finding.AnalysisId == configuredAnalysis.AnalysisId
                            && FindingReportUtilities.GetFindingArea(finding) == area).ToArray();
                        await WriteAnalysisAsync(temporaryPath, area, configuredAnalysis, areaFindings, findings,
                                findingIds, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                foreach (var area in FindingAreas)
                {
                    await WriteAreaIndexAsync(temporaryPath, area, analyses,
                            findings.Where(finding => FindingReportUtilities.GetFindingArea(finding) == area).ToArray(), cancellationToken)
                        .ConfigureAwait(false);
                }

                await MapsReportWriter.WriteMapsAsync(temporaryPath, result.Maps, cancellationToken).ConfigureAwait(false);
                await AuditMapReportWriter.WriteAuditMapAsync(temporaryPath, runId, findings, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await ReportIoUtils.WriteUtf8Async(indexPath, FormatIndex(runId, config, config.AllAnalyses.OrderBy(static item => item.AnalysisId, StringComparer.Ordinal).ToArray(), result, findings), cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (beforePublication is not null)
                {
                    await beforePublication(cancellationToken).ConfigureAwait(false);
                }

                File.Delete(Path.Combine(temporaryPath, ".owner"));

                try
                {
                    await MoveDirectoryWithTransientRetryAsync(temporaryPath, finalPath, cancellationToken).ConfigureAwait(false);
                    published = true;
                    return new PublishedReport(runId, Path.Combine(config.OutputDirectory, runId, "index.md").Replace('\\', '/'));
                }
                catch (IOException) when (Directory.Exists(finalPath))
                {
                    // A concurrent run won the same name; render again with a fresh ID.
                }
            }
            finally
            {
                if (!published)
                {
                    await TryDeleteTemporaryDirectoryAsync(temporaryPath).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task MoveDirectoryWithTransientRetryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                moveDirectory(sourcePath, destinationPath);
                return;
            }
            catch (IOException exception) when (IsTransientPublicationLock(exception))
            {
                if (attempt >= PublicationRetryDelays.Length || Directory.Exists(destinationPath))
                {
                    throw;
                }

                await Task.Delay(PublicationRetryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransientPublicationLock(Exception exception)
    {
        // Windows reports locks on files inside a directory as HRESULT_FROM_WIN32 errors.
        if ((exception.HResult & HResultFacilityMask) != HResultFacilityWin32)
        {
            return false;
        }

        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation;
    }

    private static bool TryCreateOwnedTemporaryDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var ownership = new FileStream(Path.Combine(path, ".owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException) when (Directory.Exists(path))
        {
            return false;
        }
    }

    private static string FormatIndex(
        string runId,
        ReviewConfig config,
        ConfiguredReviewAnalysis[] analyses,
        ReviewRunResult result,
        IReadOnlyList<ReviewFinding> findings)
    {
        var builder = new StringBuilder();
        builder.Append("# AiNetReview – ").Append(runId).Append("\n\n")
            .Append("Repository: ").Append(FormatCodeSpan(Path.GetFullPath(config.ProjectRoot))).Append("; solution: ")
            .Append(FormatCodeSpan(config.SolutionPath)).Append(".\n\n")
            .Append("## Review guidance\n\n")
            .Append("Review each finding in scope against source, callers, contracts, tests, and repository design. Classify with concrete evidence as false positive, acceptable design, needs clarification, or actionable. A signal alone does not require a change; avoid metric-driven refactoring. Findings describe static evidence in the loaded snapshot, not execution, runtime coverage, test quality, or defects.\n\n")
            .Append("A complete audit covers all findings in the three areas below. Name any assigned areas left unreviewed; related findings may be assessed together.\n\n")
            .Append("## Maps\n\n")
            .Append("Choose a solution or project map through `maps/index.md`. The findings map is `maps/audit/index.md`; it routes each finding ID to canonical analysis details. Source paths are project-root-relative; `Lnn` refers to the file group above. Report each ID's classification, evidence, and unresolved context; name unreviewed findings or source groups explicitly.\n\n");

        if (findings.Count == 0)
        {
            builder.Append(analyses.All(static analysis => !analysis.Enabled)
                ? "No review was performed because all analyses are disabled.\n\n"
                : "No findings were found.\n\n");
        }

        builder.Append("## Audit scope\n\n")
            .Append("| Area | Findings |\n| --- | ---: |\n");
        foreach (var area in FindingAreas)
        {
            var count = findings.Count(finding => FindingReportUtilities.GetFindingArea(finding) == area);
            builder.Append("| ").Append(area).Append(" | ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }
        builder.Append("\n");
        foreach (var area in FindingAreas)
        {
            builder.Append("- ").Append(FormatCodeSpan(area + "/index.md")).Append('\n');
        }
        builder.Append("- ").Append(FormatCodeSpan("maps/index.md")).Append('\n');

        builder.Append("\n## Quick start\n\n")
            .Append("| Task | Route |\n| --- | --- |\n")
            .Append("| Check this run's scope, configured analyses, and totals | `index.md` |\n")
            .Append("| Review findings by area | `production/index.md`, `tests/index.md`, or `mixed/index.md` |\n")
            .Append("| Open a finding by ID | `maps/audit/index.md` → canonical analysis detail |\n")
            .Append("| Find all findings involving a source file | Search its full repository-relative path in canonical analysis details under `production/`, `tests/`, and `mixed/`; the audit map indexes representative files |\n")
            .Append("| Interpret a finding or source excerpt | Read the canonical analysis report's header first; its path legend, scope, and uncertainty notes apply to the excerpts |\n")
            .Append("| Find a type declaration | `maps/projects.md` → the selected project's `structure.md` |\n")
            .Append("| Trace type dependencies | `maps/projects.md` → the selected project's `dependencies.md` hub → its `dependencies-outgoing.md` or `dependencies-incoming.md` detail |\n");

        builder.Append("\n## Analyses and effective options\n\n| Analysis | Enabled | Subject scope | Effective options |\n| --- | --- | --- | --- |\n");
        foreach (var analysis in analyses)
        {
            var effective = analysis.EffectiveOptions.Values.Count == 0 ? "—" : FormatOptions(analysis.EffectiveOptions);
            var subjectScope = analysis.Analysis.FindingPresenter.PresentAnalysis(findings.Where(finding => finding.AnalysisId == analysis.AnalysisId).ToArray()).SubjectScope;
            var testScope = subjectScope switch
            {
                ReviewAnalysisSubjectScope.ProductionWithTestEvidence => "Production targets; tests are evidence",
                ReviewAnalysisSubjectScope.Production => "Production",
                _ => "Production and tests",
            };
            if (analysis.EffectiveTestOptions is { Values.Count: > 0 } testOptions
                && !string.Equals(FormatOptions(testOptions), FormatOptions(analysis.EffectiveOptions), StringComparison.Ordinal))
            {
                effective += " / tests " + FormatOptions(testOptions);
            }
            builder.Append("| ").Append(FormatCodeSpan(analysis.AnalysisId)).Append(" | ").Append(analysis.Enabled ? "yes" : "no").Append(" | ").Append(testScope)
                .Append(" | ").Append(effective == "—" ? effective : FormatCodeSpan(effective)).Append(" |\n");
        }

        var exclusions = result.Analyses.SelectMany(analysis => analysis.Result.ScopeExclusions
            .Select(exclusion => (analysis.AnalysisId, exclusion.ProjectPath, exclusion.Reason)))
            .OrderBy(static item => item.AnalysisId, StringComparer.Ordinal)
            .ThenBy(static item => item.ProjectPath, StringComparer.Ordinal)
            .ToArray();
        builder.Append("\n## Conservative scope exclusions\n\n");
        if (exclusions.Length == 0)
        {
            builder.Append("No project areas were excluded because of binding uncertainty.\n\n");
        }
        else
        {
            builder.Append("| Analysis | Project area | Reason |\n| --- | --- | --- |\n");
            foreach (var exclusion in exclusions)
            {
                builder.Append("| ").Append(FormatCodeSpan(exclusion.AnalysisId)).Append(" | ")
                    .Append(FormatCodeSpan(exclusion.ProjectPath)).Append(" | ")
                    .Append(EscapeInline(exclusion.Reason)).Append(" |\n");
            }
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static async Task WriteAreaIndexAsync(
        string runDirectory,
        string area,
        ConfiguredReviewAnalysis[] analyses,
        IReadOnlyList<ReviewFinding> findings,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(area).Append(" findings\n\n")
            .Append("This index covers all current ").Append(area).Append(" findings. Related findings in other areas remain visible in their own index. Root report: `../index.md`.\n\n");

        var visible = analyses.Where(analysis => findings.Any(finding => finding.AnalysisId == analysis.AnalysisId))
            .ToArray();
        if (visible.Length == 0)
        {
            builder.Append("No findings in this area.\n");
        }
        else
        {
            foreach (var analysis in visible)
            {
                var count = findings.Count(finding => finding.AnalysisId == analysis.AnalysisId);
                builder.Append("- ").Append(FormatCodeSpan(FindingReportUtilities.EncodePathSegment(analysis.AnalysisId) + ".md"))
                    .Append(" ( ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" findings)\n");
            }
        }

        var directory = Path.Combine(runDirectory, area);
        Directory.CreateDirectory(directory);
        await ReportIoUtils.WriteUtf8Async(Path.Combine(directory, "index.md"), builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<ReviewFinding> GetFindings(ReviewRunResult result)
    {
        if (result.Findings.Count > 0 || result.DetectedCount == 0)
        {
            return result.Findings;
        }

        return result.Analyses.SelectMany(analysis => analysis.Result.Findings.Select(finding =>
        {
            var paths = finding.Evidence.Select(static evidence => evidence.SourcePath).Append(finding.SourcePath).Distinct(StringComparer.Ordinal).ToArray();
            return new ReviewFinding(analysis.AnalysisId, finding, paths, Array.Empty<ReviewFindingReference>());
        })).ToArray();
    }

    private static IReadOnlyDictionary<string, string> GetFindingIds(IEnumerable<ReviewFinding> findings) =>
        findings.ToDictionary(FindingReportUtilities.FindingKey, FindingReportUtilities.GetFindingId, StringComparer.Ordinal);

    private static void ValidateFindingIds(IReadOnlyList<ReviewFinding> findings)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            if (!identities.Add(FindingReportUtilities.FindingKey(finding)) || !ids.Add(FindingReportUtilities.GetFindingId(finding)))
            {
                throw new InvalidOperationException("Review findings must have unique identities and stable IDs.");
            }
        }
    }

    private static async Task WriteAnalysisAsync(
        string runDirectory,
        string area,
        ConfiguredReviewAnalysis configuredAnalysis,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> allFindings,
        IReadOnlyDictionary<string, string> findingIds,
        CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
        {
            return;
        }

        var reportDirectory = Path.Combine(runDirectory, area);
        Directory.CreateDirectory(reportDirectory);
        var analysisPath = Path.Combine(reportDirectory, configuredAnalysis.AnalysisId + ".md");
        await ReportIoUtils.WriteUtf8Async(analysisPath, FormatAnalysisReport(configuredAnalysis, analysisPath, findings, allFindings, findingIds), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatAnalysisReport(
        ConfiguredReviewAnalysis configuredAnalysis,
        string reportPath,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> allFindings,
        IReadOnlyDictionary<string, string> findingIds)
    {
        var descriptor = configuredAnalysis.Analysis.Descriptor;
        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeLinkText(descriptor.Title)).Append("\n\n")
            .Append(EscapeInline(descriptor.Purpose)).Append("\n\n");
        var effectiveOptions = FormatEffectiveOptions(configuredAnalysis);
        if (effectiveOptions.Length > 0)
        {
            builder.Append(effectiveOptions);
        }
        builder.Append("Review questions:\n\n");
        foreach (var question in descriptor.ReviewQuestions)
        {
            builder.Append("- ").Append(EscapeInline(question)).Append('\n');
        }
        builder.Append("\nSource paths are repository-relative; `Lnn` refers to the file group above. Related report paths are relative to this report.\n");

        var analysisPresentation = configuredAnalysis.Analysis.FindingPresenter.PresentAnalysis(findings);

        foreach (var note in analysisPresentation.MarkdownNotes)
        {
            builder.Append(note);
        }

        var groups = findings
            .GroupBy(static item => (item.Finding.ProjectPath, item.Finding.SourcePath))
            .OrderBy(static group => group.Key.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.SourcePath, StringComparer.Ordinal)
            .Select(group => new
            {
                group.Key.ProjectPath,
                group.Key.SourcePath,
                Findings = group.OrderBy(static item => item.Finding.StartLine)
                    .ThenBy(static item => item.Finding.SubjectId, StringComparer.Ordinal)
                    .ThenBy(static item => item.Finding.Discriminator, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();

        var projectGroups = groups.GroupBy(static group => group.ProjectPath, StringComparer.Ordinal).ToArray();
        builder.Append("\n## Summary\n\nTotal findings: ")
            .Append(findings.Count.ToString(CultureInfo.InvariantCulture)).Append(" across ")
            .Append(projectGroups.Length.ToString(CultureInfo.InvariantCulture)).Append(" projects and ")
            .Append(groups.Length.ToString(CultureInfo.InvariantCulture)).Append(" source files.\n");

        builder.Append("\n## Findings\n\n");
        foreach (var projectGroup in projectGroups)
        {
            var projectFindingCount = projectGroup.Sum(static group => group.Findings.Length);
            var representedRoles = projectGroup.SelectMany(static group => group.Findings)
                .SelectMany(static finding => finding.SubjectOccurrences)
                .Where(occurrence => occurrence.Symbol.ProjectPath == projectGroup.Key)
                .Select(static occurrence => occurrence.Role)
                .Distinct()
                .ToArray();
            var roleLabel = representedRoles.Length == 0
                ? "role not represented"
                : string.Join(" + ", representedRoles.OrderBy(static role => role)
                    .Select(static role => role == ProjectRole.Tests ? "tests" : "production"));
            builder.Append("### Project: ").Append(FormatCodeSpan(projectGroup.Key)).Append(" (")
                .Append(roleLabel).Append("; ")
                .Append(projectGroup.Count().ToString(CultureInfo.InvariantCulture)).Append(" files, ")
                .Append(projectFindingCount.ToString(CultureInfo.InvariantCulture)).Append(" findings)\n\n");
            foreach (var group in projectGroup)
            {
                builder.Append("#### File: ").Append(FormatCodeSpan(group.SourcePath)).Append(" (")
                    .Append(group.Findings.Length.ToString(CultureInfo.InvariantCulture)).Append(" findings");
                builder.Append(")\n\n");
                foreach (var reviewFinding in group.Findings)
                {
                    var finding = reviewFinding.Finding;
                    var presentation = configuredAnalysis.Analysis.FindingPresenter.PresentFinding(
                        reviewFinding, analysisPresentation.SuppressAttributionUncertainty);
                    if (findingIds.TryGetValue(FindingReportUtilities.FindingKey(reviewFinding), out var findingId))
                    {
                        builder.Append("- ").Append(findingId).Append('\n');
                        if (!presentation.OmitSubjectLine)
                        {
                            builder.Append("  - Subject: ").Append(FormatCodeSpan(ReviewFindingPresentationFormatting.FormatSymbolIdentity(finding.SubjectId))).Append(" at ")
                                .Append(FormatFindingLocation(group.SourcePath, finding.SourcePath, finding.StartLine)).Append('\n');
                        }
                    }

                    AppendFindingPresentation(builder, presentation.Blocks, reviewFinding, group.SourcePath);
                    AppendUnrepresentedEvidence(builder, reviewFinding, group.SourcePath, presentation.EvidenceIsRepresented);
                    var related = FormatRelated(reviewFinding, reportPath, allFindings);
                    if (!string.IsNullOrEmpty(related))
                    {
                        // Analysis IDs are validated lowercase slugs.
                        builder.Append("  - Related: ").Append(related).Append('\n');
                    }
                }

                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void AppendFindingPresentation(
        StringBuilder builder,
        IReadOnlyList<ReviewFindingReportBlock> blocks,
        ReviewFinding finding,
        string representativePath)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ReviewFindingReportTextBlock text:
                    builder.Append("  - ").Append(EscapeInline(text.Label)).Append(": ").Append(EscapeInline(text.Text)).Append('\n');
                    break;
                case ReviewFindingReportEvidenceBlock evidence:
                    if (!string.IsNullOrEmpty(evidence.Heading))
                    {
                        builder.Append("  - ").Append(EscapeInline(evidence.Heading)).Append('\n');
                    }

                    foreach (var row in evidence.Items)
                    {
                        builder.Append("    - ");
                        var projectPath = row.ProjectPath ?? finding.Finding.ProjectPath;
                        var projectDiffers = !string.Equals(projectPath, finding.Finding.ProjectPath, StringComparison.Ordinal);
                        if (row.ShowProjectPathWhenDifferent && projectDiffers)
                        {
                            builder.Append(FormatCodeSpan(projectPath)).Append(' ');
                        }

                        if (row.ShowSourcePathWhenDifferent
                            && !string.Equals(representativePath.Replace('\\', '/'), row.SourcePath.Replace('\\', '/'), StringComparison.Ordinal))
                        {
                            builder.Append(FormatSourceLocation(row.SourcePath, 0)).Append(' ');
                        }
                        else if (row.SourceRange is null || !row.ShowSourcePathWhenDifferent)
                        {
                            builder.Append(FormatFindingLocation(representativePath, row.SourcePath, row.Line)).Append(' ');
                        }

                        builder.Append(FormatCodeSpan(ReviewFindingPresentationFormatting.FormatSymbolIdentity(row.Label)));
                        if (row.SourceRange is { } sourceRange)
                        {
                            var start = sourceRange.StartLine.ToString(CultureInfo.InvariantCulture) + ":" + sourceRange.StartColumn.ToString(CultureInfo.InvariantCulture);
                            var end = sourceRange.EndLine.ToString(CultureInfo.InvariantCulture) + ":" + sourceRange.EndColumn.ToString(CultureInfo.InvariantCulture);
                            builder.Append(" [").Append(FormatCodeSpan(start)).Append('–').Append(FormatCodeSpan(end)).Append(')');
                        }

                        if (!string.IsNullOrWhiteSpace(row.Detail))
                        {
                            builder.Append(" — ").Append(EscapeInline(row.Detail));
                        }

                        if (row.IncludeOccurrenceRole
                            && (!row.ShowProjectPathWhenDifferent || projectDiffers
                                || string.Equals(GetEvidenceOccurrenceRole(finding, new FindingEvidence(row.SourcePath, row.Line, row.Label, string.Empty, string.Empty)), "tests", StringComparison.Ordinal)))
                        {
                            builder.Append(" (").Append(GetOccurrenceRole(finding, projectPath, row.SourcePath, row.Label)).Append(')');
                        }

                        builder.Append('\n');
                    }

                    break;
                case ReviewFindingReportSymbolsBlock symbols:
                    foreach (var row in symbols.Items)
                    {
                        builder.Append("  - ");
                        var projectPath = row.Symbol.ProjectPath;
                        var projectDiffers = !string.Equals(projectPath, finding.Finding.ProjectPath, StringComparison.Ordinal);
                        if (row.ShowProjectPathWhenDifferent && projectDiffers)
                        {
                            builder.Append(FormatCodeSpan(projectPath)).Append(' ');
                        }

                        builder.Append(FormatFindingLocation(representativePath, row.Symbol.SourcePath, row.Symbol.Line)).Append(' ')
                            .Append(FormatCodeSpan(ReviewFindingPresentationFormatting.FormatSymbolIdentity(row.Symbol.SymbolId)));
                        if (row.IncludeOccurrenceRole && projectDiffers)
                        {
                            builder.Append(" (").Append(GetOccurrenceRole(finding, row.Symbol)).Append(')');
                        }

                        builder.Append('\n');
                    }

                    break;
                case ReviewFindingReportPathBlock path:
                    var pathItems = path.Items.Select(item =>
                    {
                        var role = item.IncludeOccurrenceRole
                            ? "; " + GetEvidenceOccurrenceRole(finding, new FindingEvidence(item.SourcePath, item.Line, item.Label, string.Empty, string.Empty))
                            : string.Empty;
                        return FormatCodeSpan(ReviewFindingPresentationFormatting.FormatSymbolIdentity(item.Label)) + " ("
                            + FormatFindingLocation(representativePath, item.SourcePath, item.Line) + role + ")";
                    });
                    builder.Append("  - ").Append(EscapeInline(path.Label)).Append(": ").Append(string.Join(" -> ", pathItems)).Append('\n');
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported report block type '{block.GetType().Name}'.");
            }
        }
    }

    private static void AppendUnrepresentedEvidence(
        StringBuilder builder,
        ReviewFinding finding,
        string representativePath,
        bool evidenceIsRendered)
    {
        if (!evidenceIsRendered && finding.Finding.Evidence.Count > 0)
        {
            var evidenceItems = finding.Finding.Evidence.Where(evidence =>
                !evidence.OmitWhenRedundantWithSubject
                || !string.Equals(evidence.SourcePath, finding.Finding.SourcePath, StringComparison.Ordinal)
                || evidence.Line != finding.Finding.StartLine).ToArray();
            if (evidenceItems.Length > 0)
            {
                builder.Append("  - Evidence:\n");
                foreach (var evidence in evidenceItems)
                {
                    builder.Append("    - ").Append(FormatFindingLocation(representativePath, evidence.SourcePath, evidence.Line)).Append(' ')
                        .Append(FormatCodeSpan(ReviewFindingPresentationFormatting.FormatSymbolIdentity(evidence.Label)));
                    if (!string.IsNullOrWhiteSpace(evidence.Detail))
                    {
                        builder.Append(" — ").Append(EscapeInline(evidence.Detail));
                    }

                    if (GetEvidenceOccurrenceRole(finding, evidence) == "tests")
                    {
                        builder.Append(" (tests)");
                    }
                    builder.Append('\n');
                }
            }
        }

        var representedPaths = finding.Finding.Evidence.Select(static evidence => evidence.SourcePath)
            .Append(finding.Finding.SourcePath)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (finding.Finding.RelatedSymbols.Count > 1)
        {
            representedPaths.UnionWith(finding.Finding.RelatedSymbols.Select(static symbol => symbol.SourcePath));
        }

        var additionalPaths = finding.SourcePaths.Where(path => !representedPaths.Contains(path))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (additionalPaths.Length > 0)
        {
            builder.Append("  - Other source files: ")
                .Append(string.Join(", ", additionalPaths.Select(FormatCodeSpan))).Append('\n');
        }
    }

    private static string FormatSourceLocation(string sourcePath, int line)
    {
        var normalizedPath = sourcePath.Replace('\\', '/');
        return FormatCodeSpan(line > 0 ? normalizedPath + ":" + line.ToString(CultureInfo.InvariantCulture) : normalizedPath);
    }

    private static string FormatFindingLocation(string representativePath, string sourcePath, int line)
    {
        if (line > 0 && string.Equals(representativePath.Replace('\\', '/'), sourcePath.Replace('\\', '/'), StringComparison.Ordinal))
        {
            return FormatCodeSpan("L" + line.ToString(CultureInfo.InvariantCulture));
        }

        return FormatSourceLocation(sourcePath, line);
    }

    private static string FormatRelated(
        ReviewFinding finding,
        string reportPath,
        IReadOnlyList<ReviewFinding> allFindings)
    {
        if (finding.RelatedFindings.Count == 0)
        {
            return string.Empty;
        }

        var relatedItems = finding.RelatedFindings.Select(reference =>
        {
            var target = allFindings.FirstOrDefault(candidate => candidate.AnalysisId == reference.AnalysisId
                && candidate.Finding.ProjectPath == reference.ProjectPath
                && candidate.Finding.SourcePath == reference.SourcePath
                && candidate.Finding.SubjectId == reference.SubjectId
                && candidate.Finding.Discriminator == reference.Discriminator);
            if (target is null) return (Path: string.Empty, Id: reference.AnalysisId + " (target unavailable in this run)");

            var targetArea = FindingReportUtilities.GetFindingArea(target);
            var targetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, "..", targetArea, FindingReportUtilities.EncodePathSegment(reference.AnalysisId) + ".md"));
            var relativePath = Path.GetRelativePath(Path.GetDirectoryName(reportPath)!, targetPath).Replace('\\', '/');
            return (Path: relativePath, Id: FindingReportUtilities.GetFindingId(target));
        }).Distinct().ToArray();
        var groupedItems = relatedItems.Where(static item => item.Path.Length > 0)
            .GroupBy(static item => item.Path, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => FormatCodeSpan(group.Key) + ": " + string.Join(", ", group.Select(static item => item.Id).Order(StringComparer.Ordinal)))
            .Concat(relatedItems.Where(static item => item.Path.Length == 0).Select(static item => item.Id).Order(StringComparer.Ordinal));
        return string.Join("; ", groupedItems);
    }

    private static string GetOccurrenceRole(ReviewFinding finding, FindingSymbol symbol)
    {
        return GetOccurrenceRole(finding, symbol.ProjectPath, symbol.SourcePath, symbol.SymbolId);
    }

    private static string GetEvidenceOccurrenceRole(ReviewFinding finding, FindingEvidence evidence)
    {
        var occurrence = finding.Occurrences.FirstOrDefault(candidate =>
            candidate.Symbol.SourcePath == evidence.SourcePath
            && candidate.Symbol.SymbolId == evidence.Label);
        return occurrence?.Role == ProjectRole.Tests ? "tests" : "production";
    }

    private static string GetOccurrenceRole(ReviewFinding finding, string projectPath, string sourcePath, string symbolId)
    {
        var occurrence = finding.Occurrences.FirstOrDefault(candidate =>
            candidate.Symbol.ProjectPath == projectPath
            && candidate.Symbol.SourcePath == sourcePath
            && candidate.Symbol.SymbolId == symbolId);
        return occurrence?.Role == ProjectRole.Tests ? "tests" : "production";
    }

    private static string FormatOptions(ReviewAnalysisOptions options) => "{" + string.Join(
        ", ", options.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{JsonSerializer.Serialize(pair.Key)}: {FormatJsonValue(pair.Value)}")) + "}";

    private static string FormatEffectiveOptions(ConfiguredReviewAnalysis configuredAnalysis)
    {
        var testOptions = configuredAnalysis.EffectiveTestOptions;
        if (configuredAnalysis.EffectiveOptions.Values.Count == 0
            && (testOptions is null || testOptions.Values.Count == 0))
        {
            return string.Empty;
        }
        if (testOptions is null)
        {
            return "Effective options: " + FormatCodeSpan(FormatOptions(configuredAnalysis.EffectiveOptions)) + "\n\n";
        }

        var testDescriptors = configuredAnalysis.Analysis.Descriptor.TestOptions;
        var identical = testDescriptors.All(option => JsonElement.DeepEquals(
            configuredAnalysis.EffectiveOptions[option.Name], testOptions[option.Name]));
        var builder = new StringBuilder();
        if (identical)
        {
            builder.Append("Effective options: ")
                .Append(FormatCodeSpan(FormatOptions(configuredAnalysis.EffectiveOptions)))
                .Append(" (same for production and test projects).\n\n");
        }
        else
        {
            builder.Append("Effective options (production projects): ")
                .Append(FormatCodeSpan(FormatOptions(configuredAnalysis.EffectiveOptions))).Append("\n\n")
                .Append("Effective options (test projects): ")
                .Append(FormatCodeSpan(FormatOptions(testOptions))).Append("\n\n");
        }

        return builder.ToString();
    }

    private static string FormatJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject()
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .Select(static property => $"{JsonSerializer.Serialize(property.Name)}: {FormatJsonValue(property.Value)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(FormatJsonValue)) + "]",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => throw new ArgumentException("Option contains an undefined JSON value.", nameof(value)),
    };

    internal static string FormatCodeSpan(string value) => MarkdownFormatUtils.FormatCodeSpan(value);

    internal static string EscapeInline(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '<' or '>' or '#' or '+' or '-' or '!' or '|')
            {
                builder.Append('\\');
            }

            if (character == '\r')
            {
                continue;
            }

            builder.Append(character == '\n' ? ' ' : character);
        }

        return builder.ToString();
    }

    internal static string EscapeLinkText(string value) => EscapeInline(value).Replace("\\-", "-", StringComparison.Ordinal);

    private static string CreateRunId(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
        + "-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    private async Task TryDeleteTemporaryDirectoryAsync(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    deleteDirectory(path, true);
                }

                return;
            }
            catch (Exception exception) when (IsTransientPublicationLock(exception))
            {
                if (attempt >= PublicationRetryDelays.Length)
                {
                    return;
                }

                await Task.Delay(PublicationRetryDelays[attempt]).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}

public sealed record PublishedReport(string RunId, string IndexPath);
