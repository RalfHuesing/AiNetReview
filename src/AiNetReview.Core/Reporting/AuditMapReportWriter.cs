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

/// <summary>Writes a compact routing index for all findings in one complete audit.</summary>
internal static class AuditMapReportWriter
{
    internal static async Task WriteAuditMapAsync(
        string runDirectory,
        string runId,
        IReadOnlyList<ReviewFinding> findings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(findings);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(runDirectory, "maps", "audit");
        Directory.CreateDirectory(directory);
        var indexPath = Path.Combine(directory, "index.md");
        var builder = new StringBuilder()
            .Append("# Audit map\n\n")
            .Append("Run: ").Append(MarkdownFormatUtils.FormatCodeSpan(runId)).Append(". Maps home: `../index.md`.\n\n")
            .Append("All current findings from the configured analyses.\n\n")
            .Append("A finding is listed once, under its representative project and source file. To find every finding involving a file, search the full repository-relative path in canonical analysis details across `../../production/`, `../../tests/`, and `../../mixed/`, including secondary occurrences, witnesses, and partial declarations.\n\n")
            .Append("Findings: **").Append(findings.Count.ToString(CultureInfo.InvariantCulture)).Append("**. IDs route to canonical analysis details.\n\n");

        if (findings.Count == 0)
        {
            builder.Append("No findings in this audit.\n");
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
                    builder.Append("## Project: ").Append(MarkdownFormatUtils.FormatCodeSpan(projectPath)).Append("\n\n");
                    previousProject = projectPath;
                }

                builder.Append("### File: ").Append(MarkdownFormatUtils.FormatCodeSpan(sourcePath)).Append("\n\n");
                var routes = group.Select(finding =>
                {
                    var area = FindingReportUtilities.GetFindingArea(finding);
                    return new
                    {
                        Path = "../../" + area + "/"
                            + FindingReportUtilities.EncodePathSegment(finding.AnalysisId) + ".md",
                        Id = FindingReportUtilities.GetFindingId(finding),
                    };
                });
                foreach (var routeGroup in routes.GroupBy(static route => route.Path, StringComparer.Ordinal)
                             .OrderBy(static routeGroup => routeGroup.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    builder.Append("- ").Append(MarkdownFormatUtils.FormatCodeSpan(routeGroup.Key)).Append(": ")
                        .Append(string.Join(", ", routeGroup.Select(static route => route.Id).Order(StringComparer.Ordinal))).Append('\n');
                }

                builder.Append('\n');
            }
        }

        await ReportIoUtils.WriteUtf8Async(indexPath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }
}
