namespace AiNetReview.Core.Reporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;

/// <summary>Writes a compact, directly navigable Markdown index for one selected finding view.</summary>
internal static class AuditMapReportWriter
{
    internal static async Task WriteAuditMapAsync(
        string runDirectory,
        string runId,
        string projectRoot,
        IReadOnlyList<ReviewFinding> findings,
        string viewName,
        CancellationToken cancellationToken,
        bool hasBaseline)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (viewName is not ("changed-files" or "all-findings"))
        {
            throw new ArgumentOutOfRangeException(nameof(viewName), viewName, "Audit map view must be changed-files or all-findings.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(runDirectory, "audit-map", viewName);
        Directory.CreateDirectory(directory);
        var indexPath = Path.Combine(directory, "index.md");
        var builder = new StringBuilder()
            .Append("# Audit map — ").Append(viewName).Append("\n\n")
            .Append("Run: ").Append(MarkdownReportWriter.FormatCodeSpan(runId)).Append("; view: ")
            .Append(MarkdownReportWriter.FormatCodeSpan(viewName)).Append(". Findings are grouped by their representative project and source file. A shared group is a navigation aid and does not claim a common cause or responsibility.\n\n")
            .Append(viewName == "all-findings" && hasBaseline
                ? "> **Full-audit scope:** This view contains every current finding. Inspect it only when the user explicitly requests a full-repository audit.\n\n"
                : viewName == "changed-files"
                    ? "This view contains the selected changed-file findings. Missing-test-evidence findings follow the snapshot-wide selection rule.\n\n"
                    : "This view contains every current finding.\n\n")
            .Append("Unique findings: **").Append(findings.Count.ToString(CultureInfo.InvariantCulture)).Append("**. Each finding appears once and links to its original analysis report and source locations.\n\n");

        if (findings.Count == 0)
        {
            builder.Append("No findings are assigned in this view.\n");
        }
        else
        {
            var reportGroups = findings
                .GroupBy(static finding => finding.Finding.ProjectPath + "\0" + finding.Finding.SourcePath, StringComparer.Ordinal)
                .OrderBy(static group => group.First().Finding.ProjectPath, StringComparer.Ordinal)
                .ThenBy(static group => group.First().Finding.SourcePath, StringComparer.Ordinal);
            string? previousProject = null;
            foreach (var group in reportGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = group.First();
                var projectPath = first.Finding.ProjectPath;
                var sourcePath = first.Finding.SourcePath;
                if (!string.Equals(previousProject, projectPath, StringComparison.Ordinal))
                {
                    builder.Append("## Project ").Append(MarkdownReportWriter.FormatCodeSpan(projectPath)).Append("\n\n");
                    previousProject = projectPath;
                }

                builder.Append("### [")
                    .Append(MarkdownReportWriter.EscapeLinkText(sourcePath)).Append("](")
                    .Append(MarkdownReportWriter.FormatSourceLink(indexPath, projectRoot, sourcePath)).Append(")\n\n");

                foreach (var finding in group.OrderBy(static item => item.AnalysisId, StringComparer.Ordinal)
                             .ThenBy(MarkdownReportWriter.GetFindingId, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var findingId = MarkdownReportWriter.GetFindingId(finding);
                    var area = MarkdownReportWriter.GetFindingArea(finding);
                    var detailPath = "../../" + area + "/" + viewName + "/"
                        + MarkdownReportWriter.EncodePathSegment(finding.AnalysisId) + ".md#finding-" + findingId;
                    var signal = MarkdownReportWriter.FormatSignal(finding.AnalysisId, finding.Finding);
                    builder.Append("- [")
                        .Append(MarkdownReportWriter.EscapeLinkText(findingId)).Append("](")
                        .Append(detailPath).Append(") — ")
                        .Append(MarkdownReportWriter.FormatCodeSpan(finding.AnalysisId)).Append(": ")
                        .Append(MarkdownReportWriter.EscapeInline(signal)).Append('\n');

                    AppendSubjectSources(builder, indexPath, projectRoot, finding);
                    AppendAdditionalSources(builder, indexPath, projectRoot, finding);
                }

                builder.Append('\n');
            }
        }

        await MarkdownReportWriter.WriteUtf8Async(indexPath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static void AppendSubjectSources(StringBuilder builder, string reportPath, string projectRoot, ReviewFinding finding)
    {
        if (finding.SubjectOccurrences.Count == 0)
        {
            builder.Append("  - Representative source: ");
            AppendSourceLocation(builder, reportPath, projectRoot, finding.Finding.SourcePath, finding.Finding.StartLine);
            builder.Append('\n');
            return;
        }

        builder.Append("  - Subject occurrences: ");
        var ordered = finding.SubjectOccurrences
            .OrderBy(static occurrence => occurrence.Symbol.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Symbol.SourcePath, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Symbol.Line)
            .ThenBy(static occurrence => occurrence.Symbol.SymbolId, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            var occurrence = ordered[index];
            AppendSourceLocation(builder, reportPath, projectRoot, occurrence.Symbol.SourcePath, occurrence.Symbol.Line);
            builder.Append(" (").Append(occurrence.Role).Append("; project ")
                .Append(MarkdownReportWriter.FormatCodeSpan(occurrence.Symbol.ProjectPath)).Append(')');
        }

        builder.Append('\n');
    }

    private static void AppendAdditionalSources(StringBuilder builder, string reportPath, string projectRoot, ReviewFinding finding)
    {
        var subjectPaths = finding.SubjectOccurrences.Select(static occurrence => occurrence.Symbol.SourcePath)
            .Append(finding.Finding.SourcePath)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var additionalPaths = finding.SourcePaths.Where(path => !subjectPaths.Contains(path))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (additionalPaths.Length == 0)
        {
            return;
        }

        builder.Append("  - Other finding sources: ");
        for (var index = 0; index < additionalPaths.Length; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            AppendSourceLocation(builder, reportPath, projectRoot, additionalPaths[index], 0);
        }

        builder.Append('\n');
    }

    private static void AppendSourceLocation(StringBuilder builder, string reportPath, string projectRoot, string sourcePath, int line)
    {
        var label = line > 0 ? sourcePath + ":" + line.ToString(CultureInfo.InvariantCulture) : sourcePath;
        builder.Append('[').Append(MarkdownReportWriter.EscapeLinkText(label)).Append("](")
            .Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, sourcePath));
        if (line > 0)
        {
            builder.Append("#L").Append(line.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(')');
    }
}
