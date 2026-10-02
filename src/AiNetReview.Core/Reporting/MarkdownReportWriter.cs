namespace AiNetReview.Core.Reporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        ValidateFindingIds(findings);
        var changedFindingIds = GetFindingIds(changedFindings);
        var allFindingIds = GetFindingIds(findings);
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
                    foreach (var area in FindingAreas)
                    {
                        var changedArea = analysisChangedFindings.Where(finding => GetFindingArea(finding) == area).ToArray();
                        var allArea = allFindings.Where(finding => GetFindingArea(finding) == area).ToArray();
                        await WriteViewAnalysisAsync(temporaryPath, config.ProjectRoot, area, "changed-files", configuredAnalysis, changedArea, changedFindings, findings,
                                changedFindingIds, cancellationToken)
                            .ConfigureAwait(false);
                        await WriteViewAnalysisAsync(temporaryPath, config.ProjectRoot, area, "all-findings", configuredAnalysis, allArea, findings, findings,
                                allFindingIds, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                foreach (var area in FindingAreas)
                {
                    await WriteViewIndexAsync(temporaryPath, area, "changed-files", "Changed files", analyses,
                            changedFindings.Where(finding => GetFindingArea(finding) == area).ToArray(),
                            changedOnly: true, cancellationToken)
                        .ConfigureAwait(false);
                    await WriteViewIndexAsync(temporaryPath, area, "all-findings", "All findings", analyses,
                            findings.Where(finding => GetFindingArea(finding) == area).ToArray(),
                            changedOnly: false, cancellationToken)
                        .ConfigureAwait(false);
                }

                await AuditMapReportWriter.WriteAuditMapAsync(temporaryPath, runId, config.ProjectRoot, changedFindings,
                    "changed-files", cancellationToken).ConfigureAwait(false);
                await AuditMapReportWriter.WriteAuditMapAsync(temporaryPath, runId, config.ProjectRoot, findings,
                    "all-findings", cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await WriteUtf8Async(indexPath, FormatIndex(runId, config, config.AllAnalyses.OrderBy(static item => item.AnalysisId, StringComparer.Ordinal).ToArray(), result, findings, changedFindings, configurationPath, baselineCommandContext), cancellationToken)
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

    internal static async Task WriteUtf8Async(string path, string content, CancellationToken cancellationToken)
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
        ReviewRunResult result,
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
            .Append("Findings are potential review signals that may point to deeper or cross-cutting problems. First read the target repository's applicable instructions and relevant design documents. Investigate every finding in the commissioned working set using relevant source code, callers, contracts, and tests, in the context of application goals, architecture, and responsibilities. Related findings may be evaluated together. Do not dismiss a signal solely because it is heuristic or its attribution is uncertain. Justify each classification with concrete evidence: false positive, acceptable design, needs clarification, or actionable. An accurate signal can describe an acceptable design; distinguish that from a false positive. A signal alone does not require a change; changes must follow from this assessment. Avoid metric-driven refactoring and symptom workarounds; make a local change when the broader context supports it. Explain consequential changes and tradeoffs to the user. The goal is to support understandable, reliable agentic development and help prevent drift, not to claim that the analysis proves drift. A normal unbounded audit covers production, tests, and mixed findings in all three changed-files areas. A user-limited assignment must name the remaining areas as unreviewed. Findings are measurements on non-generated C# candidates in the loaded snapshot; they do not prove runner discovery, execution, runtime coverage, test quality, or defects. The missing-test-evidence analysis still targets production functions; the other seven maintenance analyses include both project roles. Dead-code `apiSurface` applies equally to production and test libraries. Scope or option changes do not make source files changed; a complete reevaluation after such a change needs an explicitly requested full-repository audit. Do not inspect or report findings from any `all-findings/` area unless the user explicitly requests a full repository audit.\n\n")
            .Append("For test findings, examine the behavior under test, assertion strength, whether expected results are independent of production logic, isolation, failure localization, and the role of setup, fixtures, hooks, data providers, fakes, mocks, builders, and helpers. Distinguish executable code from declarative test data and string fixtures. For mixed findings, assess production behavior and tests together, especially whether expectations independently verify the implementation. Do not mechanically split tests, merge scenarios, remove infrastructure, or refactor solely to lower a metric; change code only when contextual evidence supports it.\n\n");

        builder.Append("## Audit map\n\n")
            .Append("The [changed-files audit map](audit-map/changed-files/index.md) lists every selected finding once, grouped by project and representative source file. Use each finding's direct link to inspect its original signal and evidence, then examine the relevant implementation, callers, contracts, and tests. For each finding, report its ID, classification (false positive, acceptable design, needs clarification, or actionable), concrete evidence, and unresolved context. Name unreviewed findings or source-file groups explicitly; a partial map review is not a complete audit. The [all-findings map](audit-map/all-findings/index.md) is reference-only and requires an explicitly requested full-repository audit.\n\n");

        var changedCount = changedFindings.Count;
        var allCount = findings.Count;
        if (allCount == 0)
        {
            builder.Append(analyses.All(static analysis => !analysis.Enabled)
                ? "No review was performed because all analyses are disabled.\n\n"
                : "No findings were found.\n\n");
        }

        if (analyses.Any(static analysis => analysis.Enabled && analysis.AnalysisId == "missing-test-evidence-candidates"))
        {
            builder.Append("Changed-files selection for `missing-test-evidence-candidates` follows the complete C# snapshot: without a baseline it shows every current finding; with a baseline it shows every current finding when any C# path was added, changed, or deleted, and none when the C# snapshot is unchanged. Other analyses keep their file-based selection.\n\n");
        }

        builder.Append("## Audit scope\n\n")
            .Append("Counts are per view and are not additive between changed-files and all-findings. The global project/source-file counts below use the unique representative project/file pairs; area counts can overlap those pairs.\n\n")
            .Append("| Area | Changed-file findings | All findings |\n| --- | ---: | ---: |\n");
        foreach (var area in FindingAreas)
        {
            var changed = changedFindings.Count(finding => GetFindingArea(finding) == area);
            var all = findings.Count(finding => GetFindingArea(finding) == area);
            builder.Append("| ").Append(area).Append(" | ").Append(changed.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(all.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }

        builder.Append("\nA normal unbounded assignment includes all three `changed-files` areas. A limited assignment must identify every remaining area as unreviewed. `all-findings` requires an explicit full-repository audit request.\n\n");
        foreach (var area in FindingAreas)
        {
            builder.Append("- [").Append(area).Append(" changed-files (")
                .Append(changedFindings.Count(finding => GetFindingArea(finding) == area).ToString(CultureInfo.InvariantCulture))
                .Append(")](").Append(area).Append("/changed-files/index.md)\n");
        }
        builder.Append("- [Audit map: changed-files](audit-map/changed-files/index.md)\n");

        builder.Append("\n## Complete findings (reference only)\n\n")
            .Append("> **Agent instruction:** Do not inspect, summarize, or display findings from any `all-findings` area unless the user explicitly requests an audit of the entire repository.\n\n")
            .Append(allCount.ToString(CultureInfo.InvariantCulture)).Append(" findings across all areas.\n\n");
        foreach (var area in FindingAreas)
        {
            builder.Append("- [").Append(area).Append(" all-findings (")
                .Append(findings.Count(finding => GetFindingArea(finding) == area).ToString(CultureInfo.InvariantCulture))
                .Append(")](").Append(area).Append("/all-findings/index.md)\n");
        }
        builder.Append("- [Audit map: all-findings](audit-map/all-findings/index.md) (reference only)\n");

        builder.Append("\n## Loaded C# projects\n\n| Project | Classified role | Classification reason |\n| --- | --- | --- |\n");
        foreach (var project in result.ProjectClassifications.OrderBy(static item => item.ProjectPath, StringComparer.Ordinal))
        {
            builder.Append("| ").Append(EscapeInline(project.ProjectPath)).Append(" | ")
                .Append(project.Role == ProjectRole.Tests ? "tests" : "production").Append(" | ")
                .Append(EscapeInline(project.Reason.ToString())).Append(" |\n");
        }

        builder.Append("\n## Analyses and effective options\n\n| Analysis | Enabled | Applicability to tests | Effective options | Test option provenance |\n| --- | --- | --- | --- | --- |\n");
        foreach (var analysis in analyses)
        {
            var enabled = analysis.Enabled;
            var effective = FormatOptions(analysis.EffectiveOptions);
            var testScope = analysis.AnalysisId == "missing-test-evidence-candidates"
                ? "Production-only targets; test projects are evidence sources"
                : "Production and tests";
            if (analysis.EffectiveTestOptions is { } testOptions)
            {
                effective += " / tests " + FormatOptions(testOptions);
            }

            var provenance = analysis.Analysis.Descriptor.TestOptions.Count == 0
                ? "—"
                : string.Join("; ", analysis.Analysis.Descriptor.TestOptions.Select(option =>
                    option.Name + (analysis.ExplicitTestOptions?.GetValueOrDefault(option.Name) == true ? " explicit" : " inherited")));
            builder.Append("| `").Append(analysis.AnalysisId).Append("` | ").Append(enabled ? "yes" : "no").Append(" | ").Append(testScope)
                .Append(" | ").Append(FormatCodeSpan(effective)).Append(" | ").Append(EscapeInline(provenance)).Append(" |\n");
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
                builder.Append("| `").Append(exclusion.AnalysisId).Append("` | `")
                    .Append(EscapeInline(exclusion.ProjectPath)).Append("` | ")
                    .Append(EscapeInline(exclusion.Reason)).Append(" |\n");
            }
            builder.Append('\n');
        }

        builder.Append("## Global totals\n\n")
            .Append("Changed-files: ").Append(changedCount.ToString(CultureInfo.InvariantCulture)).Append(" findings across ")
            .Append(changedFindings.Select(static finding => (finding.Finding.ProjectPath, finding.Finding.SourcePath)).Distinct().Count()
                .ToString(CultureInfo.InvariantCulture)).Append(" unique representative project/file pairs; all-findings: ")
            .Append(allCount.ToString(CultureInfo.InvariantCulture)).Append(" findings; all-findings contain ")
            .Append(findings.Select(static finding => (finding.Finding.ProjectPath, finding.Finding.SourcePath)).Distinct().Count()
                .ToString(CultureInfo.InvariantCulture))
            .Append(" unique representative project/file pairs.\n\n");

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
        string area,
        string viewDirectory,
        string title,
        ConfiguredReviewAnalysis[] analyses,
        IReadOnlyList<ReviewFinding> findings,
        bool changedOnly,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(area).Append(" — ").Append(title).Append("\n\n")
            .Append("This index covers ").Append(area).Append(" findings in this view. Related findings in other areas remain visible in their own index. See the [shared audit guidance](../../index.md#review-guidance).\n\n");
        if (!changedOnly)
        {
            builder.Append("> **Notice for AI agents:** This area contains current findings for reference. Do not review or report them unless the user explicitly requested a full repository audit. Use [`changed-files/`](../changed-files/index.md) for active review.\n\n");
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

        var directory = Path.Combine(runDirectory, area, viewDirectory);
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

    private static IReadOnlyDictionary<string, string> GetFindingIds(IEnumerable<ReviewFinding> findings) =>
        findings.ToDictionary(FindingKey, GetFindingId, StringComparer.Ordinal);

    private static void ValidateFindingIds(IReadOnlyList<ReviewFinding> findings)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            if (!identities.Add(FindingKey(finding)) || !ids.Add(GetFindingId(finding)))
            {
                throw new InvalidOperationException("Review findings must have unique identities and stable IDs.");
            }
        }
    }

    internal static string GetFindingArea(ReviewFinding finding)
    {
        var roles = finding.SubjectOccurrences.Select(static occurrence => occurrence.Role).Distinct().ToArray();
        if (roles.Contains(ProjectRole.Production) && roles.Contains(ProjectRole.Tests))
        {
            return "mixed";
        }

        return roles.Contains(ProjectRole.Tests) ? "tests" : "production";
    }

    private static bool IsChangedForReport(ReviewFinding finding, ReviewRunResult result) =>
        finding.AnalysisId == "missing-test-evidence-candidates"
            ? result.HasCSharpSnapshotChanges != false
            : finding.IsChanged;

    private static async Task WriteViewAnalysisAsync(
        string runDirectory,
        string projectRoot,
        string area,
        string viewDirectory,
        ConfiguredReviewAnalysis configuredAnalysis,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> visibleViewFindings,
        IReadOnlyList<ReviewFinding> allFindings,
        IReadOnlyDictionary<string, string> findingIds,
        CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
        {
            return;
        }

        var reportDirectory = Path.Combine(runDirectory, area, viewDirectory);
        Directory.CreateDirectory(reportDirectory);
        var analysisPath = Path.Combine(reportDirectory, configuredAnalysis.AnalysisId + ".md");
        await WriteUtf8Async(analysisPath, FormatAnalysisReport(configuredAnalysis, projectRoot, analysisPath, area, viewDirectory, findings, visibleViewFindings, allFindings, findingIds), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatAnalysisReport(
        ConfiguredReviewAnalysis configuredAnalysis,
        string projectRoot,
        string reportPath,
        string area,
        string viewDirectory,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> visibleViewFindings,
        IReadOnlyList<ReviewFinding> allFindings,
        IReadOnlyDictionary<string, string> findingIds)
    {
        var descriptor = configuredAnalysis.Analysis.Descriptor;
        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeLinkText(descriptor.Title)).Append("\n\n")
            .Append(EscapeInline(descriptor.Purpose)).Append("\n\n")
            .Append(viewDirectory == "all-findings"
                ? "Review policy: In the commissioned scope, investigate every finding and justify its classification; a signal alone does not require a change. This area is reference-only; inspect or report it only when the user explicitly requests a full repository audit. See the [root index's Review guidance](../../index.md#review-guidance).\n\n"
                : "Review policy: In the commissioned scope, investigate every finding and justify its classification; a signal alone does not require a change. A normal unbounded audit includes all three areas; see the [root index's Review guidance](../../index.md#review-guidance).\n\n")
            .Append(FormatEffectiveOptions(configuredAnalysis)).Append("\n\n")
            .Append("Review questions:\n\n");
        if (area is "tests" or "mixed")
        {
            builder.Append("- Does the test verify observable behavior with meaningful assertions?\n")
                .Append("- Is the expected result independent of the production implementation?\n")
                .Append("- Are setup and test paths isolated, understandable, and easy to diagnose when they fail?\n")
                .Append("- What responsibility does each fixture, hook, data provider, fake, mock, builder, or helper add?\n")
                .Append("- Which parts are executable code, and which are declarative test data or string fixtures?\n");
            if (area == "mixed")
            {
                builder.Append("- Do the expectations independently check the production behavior represented by this mixed finding?\n");
            }
        }
        foreach (var question in descriptor.ReviewQuestions)
        {
            builder.Append("- ").Append(EscapeInline(question)).Append('\n');
        }

        if (configuredAnalysis.AnalysisId is "method-control-flow-outliers" or "code-size-candidates" or "missing-test-evidence-candidates")
        {
            builder.Append("\nControl-flow counting: `decisionCount` counts each `if`, conditional expression, loop, and `catch` once, and each switch section or switch-expression arm once. `decisionConstructCount` counts each `if`, conditional expression, loop, and `catch` once and each entire switch once. Nesting is the maximum depth of counted decisions (`else if` chains stay at the same depth). Operators such as `&&`, `||`, and `??` do not add decisions; these measures are not cyclomatic complexity.\n");
        }

        if (configuredAnalysis.AnalysisId == "method-control-flow-outliers")
        {
            builder.Append("\nSelection: Within each C# project, decision-count and maximum-nesting populations have separate nearest-rank values at the effective `percentile`. Inclusive cutoffs are `max(8, decision percentile)` and `max(4, nesting percentile)`. A method is selected when `decisionCount >= decision cutoff AND decisionConstructCount >= 2`, or `maxDecisionNesting >= nesting cutoff`. Test projects use effective test options shown above.\n");
        }

        if (configuredAnalysis.AnalysisId == "code-size-candidates")
        {
            builder.Append("\nCode-size counting and selection: Member code lines are distinct physical source lines with a non-missing C# token start in the full executable declaration; tokenless comment and blank lines do not count, while signature, attributes, and braces count where their tokens start. A multiline literal counts its token-start line; continuation lines count only if another token starts there. Type code lines sum the same token-start line counts across each non-generated part of an explicit class or record class symbol, excluding nested types and delegates. File lines are physical source lines; file bytes are UTF-8 bytes without a BOM.\n\n")
                .Append("Within each C# project, members meet the relative size criterion when `memberCodeLines >= max(minMemberCodeLines, project nearest-rank memberCodeLines value at percentile)` and `((decisionCount >= 8 AND decisionConstructCount >= 2) OR maxDecisionNesting >= 4)`; `extremeMemberCodeLines` is an independent inclusive threshold. Types meet the relative criterion at `typeCodeLines >= max(minTypeCodeLines, project nearest-rank typeCodeLines value at percentile)` or the independent `extremeTypeCodeLines` threshold. Files meet either inclusive threshold: `fileLines >= extremeFileLines` or `fileUtf8Bytes >= extremeFileUtf8Bytes`.\n");
        }

        if (configuredAnalysis.AnalysisId == "duplicate-code-candidates")
        {
            builder.Append("\nSimilarity presets: `exact` = 0.95, `near` = 0.80, and `fuzzy` = 0.65; `exact` is the strictest preset, not exact identity. Similarity is Jaccard over distinct fixed five-token n-gram sets from method bodies. Whitespace and comments are ignored; identifier and literal token text is retained, with no identifier or local-name normalization.\n");
        }

        if (configuredAnalysis.AnalysisId == "structural-duplication-candidates")
        {
            builder.Append("\nContainment suppression removes a smaller fragment only when every occurrence is contained in an occurrence of a larger qualifying group; a smaller group with any additional occurrence remains reportable.\n");
        }

        if (configuredAnalysis.AnalysisId == "missing-test-evidence-candidates")
        {
            builder.Append("\nSelection: A production function meets the nontrivial gate when `decisionCount >= minDecisionCount OR maxDecisionNesting >= minDecisionNesting`. A direct resolved static test path suppresses a finding. A function with no resolved static test path is reported at the nontrivial gate; an indirect-path-only function must also meet `decisionCount >= minIndirectDecisionCount OR maxDecisionNesting >= minIndirectDecisionNesting`.\n\n")
                .Append("This is static test-path evidence from the loaded snapshot, not runtime coverage. The `attribution uncertain` marker means the static test association may be incomplete; it can result from reachable unresolved bindings, method groups, or virtual/interface dispatch, and may propagate to downstream methods over known calls. A reachable global uncertainty input can mark every function, so the marker alone neither means a test is missing nor that the marked function itself has an unresolved binding. It does not assess test assertion quality. Reflection, dependency injection, external test projects, dynamic dispatch, branch execution, and custom test discovery can hide associations.\n");
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
            builder.Append("### Project: ").Append(EscapeInline(projectGroup.Key)).Append(" (")
                .Append(roleLabel).Append("; ")
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
                builder.Append("Source: [open ").Append(EscapeLinkText(group.SourcePath)).Append("](")
                    .Append(FormatSourceLink(reportPath, projectRoot, group.SourcePath)).Append(")\n\n");
                foreach (var reviewFinding in group.Findings)
                {
                    var finding = reviewFinding.Finding;
                    if (findingIds.TryGetValue(FindingKey(reviewFinding), out var findingId))
                    {
                        builder.Append("<a id=\"finding-").Append(findingId).Append("\"></a>\n");
                    }

                    var isCluster = finding.RelatedSymbols.Count > 1;
                    var occurrenceRoles = reviewFinding.SubjectOccurrences
                        .Select(static occurrence => occurrence.Role)
                        .Distinct()
                        .OrderBy(static role => role)
                        .Select(static role => role == ProjectRole.Tests ? "tests" : "production")
                        .ToArray();
                    if (occurrenceRoles.Length > 0)
                    {
                        builder.Append("- Finding origin: ").Append(string.Join(" + ", occurrenceRoles)).Append('\n');
                    }
                    if (reviewFinding.AnalysisId == "indirection-drift-candidates")
                    {
                        builder.Append("- Forwarding path: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        foreach (var member in finding.Evidence)
                        {
                            builder.Append("  - [").Append(EscapeLinkText(member.SourcePath)).Append("](")
                                .Append(FormatSourceLink(reportPath, projectRoot, member.SourcePath)).Append("):")
                                .Append(member.Line.ToString(CultureInfo.InvariantCulture)).Append(": ")
                                .Append(FormatCodeSpan(member.Label)).Append(" (")
                                .Append(GetOccurrenceRole(reviewFinding, finding.ProjectPath, member.SourcePath, member.Label)).Append(")\n");
                        }
                    }
                    else if (reviewFinding.AnalysisId == "structural-duplication-candidates")
                    {
                        builder.Append("- Structural duplicate: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        foreach (var occurrence in finding.Evidence)
                        {
                            var (projectPath, start, end) = ParseStructuralEvidenceDetail(occurrence.Detail);
                            builder.Append("  - Project ").Append(FormatCodeSpan(projectPath))
                                .Append(", file [").Append(EscapeLinkText(occurrence.SourcePath)).Append("](")
                                .Append(FormatSourceLink(reportPath, projectRoot, occurrence.SourcePath)).Append(")")
                                .Append(": ").Append(FormatCodeSpan(occurrence.Label))
                                .Append(" (start ").Append(FormatCodeSpan(start)).Append("; end-exclusive ")
                                .Append(FormatCodeSpan(end)).Append("; ")
                                .Append(GetOccurrenceRole(reviewFinding, projectPath, occurrence.SourcePath, occurrence.Label)).Append(")\n");
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
                                .Select(evidence => $"{FormatCodeSpan(evidence.Label)} ({FormatCodeSpan(evidence.SourcePath + ":" + evidence.Line.ToString(CultureInfo.InvariantCulture))}; {GetEvidenceOccurrenceRole(reviewFinding, evidence)})");
                            builder.Append("  - Shortest resolved test path: ").Append(string.Join(" -> ", path)).Append('\n');
                        }
                    }
                    else if (isCluster)
                    {
                        builder.Append("- Cluster: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                        foreach (var symbol in finding.RelatedSymbols)
                        {
                            builder.Append("  - [").Append(EscapeLinkText(symbol.SourcePath)).Append("](")
                                .Append(FormatSourceLink(reportPath, projectRoot, symbol.SourcePath)).Append("): ")
                                .Append(FormatCodeSpan(symbol.SymbolId)).Append(" (line ")
                                .Append(symbol.Line.ToString(CultureInfo.InvariantCulture)).Append("; ")
                                .Append(GetOccurrenceRole(reviewFinding, symbol)).Append(")\n");
                        }
                    }
                    else
                    {
                        builder.Append("- ").Append(FormatCodeSpan(finding.SubjectId)).Append(" (line ")
                            .Append(finding.StartLine.ToString(CultureInfo.InvariantCulture)).Append(")\n")
                            .Append("  - Signal: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                    }

                    var related = FormatRelated(reviewFinding, viewDirectory, visibleViewFindings, allFindings);
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
        string currentView,
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
            var targetArea = GetFindingArea(target);
            var targetView = inCurrentView ? currentView : "all-findings";
            var suffix = inCurrentView ? string.Empty : " (all-findings reference)";
            var link = "../../" + targetArea + "/" + targetView + "/" + EncodePathSegment(reference.AnalysisId) + ".md";
            return "[" + reference.AnalysisId + " (" + targetArea + "/" + targetView + ")](" + link + ")" + suffix;
        }).Distinct(StringComparer.Ordinal).ToArray();

        return string.Join(", ", relatedItems);
    }

    internal static string FindingKey(ReviewFinding finding) => finding.AnalysisId + "\0" + finding.Finding.ProjectPath + "\0"
        + finding.Finding.SourcePath + "\0" + finding.Finding.SubjectId + "\0" + finding.Finding.Discriminator;

    internal static string GetFindingId(ReviewFinding finding)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FindingKey(finding)));
        return "finding-" + Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
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

    internal static string FormatSourceLink(string reportPath, string projectRoot, string sourcePath)
    {
        var absoluteSource = Path.GetFullPath(sourcePath, projectRoot);
        var relative = Path.GetRelativePath(Path.GetDirectoryName(reportPath)!, absoluteSource).Replace('\\', '/');
        return string.Join('/', relative.Split('/').Select(EncodePathSegment));
    }

    private static string QuotePowerShell(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    internal static string FormatSignal(string analysisId, FindingDraft finding)
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

    private static string FormatEffectiveOptions(ConfiguredReviewAnalysis configuredAnalysis)
    {
        var testOptions = configuredAnalysis.EffectiveTestOptions;
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

        var sources = testDescriptors.Select(option =>
        {
            var isExplicit = configuredAnalysis.ExplicitTestOptions?.GetValueOrDefault(option.Name) ?? false;
            return FormatCodeSpan(option.Name) + (isExplicit ? " explicitly configured" : " inherited");
        });
        builder.Append("Test option sources: ").Append(string.Join("; ", sources)).Append(".\n\n");
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

    internal static string FormatCodeSpan(string value)
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

    internal static string EncodePathSegment(string segment) => Uri.EscapeDataString(segment);

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
