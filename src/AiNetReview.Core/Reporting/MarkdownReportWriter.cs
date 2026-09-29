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
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Func<CancellationToken, ValueTask>? beforePublication;

    public MarkdownReportWriter()
    {
    }

    internal MarkdownReportWriter(Func<CancellationToken, ValueTask> beforePublication)
    {
        this.beforePublication = beforePublication ?? throw new ArgumentNullException(nameof(beforePublication));
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
                    var changedFindings = allFindings.Where(static finding => finding.IsChanged).ToArray();
                    await WriteViewAnalysisAsync(temporaryPath, "changed-files", configuredAnalysis, changedFindings, findings, cancellationToken)
                        .ConfigureAwait(false);
                    await WriteViewAnalysisAsync(temporaryPath, "all-findings", configuredAnalysis, allFindings, findings, cancellationToken)
                        .ConfigureAwait(false);
                }

                await WriteViewIndexAsync(temporaryPath, "changed-files", "Changed files", analyses, findings, changedOnly: true, cancellationToken)
                    .ConfigureAwait(false);
                await WriteViewIndexAsync(temporaryPath, "all-findings", "All findings", analyses, findings, changedOnly: false, cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await WriteUtf8Async(indexPath, FormatIndex(runId, config, analyses, findings, configurationPath, baselineCommandContext), cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (beforePublication is not null)
                {
                    await beforePublication(cancellationToken).ConfigureAwait(false);
                }

                File.Delete(Path.Combine(temporaryPath, ".owner"));

                try
                {
                    Directory.Move(temporaryPath, finalPath);
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
                    TryDeleteTemporaryDirectory(temporaryPath);
                }
            }
        }
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
        string? configurationPath,
        BaselineCommandContext? baselineCommandContext)
    {
        var builder = new StringBuilder();
        builder.Append("# AiNetReview – ").Append(runId).Append("\n\n")
            .Append("- Run ID: `").Append(runId).Append("`\n")
            .Append("- Repository: `").Append(EscapeInline(Path.GetFullPath(config.ProjectRoot))).Append("`\n")
            .Append("- Solution: `").Append(EscapeInline(config.SolutionPath)).Append("`\n\n");

        var changedCount = findings.Count(static finding => finding.IsChanged);
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

        var reportLinks = new List<string>();
        foreach (var analysis in analyses)
        {
            var count = findings.Count(finding => finding.AnalysisId == analysis.AnalysisId && finding.IsChanged);
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
            .Append(allCount.ToString(CultureInfo.InvariantCulture)).Append(" findings). Keep this complete view intact while editing the working view.\n\n")
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

        builder.Append("The command replaces the baseline for all source files and does not require a report.\n\n")
            .Append("## Review guidance\n\n")
            .Append("These findings are review signals, not proven defects or automatic change requests. Read the target repository's applicable instructions and relevant design documents. Consider the behavior of the application as a whole, including contracts, callers, tests, and related findings across analyses. ")
            .Append("AI agents and reviewers must treat `changed-files/` as the primary working set and must not inspect or report findings from `all-findings/` unless the user explicitly requests a full repository audit. ")
            .Append("First remove only clear false positives from `changed-files/` and leave uncertain cases for review. Then work through the remaining findings one decision at a time while keeping the wider context in view. Avoid local workarounds and refactoring driven only by a metric. Explain consequential changes and tradeoffs to the user. Keep report files and links consistent when editing them.\n");
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

        var visible = analyses.Where(analysis => findings.Any(finding => finding.AnalysisId == analysis.AnalysisId
                && (!changedOnly || finding.IsChanged)))
            .ToArray();
        if (visible.Length == 0)
        {
            builder.Append("No findings in this view.\n");
        }
        else
        {
            foreach (var analysis in visible)
            {
                var count = findings.Count(finding => finding.AnalysisId == analysis.AnalysisId && (!changedOnly || finding.IsChanged));
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

    private static async Task WriteViewAnalysisAsync(
        string runDirectory,
        string viewDirectory,
        ConfiguredReviewAnalysis configuredAnalysis,
        IReadOnlyList<ReviewFinding> findings,
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
        await WriteUtf8Async(analysisPath, FormatAnalysisReport(configuredAnalysis, findings, allFindings), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatAnalysisReport(
        ConfiguredReviewAnalysis configuredAnalysis,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<ReviewFinding> allFindings)
    {
        var descriptor = configuredAnalysis.Analysis.Descriptor;
        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeLinkText(descriptor.Title)).Append("\n\n")
            .Append(EscapeInline(descriptor.Purpose)).Append("\n\n")
            .Append("Effective options: ").Append(FormatCodeSpan(FormatOptions(configuredAnalysis.EffectiveOptions))).Append("\n\n")
            .Append("Review questions:\n\n");
        foreach (var question in descriptor.ReviewQuestions)
        {
            builder.Append("- ").Append(EscapeInline(question)).Append('\n');
        }

        builder.Append("\n## Findings\n\n");

        foreach (var reviewFinding in findings.OrderBy(static item => item.Finding.ProjectPath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Finding.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Finding.StartLine)
                     .ThenBy(static item => item.Finding.SubjectId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Finding.Discriminator, StringComparer.Ordinal))
        {
            var finding = reviewFinding.Finding;
            var isCluster = finding.RelatedSymbols.Count > 1;

            if (reviewFinding.AnalysisId == "indirection-drift-candidates")
            {
                builder.Append("- Forwarding path: ").Append(EscapeInline(FormatSignal(reviewFinding.AnalysisId, finding))).Append('\n');
                foreach (var member in finding.Evidence)
                {
                    builder.Append("  - `").Append(member.SourcePath).Append("`: `")
                        .Append(member.Label).Append("`\n");
                }
            }
            else if (isCluster)
            {
                builder.Append("- Cluster: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
                foreach (var symbol in finding.RelatedSymbols)
                {
                    builder.Append("  - `").Append(symbol.SourcePath).Append("`: `")
                        .Append(symbol.SymbolId).Append("`\n");
                }
            }
            else
            {
                builder.Append("- `").Append(finding.SourcePath).Append("`: `")
                    .Append(finding.SubjectId).Append("`\n")
                    .Append("  - Signal: ").Append(EscapeInline(FormatSignal(configuredAnalysis.AnalysisId, finding))).Append('\n');
            }

            var related = FormatRelated(reviewFinding, findings, allFindings);
            if (!string.IsNullOrEmpty(related))
            {
                builder.Append("  - Related: ").Append(related).Append('\n');
            }
        }

        return builder.ToString();
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

        return finding.Rationale;
    }

    private static double Metric(FindingDraft finding, string name) =>
        finding.Metrics.TryGetValue(name, out var value) ? value : 0;

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

    private static void TryDeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a failed report is never published, and any remaining directory is clearly temporary.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: a failed report is never published, and any remaining directory is clearly temporary.
        }
    }
}

public sealed record PublishedReport(string RunId, string IndexPath);
