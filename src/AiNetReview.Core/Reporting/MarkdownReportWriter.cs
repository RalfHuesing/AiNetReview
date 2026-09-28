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
        var started = DateTimeOffset.UtcNow;
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
                    var rulePath = Path.Combine(temporaryPath, "rules", configuredRule.RuleId + ".md");
                    Directory.CreateDirectory(Path.GetDirectoryName(rulePath)!);
                    await WriteUtf8Async(rulePath, FormatRuleReport(config, configuredRule, ruleResult, Path.GetDirectoryName(rulePath)!), cancellationToken)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var ended = DateTimeOffset.UtcNow;
                var indexPath = Path.Combine(temporaryPath, "index.md");
                await WriteUtf8Async(indexPath, FormatIndex(runId, started, ended, config, rules, resultById), cancellationToken)
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
        DateTimeOffset started,
        DateTimeOffset ended,
        ReviewConfig config,
        ConfiguredRule[] rules,
        System.Collections.Generic.IReadOnlyDictionary<string, RuleRunResult> results)
    {
        var builder = new StringBuilder();
        builder.Append("# AiNetReview – ").Append(runId).Append("\n\n")
            .Append("| Metadata | Value |\n| --- | --- |\n")
            .Append("| Started (UTC) | ").Append(EscapeTable(FormatTimestamp(started))).Append(" |\n")
            .Append("| Ended (UTC) | ").Append(EscapeTable(FormatTimestamp(ended))).Append(" |\n")
            .Append("| Solution | ").Append(EscapeTable(config.SolutionPath)).Append(" |\n")
            .Append("| Detected | ").Append(results.Values.Sum(static result => result.DetectedCount).ToString(CultureInfo.InvariantCulture)).Append(" |\n\n")
            .Append("| Rule | Title | Version | Options | Detected | Report |\n| --- | --- | ---: | --- | ---: | --- |\n");

        foreach (var rule in rules)
        {
            var id = rule.RuleId;
            var descriptor = rule.Rule.Descriptor;
            builder.Append("| ").Append(EscapeTable(id)).Append(" | ").Append(EscapeTable(descriptor.Title)).Append(" | ")
                .Append(descriptor.BehaviorVersion.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(EscapeTable(FormatOptions(rule.EffectiveOptions))).Append(" | ")
                .Append(results[id].DetectedCount.ToString(CultureInfo.InvariantCulture)).Append(" | [Markdown report](rules/")
                .Append(EncodePathSegment(id)).Append(".md) |\n");
        }

        builder.Append("\nFindings are review prompts, not errors that should be fixed automatically.\n");
        return builder.ToString();
    }

    private static string FormatRuleReport(ReviewConfig config, ConfiguredRule configuredRule, RuleRunResult result, string reportDirectory)
    {
        var descriptor = configuredRule.Rule.Descriptor;
        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeInline(descriptor.RuleId)).Append(" – ").Append(EscapeInline(descriptor.Title)).Append("\n\n")
            .Append("| Property | Value |\n| --- | --- |\n")
            .Append("| Behavior version | ").Append(descriptor.BehaviorVersion.ToString(CultureInfo.InvariantCulture)).Append(" |\n")
            .Append("| Purpose | ").Append(EscapeTable(descriptor.Purpose)).Append(" |\n")
            .Append("| Measurement | ").Append(EscapeTable(descriptor.Measurement)).Append(" |\n")
            .Append("| Effective options | ").Append(EscapeTable(FormatOptions(configuredRule.EffectiveOptions))).Append(" |\n")
            .Append("| Detected | ").Append(result.DetectedCount.ToString(CultureInfo.InvariantCulture)).Append(" |\n\n")
            .Append("## Review questions\n\n");
        foreach (var question in descriptor.ReviewQuestions)
        {
            builder.Append("- ").Append(EscapeInline(question)).Append("\n");
        }

        foreach (var finding in result.Result.Findings.OrderBy(static item => item.ProjectPath, StringComparer.Ordinal)
                     .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.StartLine)
                     .ThenBy(static item => item.SubjectId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Discriminator, StringComparer.Ordinal))
        {
            builder.Append("\n## ").Append(EscapeInline(finding.SubjectId)).Append(" — ").Append(EscapeInline(finding.Discriminator)).Append("\n\n")
                .Append("| Property | Value |\n| --- | --- |\n")
                .Append("| Project | ").Append(EscapeTable(finding.ProjectPath)).Append(" |\n")
                .Append("| Source | [").Append(EscapeLinkText(finding.SourcePath)).Append(':')
                .Append(finding.StartLine.ToString(CultureInfo.InvariantCulture)).Append("](")
                .Append(SourceLink(config.ProjectRoot, finding.SourcePath, finding.StartLine, reportDirectory)).Append(") |\n")
                .Append("| Rationale | ").Append(EscapeTable(finding.Rationale)).Append(" |\n")
                .Append("| Metrics | ").Append(EscapeTable(FormatMetrics(finding))).Append(" |\n\n")
                .Append("### Evidence\n\n");
            foreach (var evidence in finding.Evidence.OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
                         .ThenBy(static item => item.Line)
                         .ThenBy(static item => item.Label, StringComparer.Ordinal)
                         .ThenBy(static item => item.Detail, StringComparer.Ordinal)
                         .ThenBy(static item => item.Snippet, StringComparer.Ordinal))
            {
                builder.Append("- [").Append(EscapeLinkText(evidence.SourcePath)).Append(':')
                    .Append(evidence.Line.ToString(CultureInfo.InvariantCulture)).Append("](")
                    .Append(SourceLink(config.ProjectRoot, evidence.SourcePath, evidence.Line, reportDirectory)).Append(") — **")
                    .Append(EscapeInline(evidence.Label)).Append("**: ").Append(EscapeInline(evidence.Detail))
                    .Append("; code: ").Append(FormatCodeSpan(evidence.Snippet)).Append('\n');
            }
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

    private static string FormatMetrics(FindingDraft finding) => string.Join(
        ", ", finding.Metrics.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value.ToString("R", CultureInfo.InvariantCulture)}"));

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

    private static string EscapeLinkText(string value) => EscapeInline(value);

    private static string EncodePathSegment(string segment) => Uri.EscapeDataString(segment);

    private static string FormatTimestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

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
