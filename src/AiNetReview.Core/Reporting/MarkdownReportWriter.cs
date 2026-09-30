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
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int HResultFacilityWin32 = unchecked((int)0x80070000);
    private const int HResultFacilityMask = unchecked((int)0xFFFF0000);
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
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
        CancellationToken cancellationToken = default,
        string? configurationPath = null,
        BaselineCommandContext? baselineCommandContext = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(result);
        var analyses = config.Analyses.OrderBy(static analysis => analysis.AnalysisId, StringComparer.Ordinal).ToArray();
        if (result.Analyses.Count != analyses.Length || analyses.Any(analysis => result.Analyses.Count(run => run.AnalysisId == analysis.AnalysisId) != 1))
        {
            throw new ArgumentException("Run results must contain exactly one result for every configured analysis.", nameof(result));
        }

        var findings = GetFindings(result);
        var changedFindings = findings.Where(finding => IsChangedForReport(finding, result)).ToArray();
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
                    var allFindings = findings.Where(finding => finding.AnalysisId == configuredAnalysis.AnalysisId).ToArray();
                    var analysisChangedFindings = changedFindings.Where(finding => finding.AnalysisId == configuredAnalysis.AnalysisId).ToArray();
                    await WriteViewAnalysisAsync(temporaryPath, "changed-files", configuredAnalysis, analysisChangedFindings, changedFindings, findings, cancellationToken)
                        .ConfigureAwait(false);
                    await WriteViewAnalysisAsync(temporaryPath, "all-findings", configuredAnalysis, allFindings, findings, findings, cancellationToken)
                        .ConfigureAwait(false);
                }

                await WriteViewIndexAsync(temporaryPath, "changed-files", "Changed files", analyses, changedFindings, changedOnly: true, cancellationToken)
                    .ConfigureAwait(false);
                await WriteViewIndexAsync(temporaryPath, "all-findings", "All findings", analyses, findings, changedOnly: false, cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await WriteUtf8Async(indexPath, FormatIndex(runId, config, analyses, findings, changedFindings, configurationPath, baselineCommandContext), cancellationToken)
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

    private static async Task WriteUtf8Async(string path, string content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, Utf8WithoutBom, 4096, leaveOpen: true) { NewLine = "\n" };
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatIndex(
        string runId,
        ReviewConfig config,
        ConfiguredReviewAnalysis[] analyses,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> changedFindings,
        string? configurationPath,
        BaselineCommandContext? baselineCommandContext)
    {
        var builder = new StringBuilder();
        builder.Append("# AiNetReview – ").Append(runId).Append("\n\n")
            .Append("- Run ID: `").Append(runId).Append("`\n")
            .Append("- Repository: `").Append(EscapeInline(Path.GetFullPath(config.ProjectRoot))).Append("`\n")
            .Append("- Solution: `").Append(EscapeInline(config.SolutionPath)).Append("`\n\n")
            .Append("## Review guidance\n\n")
            .Append("Findings are potential review signals. They may point to deeper or cross-cutting problems, but do not prove a defect or require a change. First read the target repository's applicable instructions and relevant design documents, then classify each signal using application goals, architecture, responsibilities, contracts, callers, tests, and related findings. An accurate signal can describe an acceptable design; distinguish that from a false positive. All findings may validly result in no changes. Avoid metric-driven refactoring and symptom workarounds; make a local change when the broader context supports it. Explain consequential changes and tradeoffs to the user. The goal is to support understandable, reliable agentic development and help prevent drift, not to claim that the analysis proves drift. Use `changed-files/` as the primary working set. Do not inspect or report findings from `all-findings/` unless the user explicitly requests a full repository audit.\n\n");

        var changedCount = changedFindings.Count;
        var allCount = findings.Count;
        if (allCount == 0)
        {
            builder.Append(analyses.Length == 0
                ? "No review was performed because all analyses are disabled.\n\n"
                : "No findings were found.\n\n");
        }

        builder.Append("## Analysis reports (changed files)\n\n")
            .Append("- [`changed-files/`](changed-files/index.md) contains findings that involve at least one source file that is new or changed since the optional baseline (")
            .Append(changedCount.ToString(CultureInfo.InvariantCulture)).Append(" findings). Without a baseline, all current source files are treated as new. This file based filter can miss indirect effects in unchanged files.\n\n");

        if (analyses.Any(static analysis => analysis.AnalysisId == "missing-test-evidence-candidates"))
        {
            builder.Append("For `missing-test-evidence-candidates`, the changed-files view follows the complete C# snapshot: without a baseline it shows every current finding; with a baseline it shows every current finding when any C# path was added, changed, or deleted, and none when the C# snapshot is unchanged. Other analyses keep their file-based selection. The `all-findings/` view always contains every current finding.\n\n");
        }

        var reportLinks = new List<string>();
        foreach (var analysis in analyses)
        {
            var count = changedFindings.Count(finding => finding.AnalysisId == analysis.AnalysisId);
            if (count > 0)
            {
                reportLinks.Add("- [" + EscapeLinkText(analysis.Analysis.Descriptor.Title) + " ("
                    + count.ToString(CultureInfo.InvariantCulture) + ")](changed-files/"
                    + EncodePathSegment(analysis.AnalysisId) + ".md)");
            }
        }

        builder.Append(reportLinks.Count == 0 ? "No analysis report files were created.\n\n" : string.Join('\n', reportLinks) + "\n\n");
        builder.Append("[Open the changed files view](changed-files/index.md)\n\n");

        builder.Append("## All findings (reference only)\n\n")
            .Append("> **Agent instruction:** Do not inspect, summarize, or display findings from this view unless the user explicitly requests an audit of the entire repository.\n\n")
            .Append("- [`all-findings/`](all-findings/index.md) always contains every current finding (")
            .Append(allCount.ToString(CultureInfo.InvariantCulture)).Append(" findings). This is the complete reference view.\n\n")
            .Append("[Open the complete findings view](all-findings/index.md)\n\n");

        builder.Append("## Set a new baseline\n\n");

        if (baselineCommandContext is null)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current process executable path is unavailable.");
            builder.Append("Run this PowerShell command from any directory to set the comparison point to the current source files:\n\n")
                .Append("```powershell\n& ").Append(QuotePowerShell(Path.GetFullPath(executable))).Append(" baseline ")
                .Append(QuotePowerShell(Path.GetFullPath(config.ProjectRoot))).Append("\n```\n\n");
        }
        else
        {
            builder.Append("Run this PowerShell command to update the centrally stored baseline for this manual audit target without writing into the target repository:\n\n")
                .Append("```powershell\n& ").Append(QuotePowerShell(baselineCommandContext.ScriptPath))
                .Append(" -Target ").Append(QuotePowerShell(baselineCommandContext.TargetName))
                .Append(" -BaselineOnly\n```\n\n");
        }

        builder.Append("The command replaces the baseline for all source files and does not require a report.\n");
        return builder.ToString();
    }

    private static async Task WriteViewIndexAsync(
        string runDirectory,
        string viewDirectory,
        string title,
        ConfiguredReviewAnalysis[] analyses,
        IReadOnlyList<ReviewFinding> findings,
        bool changedOnly,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(title).Append("\n\n");
        if (!changedOnly)
        {
            builder.Append("> **Notice for AI agents:** This view contains the entire repository baseline for reference. Do not review or report these findings unless the user explicitly requested a full repository audit. Use [`changed-files/`](../changed-files/index.md) for active review.\n\n");
        }

        var visible = analyses.Where(analysis => findings.Any(finding => finding.AnalysisId == analysis.AnalysisId))
            .ToArray();
        if (visible.Length == 0)
        {
            builder.Append("No findings in this view.\n");
        }
        else
        {
            foreach (var analysis in visible)
            {
                var count = findings.Count(finding => finding.AnalysisId == analysis.AnalysisId);
                builder.Append("- [").Append(EscapeLinkText(analysis.Analysis.Descriptor.Title)).Append(" (")
                    .Append(count.ToString(CultureInfo.InvariantCulture)).Append(")](")
                    .Append(EncodePathSegment(analysis.AnalysisId)).Append(".md)\n");
            }
        }

        var directory = Path.Combine(runDirectory, viewDirectory);
        Directory.CreateDirectory(directory);
        await WriteUtf8Async(Path.Combine(directory, "index.md"), builder.ToString(), cancellationToken).ConfigureAwait(false);
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
            return new ReviewFinding(analysis.AnalysisId, finding, paths, Array.Empty<ReviewFindingReference>(), paths);
        })).ToArray();
    }

    private static bool IsChangedForReport(ReviewFinding finding, ReviewRunResult result) =>
        finding.AnalysisId == "missing-test-evidence-candidates"
            ? result.HasCSharpSnapshotChanges != false
            : finding.IsChanged;

    private static async Task WriteViewAnalysisAsync(
        string runDirectory,
        string viewDirectory,
        ConfiguredReviewAnalysis configuredAnalysis,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> visibleViewFindings,
        IReadOnlyList<ReviewFinding> allFindings,
        CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
        {
            return;
        }

        var reportDirectory = Path.Combine(runDirectory, viewDirectory);
        Directory.CreateDirectory(reportDirectory);
        var analysisPath = Path.Combine(reportDirectory, configuredAnalysis.AnalysisId + ".md");
        await WriteUtf8Async(analysisPath, FormatAnalysisReport(configuredAnalysis, viewDirectory, findings, visibleViewFindings, allFindings), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatAnalysisReport(
        ConfiguredReviewAnalysis configuredAnalysis,
        string viewDirectory,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> visibleViewFindings,
        IReadOnlyList<ReviewFinding> allFindings)
    {
        var descriptor = configuredAnalysis.Analysis.Descriptor;
        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeLinkText(descriptor.Title)).Append("\n\n")
            .Append(EscapeInline(descriptor.Purpose)).Append("\n\n")
            .Append(viewDirectory == "all-findings"
                ? "Review policy: These potential signals do not require changes. This is the reference-only `all-findings/` view; inspect or report it only when the user explicitly requests a full repository audit. See the [root index's Review guidance](../index.md#review-guidance).\n\n"
                : "Review policy: These potential signals do not require changes. Use `changed-files/` as the primary review set; see the [root index's Review guidance](../index.md#review-guidance).\n\n")
            .Append("Effective options: ").Append(FormatCodeSpan(FormatOptions(configuredAnalysis.EffectiveOptions))).Append("\n\n")
            .Append("Review questions:\n\n");
        foreach (var question in descriptor.ReviewQuestions)
        {
            builder.Append("- ").Append(EscapeInline(question)).Append('\n');
        }

        if (configuredAnalysis.AnalysisId is "method-control-flow-outliers" or "code-size-candidates" or "missing-test-evidence-candidates")
        {
            builder.Append("\nControl-flow counting: `decisionCount` counts each `if`, conditional expression, loop, and `catch` once, and each switch section or switch-expression arm once. `decisionConstructCount` counts each `if`, conditional expression, loop, and `catch` once and each entire switch once. Nesting is the maximum depth of counted decisions (`else if` chains stay at the same depth). Operators such as `&&`, `||`, and `??` do not add decisions; these measures are not cyclomatic complexity.\n");
        }

        if (configuredAnalysis.AnalysisId == "missing-test-evidence-candidates")
        {
            builder.Append("\nThis is static test-path evidence from the loaded snapshot, not runtime coverage. The `attribution uncertain` marker means the static test association may be incomplete; it can result from reachable unresolved bindings, method groups, or virtual/interface dispatch, and may propagate to downstream methods over known calls. It does not assess test assertion quality. Reflection, dependency injection, external test projects, dynamic dispatch, branch execution, and custom test discovery can hide associations.\n");
            if (viewDirectory == "changed-files")
            {
                builder.Append("\nChanged-files selection is snapshot-wide because changes to test roots or the call graph can alter associations in unchanged production files. Without a baseline, every current source file is treated as new. With a baseline, any added, changed, or deleted C# path selects all current findings; an unchanged C# snapshot selects none. The source status in each file heading describes only that representative file relative to the baseline; an unchanged status does not mean unaffected.\n");
            }
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
            builder.Append("### Project: ").Append(EscapeInline(projectGroup.Key)).Append(" (")
                .Append(projectGroup.Count().ToString(CultureInfo.InvariantCulture)).Append(" files, ")
                .Append(projectFindingCount.ToString(CultureInfo.InvariantCulture)).Append(" findings)\n\n");
            foreach (var group in projectGroup)
            {
                builder.Append("#### File: ").Append(EscapeInline(group.SourcePath)).Append(" (")
                    .Append(group.Findings.Length.ToString(CultureInfo.InvariantCulture)).Append(" findings");
                if (viewDirectory == "changed-files" && configuredAnalysis.AnalysisId == "missing-test-evidence-candidates")
                {
                    builder.Append("; ").Append(GetMissingTestSourceStatus(group.Findings, group.SourcePath));
                }

                builder.Append(")\n\n");
                foreach (var reviewFinding in group.Findings)
                {
                    var finding = reviewFinding.Finding;
                    var isCluster = finding.RelatedSymbols.Count > 1;
                    if (reviewFinding.AnalysisId == "indirection-drift-candidates")
                    {
                        builder.Append("- Forwarding path: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        foreach (var member in finding.Evidence)
                        {
                            builder.Append("  - ").Append(FormatCodeSpan(member.SourcePath)).Append(':')
                                .Append(member.Line.ToString(CultureInfo.InvariantCulture)).Append(": ")
                                .Append(FormatCodeSpan(member.Label)).Append('\n');
                        }
                    }
                    else if (reviewFinding.AnalysisId == "structural-duplication-candidates")
                    {
                        builder.Append("- Structural duplicate: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        foreach (var occurrence in finding.Evidence)
                        {
                            var (projectPath, start, end) = ParseStructuralEvidenceDetail(occurrence.Detail);
                            builder.Append("  - Project ").Append(FormatCodeSpan(projectPath))
                                .Append(", file ").Append(FormatCodeSpan(occurrence.SourcePath))
                                .Append(": ").Append(FormatCodeSpan(occurrence.Label))
                                .Append(" (start ").Append(FormatCodeSpan(start)).Append("; end-exclusive ")
                                .Append(FormatCodeSpan(end)).Append(")\n");
                        }
                    }
                    else if (reviewFinding.AnalysisId == "missing-test-evidence-candidates")
                    {
                        builder.Append("- ").Append(FormatCodeSpan(finding.SubjectId)).Append(" (line ")
                            .Append(finding.StartLine.ToString(CultureInfo.InvariantCulture)).Append(")\n")
                            .Append("  - Signal: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                        if (finding.Discriminator == "indirect-test-path-only" && finding.Evidence.Count > 1)
                        {
                            var path = finding.Evidence.Skip(1)
                                .Select(static evidence => $"{evidence.Label} ({evidence.SourcePath}:{evidence.Line})");
                            builder.Append("  - Shortest resolved test path: ").Append(EscapeInline(string.Join(" -> ", path))).Append('\n');
                        }
                    }
                    else if (isCluster)
                    {
                        builder.Append("- Cluster: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                        foreach (var symbol in finding.RelatedSymbols)
                        {
                            builder.Append("  - ").Append(FormatCodeSpan(symbol.SourcePath)).Append(": ")
                                .Append(FormatCodeSpan(symbol.SymbolId)).Append(" (line ")
                                .Append(symbol.Line.ToString(CultureInfo.InvariantCulture)).Append(")\n");
                        }
                    }
                    else
                    {
                        builder.Append("- ").Append(FormatCodeSpan(finding.SubjectId)).Append(" (line ")
                            .Append(finding.StartLine.ToString(CultureInfo.InvariantCulture)).Append(")\n")
                            .Append("  - Signal: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                    }

                    var related = FormatRelated(reviewFinding, visibleViewFindings, allFindings);
                    if (!string.IsNullOrEmpty(related))
                    {
                        // Analysis IDs are validated lowercase slugs; preserve their exact identifiers and the all-findings suffix.
                        builder.Append("  - Related: ").Append(related).Append('\n');
                    }
                }

                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string GetMissingTestSourceStatus(IReadOnlyList<ReviewFinding> findings, string sourcePath)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var sourceChanged = findings.Any(finding => finding.ChangedSourcePaths.Contains(sourcePath, pathComparer));
        return sourceChanged ? "source new or changed" : "source unchanged; included snapshot-wide";
    }

    private static string FormatRelated(
        ReviewFinding finding,
        IReadOnlyList<ReviewFinding> visibleFindings,
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
            if (target is null)
            {
                return reference.AnalysisId;
            }

            var inCurrentView = visibleFindings.Any(candidate => FindingKey(candidate) == FindingKey(target));
            return inCurrentView ? reference.AnalysisId : $"{reference.AnalysisId} (all-findings)";
        }).Distinct(StringComparer.Ordinal).ToArray();

        return string.Join(", ", relatedItems);
    }

    private static string FindingKey(ReviewFinding finding) => finding.AnalysisId + "\0" + finding.Finding.ProjectPath + "\0"
        + finding.Finding.SourcePath + "\0" + finding.Finding.SubjectId + "\0" + finding.Finding.Discriminator;

    private static string QuotePowerShell(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string FormatSignal(string analysisId, FindingDraft finding)
    {
        if (analysisId == "dead-code-candidates")
        {
            return finding.Discriminator == "type-candidate"
                ? "Type without known use"
                : "Method without known use";
        }

        if (analysisId == "method-control-flow-outliers")
        {
            var signal = new StringBuilder();
            var decisionCount = Metric(finding, "decisionCount");
            var decisionCutoff = Metric(finding, "decisionCutoff");
            var decisionConstructCount = Metric(finding, "decisionConstructCount");
            if (decisionCount >= decisionCutoff && decisionConstructCount >= 2)
            {
                signal.Append(FormatNumber(decisionCount)).Append(" decisions across ")
                    .Append(FormatNumber(decisionConstructCount)).Append(" constructs (cutoff ")
                    .Append(FormatNumber(decisionCutoff)).Append(')');
            }

            var nesting = Metric(finding, "maxDecisionNesting");
            var nestingCutoff = Metric(finding, "nestingCutoff");
            if (nesting >= nestingCutoff)
            {
                if (signal.Length > 0)
                {
                    signal.Append("; ");
                }

                signal.Append("nesting ").Append(FormatNumber(nesting)).Append(" (cutoff ")
                    .Append(FormatNumber(nestingCutoff)).Append(')');
            }

            return signal.ToString();
        }

        if (analysisId == "duplicate-code-candidates")
        {
            var similarity = Metric(finding, "similarityScore");
            var minimumSimilarity = Metric(finding, "minimumSimilarityThreshold");
            return $"{FormatNumber(Metric(finding, "memberCount"))} methods; {FormatPercent(similarity)} similarity (minimum {FormatPercent(minimumSimilarity)})";
        }

        if (analysisId == "structural-duplication-candidates")
        {
            return FormatNumber(Metric(finding, "memberCount")) + " occurrences in "
                + FormatNumber(Metric(finding, "executableCount")) + " executable members; "
                + FormatNumber(Metric(finding, "statementCount")) + " statements / "
                + FormatNumber(Metric(finding, "tokenCount")) + " tokens; identical after local/parameter normalization.";
        }

        if (analysisId == "indirection-drift-candidates")
        {
            return FormatNumber(Metric(finding, "forwardingEdgeCount")) + " forwarding edges across "
                + FormatNumber(Metric(finding, "distinctTypeCount")) + " types and "
                + FormatNumber(Metric(finding, "distinctFileCount")) + " files";
        }

        if (analysisId == "non-ascii-identifiers")
        {
            return finding.Rationale;
        }

        if (analysisId == "missing-test-evidence-candidates")
        {
            var category = finding.Discriminator switch
            {
                "no-static-test-path" => "no static test path",
                "indirect-test-path-only" => "indirect test path only",
                _ => finding.Discriminator,
            };
            var signal = new StringBuilder(category)
                .Append("; ").Append(FormatNumber(Metric(finding, "decisionCount"))).Append(" decisions, nesting ")
                .Append(FormatNumber(Metric(finding, "maxDecisionNesting")));
            if (Metric(finding, "attributionUncertain") > 0)
            {
                signal.Append("; attribution uncertain");
            }

            return signal.ToString();
        }

        if (analysisId == "code-size-candidates")
        {
            if (finding.Discriminator == "member-size")
            {
                var signal = new StringBuilder("Member: ")
                    .Append(FormatNumber(Metric(finding, "memberCodeLines"))).Append(" code lines; ")
                    .Append(FormatNumber(Metric(finding, "decisionCount"))).Append(" decisions across ")
                    .Append(FormatNumber(Metric(finding, "decisionConstructCount"))).Append(" constructs; nesting ")
                    .Append(FormatNumber(Metric(finding, "maxDecisionNesting")));
                if (Metric(finding, "relativePathSelected") > 0)
                {
                    signal.Append("; relative length-and-control-flow criterion (minimum ")
                        .Append(FormatNumber(Metric(finding, "minMemberCodeLines"))).Append(", P")
                        .Append(FormatNumber(Metric(finding, "percentile"))).Append(" value ")
                        .Append(FormatNumber(Metric(finding, "memberPercentileValue"))).Append(')');
                }

                if (Metric(finding, "extremePathSelected") > 0)
                {
                    signal.Append("; extreme member-size threshold (")
                        .Append(FormatNumber(Metric(finding, "extremeMemberCodeLines"))).Append(')');
                }

                return signal.ToString();
            }

            if (finding.Discriminator == "type-size")
            {
                var signal = new StringBuilder("Class: ")
                    .Append(FormatNumber(Metric(finding, "typeCodeLines"))).Append(" code lines across ")
                    .Append(FormatNumber(Metric(finding, "typePartCount"))).Append(" declaration parts");
                if (Metric(finding, "relativePathSelected") > 0)
                {
                    signal.Append("; relative type-size criterion (minimum ")
                        .Append(FormatNumber(Metric(finding, "minTypeCodeLines"))).Append(", P")
                        .Append(FormatNumber(Metric(finding, "percentile"))).Append(" value ")
                        .Append(FormatNumber(Metric(finding, "typePercentileValue"))).Append(')');
                }

                if (Metric(finding, "extremePathSelected") > 0)
                {
                    signal.Append("; extreme type-size threshold (")
                        .Append(FormatNumber(Metric(finding, "extremeTypeCodeLines"))).Append(')');
                }

                return signal.ToString();
            }

            if (finding.Discriminator == "file-size")
            {
                var signal = new StringBuilder("File: ")
                    .Append(FormatNumber(Metric(finding, "fileLines"))).Append(" lines; ")
                    .Append(FormatNumber(Metric(finding, "fileUtf8Bytes"))).Append(" UTF-8 bytes");
                if (Metric(finding, "lineCountPathSelected") > 0)
                {
                    signal.Append("; line-count threshold (")
                        .Append(FormatNumber(Metric(finding, "extremeFileLines"))).Append(')');
                }

                if (Metric(finding, "byteCountPathSelected") > 0)
                {
                    signal.Append("; UTF-8 byte-count threshold (")
                        .Append(FormatNumber(Metric(finding, "extremeFileUtf8Bytes"))).Append(')');
                }

                return signal.ToString();
            }
        }

        return finding.Rationale;
    }

    private static double Metric(FindingDraft finding, string name) =>
        finding.Metrics.TryGetValue(name, out var value) ? value : 0;

    private static (string ProjectPath, string Start, string End) ParseStructuralEvidenceDetail(string detail)
    {
        const string projectPrefix = "project=";
        const string startMarker = ";start=";
        const string endMarker = ";end=";
        var startIndex = detail.LastIndexOf(startMarker, StringComparison.Ordinal);
        var endIndex = detail.LastIndexOf(endMarker, StringComparison.Ordinal);
        if (!detail.StartsWith(projectPrefix, StringComparison.Ordinal) || startIndex < projectPrefix.Length || endIndex < startIndex)
        {
            throw new InvalidOperationException("Structural evidence detail does not contain a valid project path and fragment region.");
        }

        try
        {
            var projectPath = JsonSerializer.Deserialize<string>(detail[projectPrefix.Length..startIndex]);
            if (projectPath is null)
            {
                throw new JsonException("Project path is null.");
            }

            return (projectPath,
                detail[(startIndex + startMarker.Length)..endIndex],
                detail[(endIndex + endMarker.Length)..]);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Structural evidence project path is invalid JSON.", exception);
        }
    }

    private static string FormatNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(double value) => (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string FormatOptions(ReviewAnalysisOptions options) => "{" + string.Join(
        ", ", options.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{JsonSerializer.Serialize(pair.Key)}: {FormatJsonValue(pair.Value)}")) + "}";

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

    private static string FormatCodeSpan(string value)
    {
        var content = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\n', ' ');
        var longestBacktickRun = 0;
        var currentBacktickRun = 0;
        foreach (var character in content)
        {
            if (character == '`')
            {
                currentBacktickRun++;
                longestBacktickRun = Math.Max(longestBacktickRun, currentBacktickRun);
            }
            else
            {
                currentBacktickRun = 0;
            }
        }

        var delimiter = new string('`', longestBacktickRun + 1);
        var needsPadding = content.Length > 0 && (content[0] is '`' or ' ' || content[^1] is '`' or ' ');
        return needsPadding
            ? delimiter + " " + content + " " + delimiter
            : delimiter + content + delimiter;
    }

    private static string EscapeInline(string value)
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

    private static string EscapeLinkText(string value) => EscapeInline(value).Replace("\\-", "-", StringComparison.Ordinal);

    private static string EncodePathSegment(string segment) => Uri.EscapeDataString(segment);

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
