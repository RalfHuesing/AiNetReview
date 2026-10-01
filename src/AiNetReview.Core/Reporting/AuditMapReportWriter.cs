namespace AiNetReview.Core.Reporting;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;

internal static class AuditMapReportWriter
{
    internal const int ReferencePageByteLimit = 16 * 1024;
    internal const int ReferencePageEntryLimit = 128;
    internal static async Task WriteAuditMapAsync(
        string runDirectory,
        string runId,
        string projectRoot,
        AuditFindingPackageViews? views,
        string viewName,
        CancellationToken cancellationToken)
    {
        var view = viewName == "changed-files" ? views?.ChangedFiles : views?.AllFindings;
        var directory = Path.Combine(runDirectory, "audit-map", viewName);
        Directory.CreateDirectory(directory);
        var builder = new StringBuilder()
            .Append("# Audit map — ").Append(viewName).Append("\n\n")
            .Append("Run: ").Append(MarkdownReportWriter.FormatCodeSpan(runId)).Append("; view: ")
            .Append(MarkdownReportWriter.FormatCodeSpan(viewName)).Append(". This is a deterministic technical grouping of source signals, not a claim of shared responsibility, defect cause, or independent changeability. Statically unobserved relationships may be absent.\n\n")
            .Append(viewName == "all-findings"
                ? "> **Full-audit scope:** This reference view contains every current finding. Inspect it only when the user explicitly requests a full-repository audit.\n\n"
                : "Use this selected view as the active assignment. Excluded findings and their details are not copied here.\n\n")
            .Append("Unique findings: **").Append((view?.FindingCount ?? 0).ToString(CultureInfo.InvariantCulture)).Append("**. Primary package assignments: **")
            .Append((view?.Packages.Sum(static package => package.Findings.Count) ?? 0).ToString(CultureInfo.InvariantCulture)).Append("**. Context is not counted as another finding.\n\n");

        if (view is null || view.Packages.Count == 0)
        {
            builder.Append("No findings are assigned in this view.\n");
        }
        else
        {
            builder.Append("## Packages\n\n| Package | Technical area | Primary findings | Analyses |\n| --- | --- | ---: | --- |\n");
            foreach (var package in view.Packages.OrderBy(static item => item.Id, StringComparer.Ordinal))
            {
                var packageFile = MarkdownReportWriter.EncodePathSegment(package.Id) + ".md";
                var areas = string.Join("; ", package.Areas.Select(area => MarkdownReportWriter.EscapeInline(area.Name + " — " + area.ProjectPath)));
                var analyses = string.Join(", ", package.Findings.Select(static finding => finding.Finding.AnalysisId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                builder.Append("| [").Append(package.Id).Append("](").Append(packageFile).Append(") | ")
                    .Append(areas).Append(" | ").Append(package.Findings.Count.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(MarkdownReportWriter.FormatCodeSpan(analyses)).Append(" |\n");
            }

            var mapRows = view.ContextAreas.Select(static row => (row.Area, row.IsContextOnly, row.PackageId))
                .Concat(view.Packages.SelectMany(package => package.Areas.Select(area => (Area: area, IsContextOnly: false, PackageId: package.Id))))
                .GroupBy(static row => row.Area.Id, StringComparer.Ordinal)
                .Select(group => (Area: group.First().Area, IsContextOnly: group.All(static row => row.IsContextOnly),
                    IsShared: view.ContextAreas.Any(context => context.Area.Id == group.Key && !context.IsContextOnly),
                    PackageIds: group.Select(static row => row.PackageId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
                .OrderBy(static row => row.Area.ProjectPath, StringComparer.Ordinal).ThenBy(static row => row.Area.Name, StringComparer.Ordinal).ToArray();
            if (mapRows.Length > 0)
            {
                builder.Append("\n## Areas and context navigation\n\n")
                    .Append("These entries provide navigation only. Context-only areas have no primary finding in this view; shared areas participate in a package that owns the listed findings.\n\n");
                foreach (var row in mapRows)
                {
                    builder.Append("<a id=\"area-").Append(row.Area.Id).Append("\"></a>\n")
                        .Append("- **").Append(row.IsContextOnly ? "Context only" : row.IsShared ? "Shared area" : "Primary area").Append(": ")
                        .Append(MarkdownReportWriter.EscapeInline(row.Area.Name)).Append("** (").Append(MarkdownReportWriter.FormatCodeSpan(row.Area.ProjectPath)).Append(')');
                    if (row.Area.FilePath is not null)
                    {
                        builder.Append(" — [source](").Append(MarkdownReportWriter.FormatSourceLink(Path.Combine(directory, "index.md"), projectRoot, row.Area.FilePath)).Append(')');
                    }
                    else
                    {
                        foreach (var location in row.Area.Declarations)
                        {
                            builder.Append(" — [").Append(MarkdownReportWriter.EscapeLinkText(location.Path)).Append(':').Append(location.StartLine.ToString(CultureInfo.InvariantCulture))
                                .Append("](").Append(MarkdownReportWriter.FormatSourceLink(Path.Combine(directory, "index.md"), projectRoot, location.Path)).Append('#')
                                .Append("L").Append(location.StartLine.ToString(CultureInfo.InvariantCulture)).Append(')');
                        }
                    }

                    builder.Append("; package ").Append(string.Join(", ", row.PackageIds.Select(packageId => "[" + packageId + "](" + MarkdownReportWriter.EncodePathSegment(packageId) + ".md)")));
                    if (row.Area.ContextTypeAreaIds.Count > 0)
                    {
                        builder.Append("; type/file fallback: ").Append(MarkdownReportWriter.EscapeInline(row.Area.IdentityReason));
                    }
                    builder.Append("\n");
                }
            }
        }

        await MarkdownReportWriter.WriteUtf8Async(Path.Combine(directory, "index.md"), builder.ToString(), cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return;
        }

        foreach (var package in view.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reportPath = Path.Combine(directory, package.Id + ".md");
            await MarkdownReportWriter.WriteUtf8Async(reportPath, await FormatAuditPackageAsync(runId, projectRoot, reportPath, viewName, package, view, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> FormatAuditPackageAsync(
        string runId,
        string projectRoot,
        string reportPath,
        string viewName,
        AuditFindingPackage package,
        AuditFindingPackageView view,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .Append("# Audit package ").Append(package.Id).Append("\n\n")
            .Append("Run: ").Append(MarkdownReportWriter.FormatCodeSpan(runId))
            .Append("; view: ").Append(MarkdownReportWriter.FormatCodeSpan(viewName)).Append("; primary findings: **")
            .Append(package.Findings.Count.ToString(CultureInfo.InvariantCulture)).Append("**.\n\n")
            .Append(viewName == "all-findings"
                ? "Full-audit scope: This reference view contains every current finding. Inspect it only when the user explicitly requests a full-repository audit.\n\n"
                : string.Empty)
            .Append("## Assignment\n\n")
            .Append("Inspect every finding assigned to this package using the source, callers, contracts, and tests below. Classify each ID as false positive, acceptable design, needs clarification, or actionable, and record concrete evidence plus unresolved context. This is a technical grouping; it does not establish common cause or independent changeability. Do not claim a full audit when other packages remain unreviewed. Shared audit guidance: [run index](../../index.md).\n\n")
            .Append("## Areas\n\n");
        foreach (var area in package.Areas)
        {
            builder.Append("- **").Append(MarkdownReportWriter.EscapeInline(area.Name)).Append("** (").Append(MarkdownReportWriter.FormatCodeSpan(area.ProjectPath)).Append("; ")
                .Append(MarkdownReportWriter.EscapeInline(area.Role == ProjectRole.Tests ? "tests" : "production")).Append("; ")
                .Append(MarkdownReportWriter.EscapeInline(area.IdentityReason)).Append(')');
            if (area.FilePath is not null)
            {
                builder.Append(" — [").Append(MarkdownReportWriter.EscapeLinkText(area.FilePath)).Append("](").Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, area.FilePath)).Append(')');
            }
            foreach (var location in area.Declarations)
            {
                builder.Append(" — [").Append(MarkdownReportWriter.EscapeLinkText(location.Path)).Append(':').Append(location.StartLine.ToString(CultureInfo.InvariantCulture))
                    .Append("](").Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, location.Path)).Append("#L")
                    .Append(location.StartLine.ToString(CultureInfo.InvariantCulture)).Append(')');
            }
            builder.Append('\n');
        }

        var fileTargets = package.Findings.SelectMany(static packaged =>
                packaged.Finding.SubjectOccurrences.Select(static occurrence => occurrence.Symbol.SourcePath)
                    .Concat(packaged.Finding.Finding.Evidence.Select(static evidence => evidence.SourcePath)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (fileTargets.Length > 0)
        {
            builder.Append("\n## File navigation\n\n");
            foreach (var sourcePath in fileTargets)
            {
                builder.Append("- [").Append(MarkdownReportWriter.EscapeLinkText(sourcePath)).Append("](")
                    .Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, sourcePath)).Append(")\n");
            }
        }

        var symbolTargets = package.Findings.SelectMany(packaged => packaged.Finding.Occurrences.Concat(packaged.Finding.SubjectOccurrences)
                .Select(occurrence => (Symbol: occurrence.Symbol, occurrence.Role,
                    IsSubject: packaged.Finding.SubjectOccurrences.Contains(occurrence))))
            .DistinctBy(static item => (item.Symbol.ProjectPath, item.Symbol.SourcePath, item.Symbol.SymbolId, item.Symbol.Line,
                item.Symbol.OccurrenceId, item.Role, item.IsSubject))
            .OrderBy(static item => item.Symbol.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static item => item.Symbol.SourcePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Symbol.Line)
            .ThenBy(static item => item.Symbol.SymbolId, StringComparer.Ordinal).ToArray();
        if (symbolTargets.Length > 0)
        {
            builder.Append("\n## Symbol navigation\n\n");
            foreach (var item in symbolTargets)
            {
                builder.Append("- ").Append(item.IsSubject ? "Subject" : "Context")
                    .Append(" (").Append(MarkdownReportWriter.EscapeInline(item.Role == ProjectRole.Tests ? "tests" : "production"))
                    .Append("): ").Append(MarkdownReportWriter.FormatCodeSpan(item.Symbol.SymbolId)).Append(" — [")
                    .Append(MarkdownReportWriter.EscapeLinkText(item.Symbol.SourcePath)).Append(':').Append(item.Symbol.Line.ToString(CultureInfo.InvariantCulture))
                    .Append("](").Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, item.Symbol.SourcePath)).Append("#L")
                    .Append(item.Symbol.Line.ToString(CultureInfo.InvariantCulture)).Append(") in ")
                    .Append(MarkdownReportWriter.FormatCodeSpan(item.Symbol.ProjectPath)).Append('\n');
            }
        }

        if (package.Findings.Count == 0)
        {
            builder.Append("\nNo primary findings.\n");
        }
        else
        {
            builder.Append("\n## Assigned findings\n\n");
            foreach (var packaged in package.Findings.OrderBy(static item => item.Id, StringComparer.Ordinal))
            {
                var finding = packaged.Finding;
                var targetArea = MarkdownReportWriter.GetFindingArea(finding);
                builder.Append("### ").Append(packaged.Id).Append(" — ").Append(MarkdownReportWriter.FormatCodeSpan(finding.AnalysisId)).Append("\n\n")
                    .Append("Original finding, including rationale, metrics and evidence: [").Append(MarkdownReportWriter.EscapeLinkText(finding.Finding.SubjectId)).Append(" (line ")
                    .Append(finding.Finding.StartLine.ToString(CultureInfo.InvariantCulture)).Append(")](../../")
                    .Append(targetArea).Append('/').Append(viewName).Append('/').Append(MarkdownReportWriter.EncodePathSegment(finding.AnalysisId)).Append(".md#finding-")
                    .Append(packaged.Id).Append(")\n\n")
                    .Append("Signal: ").Append(MarkdownReportWriter.EscapeInline(MarkdownReportWriter.FormatSignal(finding.AnalysisId, finding.Finding))).Append("\n\n")
                    .Append("Assignment reasons:\n\n");
                foreach (var assignment in packaged.Assignments)
                {
                    builder.Append("- ").Append(MarkdownReportWriter.EscapeInline(assignment.Role == ProjectRole.Tests ? "tests" : "production"))
                        .Append(" subject ").Append(MarkdownReportWriter.FormatCodeSpan(assignment.SymbolId)).Append(" in [")
                        .Append(MarkdownReportWriter.EscapeLinkText(assignment.SourcePath)).Append(':').Append(assignment.Line.ToString(CultureInfo.InvariantCulture))
                        .Append("](").Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, assignment.SourcePath)).Append("#L")
                        .Append(assignment.Line.ToString(CultureInfo.InvariantCulture)).Append("): ")
                        .Append(MarkdownReportWriter.EscapeInline(assignment.Reason));
                    if (assignment.OccurrenceId is not null)
                    {
                        builder.Append(" (occurrence ").Append(MarkdownReportWriter.FormatCodeSpan(assignment.OccurrenceId)).Append(')');
                    }
                    builder.Append(" — project ").Append(MarkdownReportWriter.FormatCodeSpan(assignment.ProjectPath)).Append('\n');
                }
                if (finding.Finding.Evidence.Count > 0)
                {
                    builder.Append("\nEvidence and source locations:\n\n");
                    foreach (var evidence in finding.Finding.Evidence)
                    {
                        builder.Append("- [").Append(MarkdownReportWriter.EscapeLinkText(evidence.SourcePath)).Append(':').Append(evidence.Line.ToString(CultureInfo.InvariantCulture))
                            .Append("](").Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, evidence.SourcePath)).Append("#L")
                            .Append(evidence.Line.ToString(CultureInfo.InvariantCulture)).Append("): ").Append(MarkdownReportWriter.FormatCodeSpan(evidence.Label));
                        if (!string.IsNullOrWhiteSpace(evidence.Detail)) builder.Append(" — ").Append(MarkdownReportWriter.EscapeInline(evidence.Detail));
                        builder.Append('\n');
                    }
                }
                builder.Append('\n');
            }
        }

        if (package.TestTypes.Count > 0)
        {
            builder.Append("## Test context\n\n");
            foreach (var test in package.TestTypes.OrderBy(static item => item.TypeId, StringComparer.Ordinal))
            {
                builder.Append("- ").Append(MarkdownReportWriter.FormatCodeSpan(test.TypeId)).Append(": ").Append(MarkdownReportWriter.EscapeInline(test.Reason));
                if (test.HasBindingUncertainty) builder.Append("; binding uncertainty detected");
                foreach (var declaration in test.Declarations)
                {
                    builder.Append(" — [").Append(MarkdownReportWriter.EscapeLinkText(declaration.Path)).Append(':').Append(declaration.StartLine.ToString(CultureInfo.InvariantCulture))
                        .Append("](").Append(MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, declaration.Path)).Append("#L")
                        .Append(declaration.StartLine.ToString(CultureInfo.InvariantCulture)).Append(')');
                }
                builder.Append('\n');
            }
            builder.Append('\n');
        }

