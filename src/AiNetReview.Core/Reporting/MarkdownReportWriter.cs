namespace AiNetReview.Core.Reporting;

using System;
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
using AiNetReview.Core.Rules;

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(result);
        var rules = config.Rules.OrderBy(static rule => rule.RuleId, StringComparer.Ordinal).ToArray();
        if (result.Rules.Count != rules.Length || rules.Any(rule => result.Rules.Count(run => run.RuleId == rule.RuleId) != 1))
        {
            throw new ArgumentException("Run results must contain exactly one result for every configured rule.", nameof(result));
        }

        var resultById = result.Rules.ToDictionary(static run => run.RuleId, StringComparer.Ordinal);
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
                foreach (var configuredRule in rules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ruleResult = resultById[configuredRule.RuleId];
                    if (ruleResult.DetectedCount == 0)
                    {
                        continue;
                    }

                    var rulePath = Path.Combine(temporaryPath, "rules", configuredRule.RuleId + ".md");
                    Directory.CreateDirectory(Path.GetDirectoryName(rulePath)!);
                    await WriteUtf8Async(rulePath, FormatRuleReport(config, configuredRule, ruleResult, Path.GetDirectoryName(rulePath)!), cancellationToken)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await WriteUtf8Async(indexPath, FormatIndex(runId, config, rules, resultById), cancellationToken)
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
        ConfiguredRule[] rules,
        System.Collections.Generic.IReadOnlyDictionary<string, RuleRunResult> results)
    {
        var builder = new StringBuilder();
        builder.Append("# AiNetReview – ").Append(runId).Append("\n\n")
            .Append("- Run ID: `").Append(runId).Append("`\n")
            .Append("- Repository: `").Append(EscapeInline(Path.GetFullPath(config.ProjectRoot))).Append("`\n")
            .Append("- Solution: `").Append(EscapeInline(config.SolutionPath)).Append("`\n\n");

        var rulesWithFindings = rules.Where(rule => results[rule.RuleId].DetectedCount > 0).ToArray();
        if (rulesWithFindings.Length == 0)
        {
            builder.Append(rules.Length == 0
                ? "Keine Prüfung fand statt, da alle Regeln deaktiviert sind.\n"
                : "Keine Befunde gefunden.\n");
            return builder.ToString();
        }

        builder.Append("## Rules with open findings\n\n");
        foreach (var rule in rulesWithFindings)
        {
            builder.Append("- [").Append(EscapeLinkText(rule.Rule.Descriptor.Title)).Append("](rules/")
                .Append(EncodePathSegment(rule.RuleId)).Append(".md)\n");
        }

        builder.Append("\n## Working through findings\n\n")
            .Append("Review each finding against the source code. After fixing it or deciding to ignore it, explain the decision to the user and delete the finding's table row. When a rule has no rows left, delete its report and remove its link here. When all findings are handled, replace these instructions with **All findings addressed**.\n");
        return builder.ToString();
    }

    private static string FormatRuleReport(ReviewConfig config, ConfiguredRule configuredRule, RuleRunResult result, string reportDirectory)
    {
        var descriptor = configuredRule.Rule.Descriptor;
        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeLinkText(descriptor.Title)).Append("\n\n")
            .Append(EscapeInline(descriptor.Purpose)).Append("\n\n")
            .Append("Effective options: ").Append(FormatCodeSpan(FormatOptions(configuredRule.EffectiveOptions))).Append("\n\n")
            .Append("Review questions:\n\n");
        foreach (var question in descriptor.ReviewQuestions)
        {
            builder.Append("- ").Append(EscapeInline(question)).Append("\n");
        }

        var projectCountsBySource = result.Result.Findings
            .GroupBy(static finding => finding.SourcePath, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static finding => finding.ProjectPath).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);

        builder.Append("\n| Source | Signal | Other Locations |\n| --- | --- | --- |\n");
        foreach (var finding in result.Result.Findings.OrderBy(static item => item.ProjectPath, StringComparer.Ordinal)
                     .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.StartLine)
                     .ThenBy(static item => item.SubjectId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Discriminator, StringComparer.Ordinal))
        {
            builder.Append("| [").Append(EscapeLinkText(finding.SourcePath)).Append(':')
                .Append(finding.StartLine.ToString(CultureInfo.InvariantCulture)).Append("](")
                .Append(SourceLink(config.ProjectRoot, finding.SourcePath, finding.StartLine, reportDirectory)).Append(')');
            if (projectCountsBySource[finding.SourcePath] > 1)
            {
                builder.Append(" (").Append(EscapeTable(finding.ProjectPath)).Append(')');
            }

            builder.Append(" | ").Append(EscapeTable(FormatSignal(configuredRule.RuleId, finding))).Append(" | ")
                .Append(FormatAdditionalLocations(config.ProjectRoot, finding, reportDirectory)).Append(" |\n");
        }

        return builder.ToString();
    }

    private static string SourceLink(string projectRoot, string sourcePath, int line, string reportDirectory)
    {
        var absoluteSourcePath = Path.GetFullPath(Path.Combine(projectRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar)));
        var relativePath = Path.GetRelativePath(reportDirectory, absoluteSourcePath);
        var linkPath = Path.IsPathFullyQualified(relativePath)
            ? new Uri(absoluteSourcePath).AbsoluteUri
            : string.Join('/', relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return linkPath + "#L" + line.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSignal(string ruleId, FindingDraft finding)
    {
        if (ruleId == "dead-code-candidates")
        {
            return finding.Discriminator == "type-candidate"
                ? "Type without known use"
                : "Method without known use";
        }

        if (ruleId == "method-control-flow-outliers")
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

        if (ruleId == "duplicate-code-candidates")
        {
            var similarity = Metric(finding, "similarityScore");
            var minimumSimilarity = Metric(finding, "minimumSimilarityThreshold");
            return $"{FormatNumber(Metric(finding, "memberCount"))} methods; {FormatPercent(similarity)} similarity (minimum {FormatPercent(minimumSimilarity)})";
        }

        return finding.Rationale;
    }

    private static double Metric(FindingDraft finding, string name) =>
        finding.Metrics.TryGetValue(name, out var value) ? value : 0;

    private static string FormatNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(double value) => (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string FormatAdditionalLocations(string projectRoot, FindingDraft finding, string reportDirectory)
    {
        var locations = finding.Evidence
            .Where(evidence => evidence.SourcePath != finding.SourcePath || evidence.Line != finding.StartLine)
            .Select(static evidence => (evidence.SourcePath, evidence.Line))
            .Distinct()
            .OrderBy(static location => location.SourcePath, StringComparer.Ordinal)
            .ThenBy(static location => location.Line)
            .ToArray();
        if (locations.Length == 0)
        {
            return "—";
        }

        return string.Join(", ", locations.Select(location =>
            $"[{EscapeLinkText(location.SourcePath)}:{location.Line.ToString(CultureInfo.InvariantCulture)}]({SourceLink(projectRoot, location.SourcePath, location.Line, reportDirectory)})"));
    }

    private static string FormatOptions(RuleOptions options) => "{" + string.Join(
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

    private static string EscapeTable(string value) => EscapeInline(value);

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
