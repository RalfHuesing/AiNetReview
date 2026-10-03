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
                            && GetFindingArea(finding) == area).ToArray();
                        await WriteAnalysisAsync(temporaryPath, area, configuredAnalysis, areaFindings, findings,
                                findingIds, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                foreach (var area in FindingAreas)
                {
                    await WriteAreaIndexAsync(temporaryPath, area, analyses,
                            findings.Where(finding => GetFindingArea(finding) == area).ToArray(), cancellationToken)
                        .ConfigureAwait(false);
                }

                await MapsReportWriter.WriteMapsAsync(temporaryPath, result.Maps, cancellationToken).ConfigureAwait(false);
                await AuditMapReportWriter.WriteAuditMapAsync(temporaryPath, runId, findings, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await WriteUtf8Async(indexPath, FormatIndex(runId, config, config.AllAnalyses.OrderBy(static item => item.AnalysisId, StringComparer.Ordinal).ToArray(), result, findings), cancellationToken)
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
            var count = findings.Count(finding => GetFindingArea(finding) == area);
            builder.Append("| ").Append(area).Append(" | ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }
        builder.Append("\n");
        foreach (var area in FindingAreas)
        {
            builder.Append("- ").Append(FormatCodeSpan(area + "/index.md")).Append('\n');
        }
        builder.Append("- ").Append(FormatCodeSpan("maps/index.md")).Append('\n');

        builder.Append("\n## Analyses and effective options\n\n| Analysis | Enabled | Subject scope | Effective options |\n| --- | --- | --- | --- |\n");
        foreach (var analysis in analyses)
        {
            var effective = analysis.EffectiveOptions.Values.Count == 0 ? "—" : FormatOptions(analysis.EffectiveOptions);
            var testScope = analysis.AnalysisId switch
            {
                "missing-test-evidence-candidates" => "Production targets; tests are evidence",
                "type-dependency-cycle-candidates" or "type-dependency-hub-candidates" => "Production",
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
                builder.Append("- ").Append(FormatCodeSpan(EncodePathSegment(analysis.AnalysisId) + ".md"))
                    .Append(" ( ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" findings)\n");
            }
        }

        var directory = Path.Combine(runDirectory, area);
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
            return new ReviewFinding(analysis.AnalysisId, finding, paths, Array.Empty<ReviewFindingReference>());
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
        await WriteUtf8Async(analysisPath, FormatAnalysisReport(configuredAnalysis, analysisPath, findings, allFindings, findingIds), cancellationToken)
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

        var allMissingTestFindingsAreUncertain = configuredAnalysis.AnalysisId == "missing-test-evidence-candidates"
            && findings.Count > 0
            && findings.All(static item => Metric(item.Finding, "attributionUncertain") > 0);
        if (allMissingTestFindingsAreUncertain)
        {
            builder.Append("\nAttribution uncertain: every finding in this report has uncertain static test attribution.\n");
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
        }

        if (configuredAnalysis.AnalysisId == "type-dependency-cycle-candidates")
        {
            builder.Append("\nSelection: A finding represents one maximal strongly connected component in the production type graph. It must contain at least three distinct types and at least three distinct canonical declaration source files; both floors are inclusive. Every internal directed edge and retained source witness is listed, along with every participating type declaration and one genuine deterministic cycle as an example. Counts use distinct type pairs and canonical declaration paths. Test, generated, metadata, dynamic, and implicit compiler-created types are outside the graph. This is a static dependency signal, not proof of runtime recursion or an architectural violation.\n");
        }

        if (configuredAnalysis.AnalysisId == "type-dependency-hub-candidates")
        {
            builder.Append("\nSelection: A production type is reported when its distinct direct production consumer count meets `minFanIn` **and** its distinct direct production dependency count meets `minFanOut`; both inclusive thresholds default to 10 and are configured independently. Neighbor file counts use distinct canonical declaration paths, while project counts retain project-specific types. Every direct production neighbor and retained edge witness is listed. Direct test consumers are separate context and do not affect production counts or finding origin. Generated, metadata, dynamic, and implicit compiler-created types are excluded. This static neighborhood does not prove responsibility concentration or runtime behavior.\n");
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
                    var isCluster = finding.RelatedSymbols.Count > 1;
                    var isDuplicateCluster = isCluster && reviewFinding.AnalysisId == "duplicate-code-candidates";
                    var hasOccurrenceList = isDuplicateCluster
                        || reviewFinding.AnalysisId is "indirection-drift-candidates" or "structural-duplication-candidates"
                        || reviewFinding.AnalysisId == "code-size-candidates" && finding.Discriminator == "file-size";
                    if (findingIds.TryGetValue(FindingKey(reviewFinding), out var findingId))
                    {
                        builder.Append("- ").Append(findingId).Append('\n');
                        if (!hasOccurrenceList)
                        {
                            builder.Append("  - Subject: ").Append(FormatCodeSpan(FormatSymbolIdentity(finding.SubjectId))).Append(" at ")
                                .Append(FormatFindingLocation(group.SourcePath, finding.SourcePath, finding.StartLine)).Append('\n');
                        }
                    }

                    if (reviewFinding.AnalysisId == "indirection-drift-candidates")
                    {
                        builder.Append("  - Forwarding path: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        foreach (var member in finding.Evidence)
                        {
                            builder.Append("    - ").Append(FormatFindingLocation(group.SourcePath, member.SourcePath, member.Line)).Append(" ")
                                .Append(FormatCodeSpan(FormatSymbolIdentity(member.Label))).Append(" (")
                                .Append(GetOccurrenceRole(reviewFinding, finding.ProjectPath, member.SourcePath, member.Label)).Append(")\n");
                        }
                    }
                    else if (reviewFinding.AnalysisId == "structural-duplication-candidates")
                    {
                        builder.Append("  - Structural duplicate: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        foreach (var occurrence in finding.Evidence)
                        {
                            var (projectPath, start, end) = ParseStructuralEvidenceDetail(occurrence.Detail);
                            var occurrenceRole = GetOccurrenceRole(reviewFinding, projectPath, occurrence.SourcePath, occurrence.Label);
                            builder.Append("    - ");
                            if (!string.Equals(projectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(FormatCodeSpan(projectPath)).Append(' ');
                            }
                            if (!string.Equals(group.SourcePath.Replace('\\', '/'), occurrence.SourcePath.Replace('\\', '/'), StringComparison.Ordinal))
                            {
                                builder.Append(FormatSourceLocation(occurrence.SourcePath, 0)).Append(' ');
                            }
                            builder.Append(FormatCodeSpan(FormatSymbolIdentity(occurrence.Label)))
                                .Append(" [").Append(FormatCodeSpan(start)).Append("–").Append(FormatCodeSpan(end)).Append(')');
                            if (!string.Equals(projectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(" (").Append(occurrenceRole).Append(')');
                            }
                            builder.Append('\n');
                        }
                    }
                    else if (reviewFinding.AnalysisId == "missing-test-evidence-candidates")
                    {
                        builder.Append("  - Signal: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding,
                            allMissingTestFindingsAreUncertain))).Append('\n');
                        if (finding.Discriminator == "indirect-test-path-only" && finding.Evidence.Count > 1)
                        {
                            var path = finding.Evidence.Skip(1)
                                .Select(evidence => $"{FormatCodeSpan(FormatSymbolIdentity(evidence.Label))} ({FormatFindingLocation(group.SourcePath, evidence.SourcePath, evidence.Line)}; {GetEvidenceOccurrenceRole(reviewFinding, evidence)})");
                            builder.Append("  - Shortest resolved test path: ").Append(string.Join(" -> ", path)).Append('\n');
                        }
                    }
                    else if (reviewFinding.AnalysisId == "type-dependency-cycle-candidates")
                    {
                        builder.Append("  - Dependency group: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        builder.Append("  - Example cycle and review question: ").Append(EscapeInline(finding.Rationale)).Append('\n');
                        builder.Append("  - Participating declarations and internal edge witnesses:\n");
                        foreach (var item in finding.Evidence)
                        {
                            builder.Append("    - ").Append(FormatFindingLocation(group.SourcePath, item.SourcePath, item.Line)).Append(" ")
                                .Append(FormatCodeSpan(FormatSymbolIdentity(item.Label))).Append(" — ").Append(EscapeInline(item.Detail)).Append('\n');
                        }
                    }
                    else if (reviewFinding.AnalysisId == "type-dependency-hub-candidates")
                    {
                        builder.Append("  - Dependency hub: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                        builder.Append("  - Neighborhood and review question: ").Append(EscapeInline(finding.Rationale)).Append('\n');
                        builder.Append("  - Type declarations, direct production neighbors, and separate test consumers:\n");
                        foreach (var item in finding.Evidence)
                        {
                            builder.Append("    - ").Append(FormatFindingLocation(group.SourcePath, item.SourcePath, item.Line)).Append(" ")
                                .Append(FormatCodeSpan(FormatSymbolIdentity(item.Label))).Append(" — ").Append(EscapeInline(item.Detail)).Append('\n');
                        }
                    }
                    else if (isDuplicateCluster)
                    {
                        builder.Append("  - Cluster: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                        var renderedSymbols = new HashSet<FindingSymbol>();
                        foreach (var evidence in finding.Evidence)
                        {
                            var evidenceIdentity = GetDuplicateEvidenceIdentity(evidence.Detail);
                            var candidates = finding.RelatedSymbols.Where(candidate =>
                                candidate.SourcePath == evidence.SourcePath && candidate.Line == evidence.Line).ToArray();
                            var symbol = candidates.FirstOrDefault(candidate => candidate.SymbolId == evidenceIdentity
                                    && evidence.Detail.Contains("in project '" + candidate.ProjectPath + "'", StringComparison.Ordinal))
                                ?? candidates.FirstOrDefault(candidate => candidate.SymbolId == evidenceIdentity)
                                ?? candidates.FirstOrDefault(candidate => candidate.SymbolId == evidence.Label)
                                ?? candidates.FirstOrDefault(candidate => evidence.Detail.Contains(
                                    "in project '" + candidate.ProjectPath + "'", StringComparison.Ordinal))
                                ?? candidates.FirstOrDefault();
                            if (symbol is not null)
                            {
                                renderedSymbols.Add(symbol);
                            }
                            var projectPath = symbol?.ProjectPath ?? finding.ProjectPath;
                            builder.Append("    - ");
                            if (!string.Equals(projectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(FormatCodeSpan(projectPath)).Append(' ');
                            }
                            builder.Append(FormatFindingLocation(group.SourcePath, evidence.SourcePath, evidence.Line)).Append(' ')
                                .Append(FormatCodeSpan(FormatSymbolIdentity(symbol?.SymbolId ?? evidenceIdentity)))
                                .Append(" — ").Append(EscapeInline(FormatDuplicateEvidenceDetail(evidence.Detail)));
                            if (!string.Equals(projectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(" (").Append(GetOccurrenceRole(reviewFinding, projectPath, evidence.SourcePath, symbol?.SymbolId ?? string.Empty)).Append(')');
                            }
                            builder.Append('\n');
                        }
                        foreach (var symbol in finding.RelatedSymbols.Where(symbol => !renderedSymbols.Contains(symbol)))
                        {
                            builder.Append("    - ");
                            if (!string.Equals(symbol.ProjectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(FormatCodeSpan(symbol.ProjectPath)).Append(' ');
                            }
                            builder.Append(FormatFindingLocation(group.SourcePath, symbol.SourcePath, symbol.Line)).Append(' ')
                                .Append(FormatCodeSpan(FormatSymbolIdentity(symbol.SymbolId)));
                            if (!string.Equals(symbol.ProjectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(" (").Append(GetOccurrenceRole(reviewFinding, symbol)).Append(')');
                            }
                            builder.Append('\n');
                        }
                    }
                    else if (isCluster)
                    {
                        builder.Append("  - Cluster: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                        foreach (var symbol in finding.RelatedSymbols)
                        {
                            builder.Append("  - ");
                            if (!string.Equals(symbol.ProjectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(FormatCodeSpan(symbol.ProjectPath)).Append(' ');
                            }
                            builder.Append(FormatFindingLocation(group.SourcePath, symbol.SourcePath, symbol.Line)).Append(' ')
                                .Append(FormatCodeSpan(FormatSymbolIdentity(symbol.SymbolId)));
                            if (!string.Equals(symbol.ProjectPath, finding.ProjectPath, StringComparison.Ordinal))
                            {
                                builder.Append(" (").Append(GetOccurrenceRole(reviewFinding, symbol)).Append(')');
                            }
                            builder.Append('\n');
                        }
                    }
                    else
                    {
                        builder.Append("  - Signal: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding,
                            allMissingTestFindingsAreUncertain))).Append('\n');
                    }

                    AppendUnrepresentedEvidence(builder, reviewFinding, group.SourcePath);

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

    private static void AppendUnrepresentedEvidence(StringBuilder builder, ReviewFinding finding, string representativePath)
    {
        var evidenceIsRendered = finding.AnalysisId is "indirection-drift-candidates"
            or "structural-duplication-candidates"
            or "missing-test-evidence-candidates"
            or "type-dependency-cycle-candidates"
            or "type-dependency-hub-candidates"
            || finding.AnalysisId == "duplicate-code-candidates" && finding.Finding.RelatedSymbols.Count > 1;
        if (!evidenceIsRendered && finding.Finding.Evidence.Count > 0)
        {
            var evidenceItems = finding.Finding.Evidence.Where(evidence => !IsRedundantDeclarationEvidence(finding, evidence)).ToArray();
            if (evidenceItems.Length > 0)
            {
                builder.Append("  - Evidence:\n");
                foreach (var evidence in evidenceItems)
                {
                    builder.Append("    - ").Append(FormatFindingLocation(representativePath, evidence.SourcePath, evidence.Line)).Append(' ')
                        .Append(FormatCodeSpan(FormatSymbolIdentity(evidence.Label)));
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

    private static bool IsRedundantDeclarationEvidence(ReviewFinding finding, FindingEvidence evidence)
    {
        if (!string.Equals(evidence.SourcePath, finding.Finding.SourcePath, StringComparison.Ordinal)
            || evidence.Line != finding.Finding.StartLine)
        {
            return false;
        }

        return finding.AnalysisId switch
        {
            "code-size-candidates" => (evidence.Label == "Executable member declaration"
                    && evidence.Detail == "This loaded source line identifies the measured member declaration.")
                || (evidence.Label == "Class declaration part"
                    && evidence.Detail == "This loaded source line identifies one part contributing to the measured type."),
            "dead-code-candidates" => evidence.Label is "Type declaration" or "Method declaration"
                && evidence.Detail == "Candidate declaration selected from production C# source.",
            _ => false,
        };
    }

    private static string GetDuplicateEvidenceIdentity(string detail)
    {
        const string projectMarker = " in project '";
        var markerIndex = detail.IndexOf(projectMarker, StringComparison.Ordinal);
        return markerIndex >= 0 ? detail[..markerIndex] : detail;
    }

    private static string FormatDuplicateEvidenceDetail(string detail)
    {
        const string tokenSuffix = " body tokens).";
        if (!detail.EndsWith(tokenSuffix, StringComparison.Ordinal))
        {
            return detail;
        }

        var countStart = detail.LastIndexOf(" (", StringComparison.Ordinal);
        return countStart >= 0
            ? detail[(countStart + 2)..^2]
            : detail;
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

    private static string FormatSymbolIdentity(string symbolId)
    {
        var separator = symbolId.IndexOf(':');
        if (separator != 1 || symbolId.Length < 3)
        {
            return symbolId;
        }

        var kind = symbolId[0];
        if (kind is not ('M' or 'T' or 'P' or 'F' or 'E' or '!'))
        {
            return symbolId;
        }

        var identity = symbolId[(separator + 1)..];
        var returnTypeStart = identity.IndexOf('~');
        if (returnTypeStart >= 0)
        {
            identity = identity[..returnTypeStart];
        }

        var signature = identity.IndexOf('(');
        if (signature >= 0)
        {
            identity = identity[..signature];
        }

        identity = identity.Replace('+', '.');
        var parts = identity.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => symbolId,
            1 => parts[0],
            _ => parts[^2] + "." + parts[^1],
        };
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

            var targetArea = GetFindingArea(target);
            var targetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, "..", targetArea, EncodePathSegment(reference.AnalysisId) + ".md"));
            var relativePath = Path.GetRelativePath(Path.GetDirectoryName(reportPath)!, targetPath).Replace('\\', '/');
            return (Path: relativePath, Id: GetFindingId(target));
        }).Distinct().ToArray();
        var groupedItems = relatedItems.Where(static item => item.Path.Length > 0)
            .GroupBy(static item => item.Path, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => FormatCodeSpan(group.Key) + ": " + string.Join(", ", group.Select(static item => item.Id).Order(StringComparer.Ordinal)))
            .Concat(relatedItems.Where(static item => item.Path.Length == 0).Select(static item => item.Id).Order(StringComparer.Ordinal));
        return string.Join("; ", groupedItems);
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

    internal static string FormatSignal(string analysisId, FindingDraft finding, bool suppressAttributionUncertainty = false)
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

        if (analysisId == "type-dependency-cycle-candidates")
        {
            return FormatNumber(Metric(finding, "typeCount")) + " production types in "
                + FormatNumber(Metric(finding, "declarationFileCount")) + " declaration files across "
                + FormatNumber(Metric(finding, "projectCount")) + " projects; "
                + FormatNumber(Metric(finding, "internalEdgeCount")) + " directed dependencies";
        }

        if (analysisId == "type-dependency-hub-candidates")
        {
            return $"{FormatNumber(Metric(finding, "fanIn"))} production consumer types (minimum {FormatNumber(Metric(finding, "minFanIn"))}; {FormatNumber(Metric(finding, "fanInNeighborFileCount"))} files / {FormatNumber(Metric(finding, "fanInNeighborProjectCount"))} projects) and {FormatNumber(Metric(finding, "fanOut"))} production dependency types (minimum {FormatNumber(Metric(finding, "minFanOut"))}; {FormatNumber(Metric(finding, "fanOutNeighborFileCount"))} files / {FormatNumber(Metric(finding, "fanOutNeighborProjectCount"))} projects); {FormatNumber(Metric(finding, "testConsumerCount"))} direct test consumer types separately";
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
            if (Metric(finding, "attributionUncertain") > 0 && !suppressAttributionUncertainty)
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