        if (package.DirectReferences.Count > 0 || package.DirectUncertainties.Count > 0)
        {
            builder.Append("## Direct source context\n\n");
            var groups = CreateReferenceGroups(package, view, reportPath, projectRoot);
            var detailPages = await WriteReferenceDetailsAsync(runId, viewName, package.Id, reportPath, groups, cancellationToken).ConfigureAwait(false);
            builder.Append("Direct source references: **").Append(package.DirectReferences.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" locations** in **").Append(package.DirectReferences.Select(reference => ReferenceGroupKey(reference, view.ProjectPaths)).Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture))
                .Append(" groups**; binding uncertainty origins: **").Append(package.DirectUncertainties.Count.ToString(CultureInfo.InvariantCulture)).Append("**.\n\n");
            var groupRows = groups.Select(group => FormatGroupSummary(package.Id, group, detailPages[group])).ToArray();
            if (groupRows.Length > ReferencePageEntryLimit || Encoding.UTF8.GetByteCount(string.Concat(groupRows)) > ReferencePageByteLimit)
            {
                var groupPages = await WriteReferenceGroupIndexAsync(runId, viewName, package.Id, reportPath, groupRows, cancellationToken).ConfigureAwait(false);
                builder.Append("Browse all reference groups: ").Append(string.Join(", ", groupPages.Select(page => "[page "
                    + page.ToString(CultureInfo.InvariantCulture) + "](" + MarkdownReportWriter.EncodePathSegment(package.Id + "-reference-groups-"
                    + page.ToString("D4", CultureInfo.InvariantCulture)) + ".md)"))).Append(".\n");
            }
            else
            {
                foreach (var row in groupRows) builder.Append(row);
            }
            builder.Append('\n');
        }

        var contextRows = view.ContextAreas.Where(row => row.PackageId == package.Id).ToArray();
        if (contextRows.Length > 0)
        {
            builder.Append("## Context areas\n\n");
            foreach (var row in contextRows)
            {
                builder.Append("- ").Append(row.IsContextOnly ? "Context only" : "Shared area").Append(": [")
                    .Append(MarkdownReportWriter.EscapeLinkText(row.Area.Name)).Append("](index.md#area-").Append(row.Area.Id).Append(") — ")
                    .Append(MarkdownReportWriter.EscapeInline(row.Area.ProjectPath)).Append("; ").Append(MarkdownReportWriter.EscapeInline(row.Area.IdentityReason)).Append('\n');
            }
            builder.Append('\n');
        }

        if (package.RelatedPackageIds.Count > 0)
        {
            builder.Append("## Related packages\n\n");
            foreach (var relatedId in package.RelatedPackageIds)
            {
                builder.Append("- [").Append(relatedId).Append("](").Append(MarkdownReportWriter.EncodePathSegment(relatedId)).Append(".md)\n");
            }
        }
        builder.Append("\nThe reference scope is static type and method relationships from the loaded C# snapshot. Property, field, and event access alone is not represented as a relationship. Runtime dispatch, reflection, dependency injection, and other unmodeled relationships may be absent.\n");
        return builder.ToString();
    }

    private static IReadOnlyList<ReferenceDetailGroup> CreateReferenceGroups(
        AuditFindingPackage package,
        AuditFindingPackageView view,
        string reportPath,
        string projectRoot)
    {
        var groups = new List<ReferenceDetailGroup>();
        var projectPaths = view.ProjectPaths;
        foreach (var group in package.DirectReferences.GroupBy(reference => string.Join("\0",
                     IsOutgoing(package, reference, projectPaths) ? "outgoing" : "incoming",
                     projectPaths.GetValueOrDefault(reference.SourceProjectId.ToString(), reference.SourcePath), reference.SourcePath, reference.SourceTypeId, reference.SourceRole,
                     projectPaths.GetValueOrDefault(reference.TargetProjectId.ToString(), reference.TargetTypeId), reference.TargetTypeId, reference.TargetMemberId, reference.TargetRole, reference.Kind), StringComparer.Ordinal)
                 .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var direction = IsOutgoing(package, first, projectPaths) ? "outgoing" : "incoming";
            var sourceProject = projectPaths.GetValueOrDefault(first.SourceProjectId.ToString(), first.SourcePath);
            var targetProject = projectPaths.GetValueOrDefault(first.TargetProjectId.ToString(), first.TargetTypeId);
            var targetArea = view.Areas.FirstOrDefault(area => area.TypeId == first.TargetTypeId
                && string.Equals(area.ProjectPath, targetProject, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            var targetLocations = targetArea?.Declarations.Select(location => "[" + MarkdownReportWriter.EscapeLinkText(location.Path) + ":"
                + location.StartLine.ToString(CultureInfo.InvariantCulture) + "](" + MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, location.Path)
                + "#L" + location.StartLine.ToString(CultureInfo.InvariantCulture) + ")").ToArray() ?? Array.Empty<string>();
            var label = $"{direction}: {MarkdownReportWriter.FormatCodeSpan(first.SourceTypeId ?? first.SourcePath)} in [{MarkdownReportWriter.EscapeLinkText(first.SourcePath)}]({MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, first.SourcePath)}) ({first.SourceRole}, project {MarkdownReportWriter.FormatCodeSpan(sourceProject)}) → {MarkdownReportWriter.FormatCodeSpan(first.TargetTypeId)} ({first.TargetRole}, project {MarkdownReportWriter.FormatCodeSpan(targetProject)})"
                + (first.TargetMemberId is null ? string.Empty : " / " + MarkdownReportWriter.FormatCodeSpan(first.TargetMemberId))
                + (targetLocations.Length == 0 ? string.Empty : "; target declarations " + string.Join(", ", targetLocations))
                + $"; {first.Kind}";
            var entries = group.OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.Span.Start).ThenBy(static item => item.Location.Span.Length)
                .ThenBy(static item => item.Location.StartLine).ThenBy(static item => item.Location.StartColumn)
                .ThenBy(static item => item.Location.EndLine).ThenBy(static item => item.Location.EndColumn)
                .Select(item => $"- Source project {MarkdownReportWriter.FormatCodeSpan(projectPaths.GetValueOrDefault(item.SourceProjectId.ToString(), item.SourcePath))}; source {MarkdownReportWriter.FormatCodeSpan(item.SourceTypeId ?? "file context")}; target project {MarkdownReportWriter.FormatCodeSpan(projectPaths.GetValueOrDefault(item.TargetProjectId.ToString(), item.TargetTypeId))}; target {MarkdownReportWriter.FormatCodeSpan(item.TargetTypeId)}; member {MarkdownReportWriter.FormatCodeSpan(item.TargetMemberId ?? "(type)")}; roles {item.SourceRole} → {item.TargetRole}; binding {item.Kind}; span {item.Location.Span.Start}..{item.Location.Span.End}; location {item.Location.StartLine}:{item.Location.StartColumn}–{item.Location.EndLine}:{item.Location.EndColumn} at [{MarkdownReportWriter.EscapeLinkText(item.SourcePath)}:{item.Location.StartLine}:{item.Location.StartColumn}]({MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, item.SourcePath)}#L{item.Location.StartLine}); target declarations {string.Join(", ", view.Areas.Where(area => area.TypeId == item.TargetTypeId && string.Equals(area.ProjectPath, projectPaths.GetValueOrDefault(item.TargetProjectId.ToString(), item.TargetTypeId), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).SelectMany(static area => area.Declarations).Select(location => "[" + MarkdownReportWriter.EscapeLinkText(location.Path) + ":" + location.StartLine.ToString(CultureInfo.InvariantCulture) + "](" + MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, location.Path) + "#L" + location.StartLine.ToString(CultureInfo.InvariantCulture) + ")"))}\n")
                .ToArray();
            groups.Add(new ReferenceDetailGroup(label, entries));
        }

        foreach (var group in package.DirectUncertainties.GroupBy(item => string.Join("\0", projectPaths.GetValueOrDefault(item.SourceProjectId.ToString(), item.SourcePath), item.SourcePath,
                     item.OriginTypeId, item.SourceRole, item.Reason, item.CandidateTypeId, item.CandidateSymbolId), StringComparer.Ordinal)
                 .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var label = $"binding uncertainty: {first.SourceRole}, project {MarkdownReportWriter.FormatCodeSpan(projectPaths.GetValueOrDefault(first.SourceProjectId.ToString(), first.SourcePath))}, origin {MarkdownReportWriter.FormatCodeSpan(first.OriginTypeId ?? "file context")}; {MarkdownReportWriter.EscapeInline(first.Reason)}; candidate {MarkdownReportWriter.FormatCodeSpan(first.CandidateSymbolId)}";
            var entries = group.OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.Span.Start).ThenBy(static item => item.Location.Span.Length)
                .ThenBy(static item => item.Location.StartLine).ThenBy(static item => item.Location.StartColumn)
                .ThenBy(static item => item.Location.EndLine).ThenBy(static item => item.Location.EndColumn)
                .Select(item => $"- Source project {MarkdownReportWriter.FormatCodeSpan(projectPaths.GetValueOrDefault(item.SourceProjectId.ToString(), item.SourcePath))}; origin project/type {MarkdownReportWriter.FormatCodeSpan(projectPaths.GetValueOrDefault(item.SourceProjectId.ToString(), item.SourcePath))} / {MarkdownReportWriter.FormatCodeSpan(item.OriginTypeId ?? "file context")}; role {item.SourceRole}; candidate type {MarkdownReportWriter.FormatCodeSpan(item.CandidateTypeId ?? "(unresolved)")}; candidate symbol {MarkdownReportWriter.FormatCodeSpan(item.CandidateSymbolId)}; reason {MarkdownReportWriter.EscapeInline(item.Reason)}; span {item.Location.Span.Start}..{item.Location.Span.End}; location {item.Location.StartLine}:{item.Location.StartColumn}–{item.Location.EndLine}:{item.Location.EndColumn}; at [{MarkdownReportWriter.EscapeLinkText(item.SourcePath)}:{item.Location.StartLine}:{item.Location.StartColumn}]({MarkdownReportWriter.FormatSourceLink(reportPath, projectRoot, item.SourcePath)}#L{item.Location.StartLine})\n")
                .ToArray();
            groups.Add(new ReferenceDetailGroup(label, entries));
        }

        return groups;
    }

    private static bool IsOutgoing(AuditFindingPackage package, AuditSourceReference reference, IReadOnlyDictionary<string, string> projectPaths)
    {
        if (package.Areas.Any(area => area.TypeId is not null && area.TypeId == reference.SourceTypeId))
        {
            return true;
        }

        var sourceProjectPath = projectPaths.GetValueOrDefault(reference.SourceProjectId.ToString());
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return sourceProjectPath is not null && package.Areas.Any(area => area.FilePath is not null
            && string.Equals(area.ProjectPath, sourceProjectPath, pathComparison)
            && string.Equals(area.FilePath, reference.SourcePath, pathComparison));
    }

    private static string ReferenceGroupKey(AuditSourceReference reference, IReadOnlyDictionary<string, string> projectPaths) => string.Join("\0",
        projectPaths.GetValueOrDefault(reference.SourceProjectId.ToString(), reference.SourcePath), reference.SourcePath,
        reference.SourceTypeId, reference.SourceRole, projectPaths.GetValueOrDefault(reference.TargetProjectId.ToString(), reference.TargetTypeId),
        reference.TargetTypeId, reference.TargetMemberId, reference.TargetRole, reference.Kind);

    private static string FormatGroupSummary(string packageId, ReferenceDetailGroup group, IReadOnlyList<int> pages) =>
        "- " + group.Label + " — **" + group.Entries.Count.ToString(CultureInfo.InvariantCulture) + " source locations**; details: "
        + string.Join(", ", pages.Select(page => "[page " + page.ToString(CultureInfo.InvariantCulture) + "](" + MarkdownReportWriter.EncodePathSegment(
            packageId + "-references-" + page.ToString("D4", CultureInfo.InvariantCulture)) + ".md)")) + "\n";

    private static async Task<IReadOnlyList<int>> WriteReferenceGroupIndexAsync(string runId, string viewName, string packageId,
        string packagePath, IReadOnlyList<string> rows, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(packagePath)!;
        var pages = new List<int>();
        var current = new List<string>();
        var pageNumber = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current.Add(row);
            var content = FormatGroupIndexPage(runId, viewName, packageId, pageNumber + 1, current);
            if (current.Count > ReferencePageEntryLimit || Encoding.UTF8.GetByteCount(content) > ReferencePageByteLimit)
            {
                current.RemoveAt(current.Count - 1);
                if (current.Count > 0)
                {
                    pageNumber++;
                    await WriteGroupIndexPageAsync(directory, runId, viewName, packageId, pageNumber, current, cancellationToken).ConfigureAwait(false);
                    pages.Add(pageNumber);
                    current.Clear();
                }
                current.Add(row);
                if (Encoding.UTF8.GetByteCount(FormatGroupIndexPage(runId, viewName, packageId, pageNumber + 1, current)) > ReferencePageByteLimit)
                {
                    current.Insert(0, "> This complete group summary exceeds the 16-KiB page target; it is retained intact.\n\n");
                    pageNumber++;
                    await WriteGroupIndexPageAsync(directory, runId, viewName, packageId, pageNumber, current, cancellationToken).ConfigureAwait(false);
                    pages.Add(pageNumber);
                    current.Clear();
                }
            }
        }
        if (current.Count > 0)
        {
            pageNumber++;
            await WriteGroupIndexPageAsync(directory, runId, viewName, packageId, pageNumber, current, cancellationToken).ConfigureAwait(false);
            pages.Add(pageNumber);
        }
        return pages;
    }

    private static Task WriteGroupIndexPageAsync(string directory, string runId, string viewName, string packageId, int page,
        IReadOnlyList<string> rows, CancellationToken cancellationToken)
    {
        var name = packageId + "-reference-groups-" + page.ToString("D4", CultureInfo.InvariantCulture) + ".md";
        return MarkdownReportWriter.WriteUtf8Async(Path.Combine(directory, name), FormatGroupIndexPage(runId, viewName, packageId, page, rows), cancellationToken);
    }

    private static string FormatGroupIndexPage(string runId, string viewName, string packageId, int page, IReadOnlyList<string> rows)
    {
        var builder = new StringBuilder().Append("# Reference groups — ").Append(packageId).Append(" — page ")
            .Append(page.ToString(CultureInfo.InvariantCulture)).Append("\n\nRun: ").Append(MarkdownReportWriter.FormatCodeSpan(runId))
            .Append("; view: ").Append(MarkdownReportWriter.FormatCodeSpan(viewName)).Append("; package: ").Append(packageId).Append(".\n\n")
            .Append(viewName == "all-findings"
                ? "> **Full-audit scope:** This reference view contains every current finding. Inspect it only when the user explicitly requests a full-repository audit.\n\n"
                : "> Scope: selected changed-files findings and their direct source context only.\n\n");
        builder.Append("[Back to package](").Append(MarkdownReportWriter.EncodePathSegment(packageId)).Append(".md)\n\n");
        foreach (var row in rows) builder.Append(row);
        return builder.ToString();
    }

    private static async Task<IReadOnlyDictionary<ReferenceDetailGroup, IReadOnlyList<int>>> WriteReferenceDetailsAsync(
        string runId,
        string viewName,
        string packageId,
        string packagePath,
        IReadOnlyList<ReferenceDetailGroup> groups,
        CancellationToken cancellationToken)
    {
        var pageByGroup = new Dictionary<ReferenceDetailGroup, IReadOnlyList<int>>();
        var pageNumber = 0;
        foreach (var group in groups)
        {
            var pages = new List<int>();
            var entryPages = PartitionReferenceEntries(group.Entries, (offset, entries) =>
                FormatReferencePage(runId, viewName, packageId, pageNumber + offset, group.Label, entries));
            foreach (var entries in entryPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pageNumber++;
                await WriteReferencePageAsync(runId, viewName, packageId, packagePath, pageNumber, group.Label, entries, cancellationToken).ConfigureAwait(false);
                pages.Add(pageNumber);
            }
            pageByGroup[group] = pages;
        }
        return pageByGroup;
    }

    internal static IReadOnlyList<IReadOnlyList<string>> PartitionReferenceEntries(
        IReadOnlyList<string> entries,
        Func<int, IReadOnlyList<string>, string> formatPage)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(formatPage);
        var pages = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        foreach (var entry in entries)
        {
            current.Add(entry);
            if (current.Count <= ReferencePageEntryLimit
                && Encoding.UTF8.GetByteCount(formatPage(pages.Count + 1, current)) <= ReferencePageByteLimit)
            {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0)
            {
                pages.Add(current.ToArray());
                current.Clear();
            }
            current.Add(entry);
            if (Encoding.UTF8.GetByteCount(formatPage(pages.Count + 1, current)) > ReferencePageByteLimit)
            {
                pages.Add(current.ToArray());
                current.Clear();
            }
        }

        if (current.Count > 0) pages.Add(current.ToArray());
        return pages;
    }

    private static async Task WriteReferencePageAsync(string runId, string viewName, string packageId, string packagePath,
        int page, string label, IReadOnlyList<string> entries, CancellationToken cancellationToken)
    {
        var name = packageId + "-references-" + page.ToString("D4", CultureInfo.InvariantCulture) + ".md";
        var path = Path.Combine(Path.GetDirectoryName(packagePath)!, name);
        await MarkdownReportWriter.WriteUtf8Async(path, FormatReferencePage(runId, viewName, packageId, page, label, entries), cancellationToken).ConfigureAwait(false);
    }

    private static string FormatReferencePage(string runId, string viewName, string packageId, int page, string label, IReadOnlyList<string> entries)
    {
        var builder = new StringBuilder().Append("# Reference details — ").Append(packageId).Append(" — page ")
            .Append(page.ToString(CultureInfo.InvariantCulture)).Append("\n\nRun: ").Append(MarkdownReportWriter.FormatCodeSpan(runId))
            .Append("; view: ").Append(MarkdownReportWriter.FormatCodeSpan(viewName)).Append("; package: ").Append(packageId).Append(".\n\n");
        builder.Append(viewName == "all-findings"
            ? "> **Full-audit scope:** This reference view contains every current finding. Inspect it only when the user explicitly requests a full-repository audit.\n\n"
            : "> Scope: selected changed-files findings and their direct source context only.\n\n");
        builder.Append("[Back to package](").Append(MarkdownReportWriter.EncodePathSegment(packageId)).Append(".md)\n\n");
        if (entries.Count == 1 && Encoding.UTF8.GetByteCount(builder.ToString()) + Encoding.UTF8.GetByteCount(entries[0]) > ReferencePageByteLimit)
        {
            builder.Append("> This complete single reference record exceeds the 16-KiB page target; it is retained intact.\n\n");
        }
        builder.Append("## ").Append(label).Append("\n\n");
        foreach (var entry in entries) builder.Append(entry);
        return builder.ToString();
    }

    private sealed record ReferenceDetailGroup(string Label, IReadOnlyList<string> Entries);

}
