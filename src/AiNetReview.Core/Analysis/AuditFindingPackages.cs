namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AiNetReview.Core.Findings;

/// <summary>Builds deterministic, per-view finding assignments from validated findings and the loaded source context.</summary>
internal static class AuditFindingPackages
{
    internal static AuditFindingPackageViews Build(
        IReadOnlyList<ReviewFinding> findings,
        AuditSourceContext sourceContext,
        bool? hasCSharpSnapshotChanges)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(sourceContext);

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var projects = sourceContext.Projects.ToDictionary(static project => project.ProjectPath, pathComparer);
        var areas = CreateAreas(sourceContext, projects, pathComparer);
        var typeAreaById = areas.Where(static area => area.TypeId is not null)
            .ToDictionary(static area => area.TypeId!, StringComparer.Ordinal);
        var fileAreas = areas.Where(static area => area.FilePath is not null)
            .ToDictionary(area => FileKey(area.ProjectPath, area.FilePath!), StringComparer.Ordinal);
        var assignments = BuildTestAssignments(sourceContext, projects, typeAreaById);
        var resolved = findings.Select(finding => ResolveFinding(finding, areas, typeAreaById, fileAreas,
                assignments, projects, sourceContext, pathComparer))
            .ToArray();

        var all = CreateView(resolved, areas, assignments, sourceContext, typeAreaById, projects, pathComparer, changedOnly: false, hasCSharpSnapshotChanges);
        var changed = CreateView(resolved.Where(item => IsChangedForReport(item.Finding.Finding, hasCSharpSnapshotChanges)).ToArray(),
            areas, assignments, sourceContext, typeAreaById, projects, pathComparer, changedOnly: true, hasCSharpSnapshotChanges);
        return new AuditFindingPackageViews(changed, all);
    }

    internal static bool IsChangedForReport(ReviewFinding finding, bool? hasCSharpSnapshotChanges) =>
        finding.AnalysisId == "missing-test-evidence-candidates"
            ? hasCSharpSnapshotChanges != false
            : finding.IsChanged;

    private static AuditFindingArea[] CreateAreas(
        AuditSourceContext context,
        IReadOnlyDictionary<string, AuditSourceProject> projects,
        StringComparer pathComparer)
    {
        var result = new List<AuditFindingArea>();
        foreach (var type in context.Types.Where(static type => !type.IsGenerated && type.Id == type.OuterTypeId))
        {
            var project = projects.Values.Single(item => item.ProjectId == type.ProjectId);
            var family = context.Types.Where(item => !item.IsGenerated && item.OuterTypeId == type.Id).ToArray();
            result.Add(new AuditFindingArea(
                StableId("area-type", project.ProjectPath, type.Id), project.ProjectPath, project.Role, type.Name,
                type.Id, null,
                Array.AsReadOnly(family.SelectMany(static item => item.Declarations).Distinct()
                    .OrderBy(static location => location.Path, StringComparer.Ordinal).ThenBy(static location => location.StartLine).ToArray()),
                "source type",
                Array.AsReadOnly(family.Select(static item => TypeDocumentationId(item.Id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                Array.Empty<string>()));
        }

        foreach (var file in context.Files.Where(static file => !file.IsGenerated))
        {
            var project = projects.Values.Single(item => item.ProjectId == file.ProjectId);
            var fileTypes = result.Where(area => pathComparer.Equals(area.ProjectPath, project.ProjectPath)
                    && area.TypeId is not null
                    && area.Declarations.Any(location => pathComparer.Equals(location.Path, file.Path)))
                .ToArray();
            result.Add(new AuditFindingArea(
                StableId("area-file", project.ProjectPath, file.Path),
                project.ProjectPath,
                project.Role,
                PathName(file.Path),
                null,
                file.Path,
                Array.Empty<AuditSourceLocation>(),
                fileTypes.Length > 1 ? "multiple types in file" : "file fallback",
                Array.Empty<string>(),
                Array.AsReadOnly(fileTypes.Select(static area => area.Id).Order(StringComparer.Ordinal).ToArray())));
        }

        return result.OrderBy(static area => area.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static area => area.FilePath, StringComparer.Ordinal)
            .ThenBy(static area => area.Name, StringComparer.Ordinal)
            .ThenBy(static area => area.Id, StringComparer.Ordinal)
            .ToArray();

    }

    private static IReadOnlyDictionary<string, AuditTestTypeAssignment> BuildTestAssignments(
        AuditSourceContext context,
        IReadOnlyDictionary<string, AuditSourceProject> projects,
        IReadOnlyDictionary<string, AuditFindingArea> typeAreas)
    {
        var references = context.References.Where(reference => reference.SourceTypeId is not null)
            .GroupBy(static reference => reference.SourceTypeId!, StringComparer.Ordinal);
        var referencesByType = references.ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var uncertainTypes = context.Uncertainties.Where(static uncertainty => uncertainty.OriginTypeId is not null)
            .Select(static uncertainty => uncertainty.OriginTypeId!).ToHashSet(StringComparer.Ordinal);
        var assignments = new Dictionary<string, AuditTestTypeAssignment>(StringComparer.Ordinal);
        foreach (var type in context.Types.Where(type => !type.IsGenerated && type.Id == type.OuterTypeId
            && projects.Values.Single(project => project.ProjectId == type.ProjectId).Role == ProjectRole.Tests))
        {
            var project = projects.Values.Single(item => item.ProjectId == type.ProjectId);
            var directTargets = referencesByType.TryGetValue(type.Id, out var sourceReferences)
                ? sourceReferences.Where(reference => reference.TargetRole == ProjectRole.Production && typeAreas.ContainsKey(reference.TargetTypeId))
                    .Select(static reference => reference.TargetTypeId).Distinct(StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            var uncertain = uncertainTypes.Contains(type.Id);
            var assignedTypeId = directTargets.Length == 1 && !uncertain ? directTargets[0] : null;
            var ownArea = typeAreas[type.Id];
            var outerArea = typeAreas.TryGetValue(type.OuterTypeId, out var mappedOuterArea) ? mappedOuterArea : ownArea;
            var targetArea = assignedTypeId is not null && typeAreas.TryGetValue(assignedTypeId, out var productionArea)
                ? productionArea
                : outerArea;
            var reason = uncertain
                ? "test type has binding uncertainty; retained in its own test area"
                : directTargets.Length == 1
                    ? assignedTypeId is null ? "test target is not uniquely represented; retained in its own test area" : "one direct production type reference"
                    : directTargets.Length > 1
                        ? "references multiple production areas; retained in its own test area"
                        : "no supported direct production type reference; retained in its own test area";
            assignments[type.Id] = new AuditTestTypeAssignment(type.Id, type.OuterTypeId, targetArea.Id,
                directTargets.Where(typeAreas.ContainsKey).Select(id => typeAreas[id].Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                uncertain, reason, type.Declarations);
        }

        return assignments;
    }

    private static ResolvedFinding ResolveFinding(
        ReviewFinding finding,
        IReadOnlyList<AuditFindingArea> areas,
        IReadOnlyDictionary<string, AuditFindingArea> typeAreas,
        IReadOnlyDictionary<string, AuditFindingArea> fileAreas,
        IReadOnlyDictionary<string, AuditTestTypeAssignment> testAssignments,
        IReadOnlyDictionary<string, AuditSourceProject> projects,
        AuditSourceContext sourceContext,
        StringComparer pathComparer)
    {
        var occurrenceAreas = new List<(string AreaId, ReviewFindingOccurrence Occurrence, string Reason)>();
        foreach (var occurrence in finding.SubjectOccurrences)
        {
            var symbol = occurrence.Symbol;
            if (!projects.TryGetValue(symbol.ProjectPath, out var project))
            {
                throw new AnalysisFailedException($"Finding '{finding.AnalysisId}/{finding.Finding.SubjectId}' references an unclassified subject project '{symbol.ProjectPath}'.");
            }

            var matchedTypeId = FindContainingType(symbol, project.ProjectId, sourceContext, pathComparer);
            if (matchedTypeId is not null)
            {
                var matched = typeAreas[matchedTypeId];
                if (occurrence.Role == ProjectRole.Tests && testAssignments.TryGetValue(matchedTypeId, out var assignment))
                {
                    occurrenceAreas.Add((assignment.AreaId, occurrence, assignment.Reason));
                }
                else
                {
                    occurrenceAreas.Add((matched.Id, occurrence, "subject symbol belongs to source type"));
                }

                continue;
            }

            var key = FileKey(symbol.ProjectPath, symbol.SourcePath);
            if (symbol.SymbolId.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var fileTypes = areas.Where(area => area.TypeId is not null && pathComparer.Equals(area.ProjectPath, symbol.ProjectPath)
                        && area.Declarations.Any(location => pathComparer.Equals(location.Path, symbol.SourcePath)))
                    .Select(area => area.TypeId!).Distinct(StringComparer.Ordinal).ToArray();
                if (fileTypes.Length == 1)
                {
                    var typeId = fileTypes[0];
                    var area = typeAreas[typeId];
                    occurrenceAreas.Add((occurrence.Role == ProjectRole.Tests && testAssignments.TryGetValue(fileTypes[0], out var testAssignment)
                        ? testAssignment.AreaId : area.Id, occurrence, "file finding falls back to its only outer source type"));
                    continue;
                }
            }

            if (fileAreas.TryGetValue(key, out var fileArea))
            {
                occurrenceAreas.Add((fileArea.Id, occurrence, "subject could not be resolved to one outer source type; file fallback"));
                continue;
            }

            throw new AnalysisFailedException($"Finding '{finding.AnalysisId}/{finding.Finding.SubjectId}' has no loaded file area for subject '{symbol.SourcePath}'.");
        }

        if (occurrenceAreas.Count == 0)
        {
            var fallbackKey = FileKey(finding.Finding.ProjectPath, finding.Finding.SourcePath);
            if (fileAreas.TryGetValue(fallbackKey, out var fallbackArea))
            {
                occurrenceAreas.Add((fallbackArea.Id,
                    new ReviewFindingOccurrence(new FindingSymbol(finding.Finding.ProjectPath, finding.Finding.SourcePath, finding.Finding.SubjectId, finding.Finding.StartLine),
                        projects.TryGetValue(finding.Finding.ProjectPath, out var roleProject) ? roleProject.Role : ProjectRole.Production),
                    "no subject occurrence resolved; representative-file fallback"));
            }
        }

        var distinct = occurrenceAreas.Select(static occurrence => occurrence.AreaId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0)
        {
            throw new AnalysisFailedException($"Finding '{finding.AnalysisId}/{finding.Finding.SubjectId}' could not be assigned to a source area.");
        }

        var identity = FindingIdentity(finding);
        var areaAssignments = occurrenceAreas.Select(item => new AuditFindingAreaAssignment(
                item.AreaId, item.Occurrence.Role, item.Occurrence.Symbol.ProjectPath, item.Occurrence.Symbol.SourcePath,
                item.Occurrence.Symbol.SymbolId, item.Occurrence.Symbol.Line, item.Occurrence.Symbol.OccurrenceId, item.Reason))
            .OrderBy(static item => item.AreaId, StringComparer.Ordinal).ThenBy(static item => item.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static item => item.SourcePath, StringComparer.Ordinal).ThenBy(static item => item.Line)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal).ThenBy(static item => item.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(static item => item.Role).ToArray();
        return new ResolvedFinding(
            new AuditPackagedFinding(StableId("finding", identity), finding, distinct, Array.AsReadOnly(areaAssignments)),
            distinct);
    }

    private static AuditFindingPackageView CreateView(
        IReadOnlyList<ResolvedFinding> findings,
        IReadOnlyList<AuditFindingArea> areas,
        IReadOnlyDictionary<string, AuditTestTypeAssignment> assignments,
        AuditSourceContext sourceContext,
        IReadOnlyDictionary<string, AuditFindingArea> typeAreaById,
        IReadOnlyDictionary<string, AuditSourceProject> projects,
        StringComparer pathComparer,
        bool changedOnly,
        bool? hasCSharpSnapshotChanges)
    {
        var packageRows = findings.GroupBy(item => PackageIdentity(item.AreaIds), StringComparer.Ordinal)
            .Select(group => new MutablePackage(StableId("package", group.Key), group.Key,
                group.Select(static item => item.Finding).OrderBy(static item => item.Id, StringComparer.Ordinal).ToArray(),
                group.SelectMany(static item => item.AreaIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(static package => package.Id, StringComparer.Ordinal).ToArray();
        var areaToPackage = packageRows.SelectMany(package => package.AreaIds.Select(areaId => (areaId, package.Id)))
            .GroupBy(static pair => pair.areaId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static pair => pair.Id).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var areasById = areas.ToDictionary(static area => area.Id, StringComparer.Ordinal);

        foreach (var package in packageRows)
        {
            foreach (var areaId in package.AreaIds)
            {
                var area = areasById[areaId];
                if (area.TypeId is not null)
                {
                    foreach (var assignment in assignments.Values.Where(item => item.AreaId == areaId))
                    {
                        package.TestTypes.Add(assignment);
                    }

                    foreach (var reference in sourceContext.References.Where(reference => reference.SourceTypeId == area.TypeId || reference.TargetTypeId == area.TypeId))
                    {
                        package.References.Add(reference);
                        var otherTypeId = reference.SourceTypeId == area.TypeId ? reference.TargetTypeId : reference.SourceTypeId;
                        if (otherTypeId is not null && typeAreaById.TryGetValue(otherTypeId, out var otherArea))
                        {
                            if (otherArea.Id != area.Id) package.ContextAreaIds.Add(otherArea.Id);
                            if (areaToPackage.TryGetValue(otherArea.Id, out var related))
                            {
                                foreach (var relatedPackage in related.Where(id => id != package.Id)) package.RelatedPackageIds.Add(relatedPackage);
                            }
                        }
                        else if (reference.SourceTypeId is null)
                        {
                            var fileContext = areas.FirstOrDefault(candidate => candidate.FilePath is not null
                                && pathComparer.Equals(candidate.ProjectPath, projects.Values.Single(project => project.ProjectId == reference.SourceProjectId).ProjectPath)
                                && pathComparer.Equals(candidate.FilePath, reference.SourcePath));
                            if (fileContext is not null && fileContext.Id != area.Id) package.ContextAreaIds.Add(fileContext.Id);
                        }
                    }

                    package.Uncertainties.UnionWith(sourceContext.Uncertainties.Where(uncertainty => uncertainty.OriginTypeId == area.TypeId));

                    foreach (var assignment in assignments.Values.Where(item => item.AreaId == areaId))
                    {
                        package.TestTypes.Add(assignment);
                        foreach (var referencedArea in assignment.ProductionContextAreaIds)
                        {
                            package.ContextAreaIds.Add(referencedArea);
                            if (areaToPackage.TryGetValue(referencedArea, out var related))
                            {
                                foreach (var relatedPackage in related.Where(id => id != package.Id)) package.RelatedPackageIds.Add(relatedPackage);
                            }
                        }
                    }
                }
                else if (area.FilePath is not null)
                {
                    package.ContextAreaIds.UnionWith(area.ContextTypeAreaIds);
                    foreach (var assignment in assignments.Values.Where(item => item.Declarations.Any(location => pathComparer.Equals(location.Path, area.FilePath))))
                    {
                        package.TestTypes.Add(assignment);
                        package.ContextAreaIds.UnionWith(assignment.ProductionContextAreaIds);
                    }

                    var project = projects.Values.Single(item => pathComparer.Equals(item.ProjectPath, area.ProjectPath));
                    foreach (var reference in sourceContext.References.Where(reference => reference.SourceTypeId is null
                        && reference.SourceProjectId == project.ProjectId && pathComparer.Equals(reference.SourcePath, area.FilePath)))
                    {
                        package.References.Add(reference);
                        if (typeAreaById.TryGetValue(reference.TargetTypeId, out var targetArea) && targetArea.Id != area.Id)
                        {
                            package.ContextAreaIds.Add(targetArea.Id);
                            if (areaToPackage.TryGetValue(targetArea.Id, out var related))
                            {
                                foreach (var relatedPackage in related.Where(id => id != package.Id)) package.RelatedPackageIds.Add(relatedPackage);
                            }
                        }
                    }

                    package.Uncertainties.UnionWith(sourceContext.Uncertainties.Where(uncertainty => uncertainty.OriginTypeId is null
                        && uncertainty.SourceProjectId == project.ProjectId && pathComparer.Equals(uncertainty.SourcePath, area.FilePath)));
                }
            }

            foreach (var packagedFinding in package.Findings)
            {
                foreach (var relatedFinding in packagedFinding.Finding.RelatedFindings)
                {
                    var relatedIdentity = StableId("finding", FindingReferenceIdentity(relatedFinding));
                    foreach (var relatedPackage in packageRows.Where(candidate => candidate.Findings.Any(item => item.Id == relatedIdentity)))
                    {
                        if (relatedPackage.Id != package.Id) package.RelatedPackageIds.Add(relatedPackage.Id);
                    }
                }
            }
        }

        // A shared finding whose subject set crosses areas has one package; all member areas remain in that package.
        var includedAreas = packageRows.SelectMany(static package => package.AreaIds).ToHashSet(StringComparer.Ordinal);
        var contextRows = packageRows.SelectMany(package => package.ContextAreaIds.Select(areaId => new AuditPackageContextArea(
                areasById[areaId], !includedAreas.Contains(areaId), package.Id)))
            .GroupBy(static context => (context.Area.Id, context.PackageId))
            .Select(static group => group.First())
            .OrderBy(static context => context.Area.Id, StringComparer.Ordinal).ThenBy(static context => context.PackageId, StringComparer.Ordinal)
            .ToArray();

        return new AuditFindingPackageView(changedOnly, Array.AsReadOnly(areas.ToArray()),
            Array.AsReadOnly(packageRows.Select(package => package.Freeze(areasById)).ToArray()),
            Array.AsReadOnly(contextRows), findings.Count, hasCSharpSnapshotChanges)
        {
            ProjectPaths = projects.Values.ToDictionary(static project => project.ProjectId.ToString(), static project => project.ProjectPath, StringComparer.Ordinal),
        };
    }

    private static string? FindContainingType(FindingSymbol symbol, Microsoft.CodeAnalysis.ProjectId projectId, AuditSourceContext context, StringComparer pathComparer) =>
        context.Subjects.FirstOrDefault(subject => subject.ProjectId == projectId
            && pathComparer.Equals(subject.SourcePath, symbol.SourcePath)
            && subject.SymbolId == symbol.SymbolId)?.OutermostTypeId;

    private static string TypeDocumentationId(string typeId)
    {
        var separator = typeId.LastIndexOf('|');
        return separator >= 0 ? typeId[(separator + 1)..] : typeId;
    }

    private static string FindingIdentity(ReviewFinding finding) => string.Join("\0", finding.AnalysisId,
        finding.Finding.ProjectPath, finding.Finding.SourcePath, finding.Finding.SubjectId, finding.Finding.Discriminator);

    private static string FindingReferenceIdentity(ReviewFindingReference finding) => string.Join("\0", finding.AnalysisId,
        finding.ProjectPath, finding.SourcePath, finding.SubjectId, finding.Discriminator);

    private static string PackageIdentity(IEnumerable<string> areaIds) => string.Join("\0", areaIds.Order(StringComparer.Ordinal));

    private static string StableId(string kind, params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", parts)));
        return kind + "-" + Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string FileKey(string projectPath, string sourcePath)
    {
        var normalizedProject = OperatingSystem.IsWindows() ? projectPath.ToUpperInvariant() : projectPath;
        var normalizedSource = OperatingSystem.IsWindows() ? sourcePath.ToUpperInvariant() : sourcePath;
        return normalizedProject + "\0" + normalizedSource;
    }

    private static string PathName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private sealed record ResolvedFinding(AuditPackagedFinding Finding, string[] AreaIds);

    private sealed class MutablePackage(string id, string identity, IReadOnlyList<AuditPackagedFinding> findings, string[] areaIds)
    {
        public string Id { get; } = id;
        public string Identity { get; } = identity;
        public IReadOnlyList<AuditPackagedFinding> Findings { get; } = findings;
        public string[] AreaIds { get; } = areaIds;
        public HashSet<string> ContextAreaIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RelatedPackageIds { get; } = new(StringComparer.Ordinal);
        public HashSet<AuditTestTypeAssignment> TestTypes { get; } = [];
        public HashSet<AuditSourceReference> References { get; } = [];
        public HashSet<AuditSourceUncertainty> Uncertainties { get; } = [];

        public AuditFindingPackage Freeze(IReadOnlyDictionary<string, AuditFindingArea> areas) => new(
            Id,
            Array.AsReadOnly(AreaIds.Select(areaId => areas[areaId]).ToArray()),
            Findings,
            Array.AsReadOnly(TestTypes.OrderBy(static item => item.TypeId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(References.OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.StartLine).ThenBy(static item => item.Location.StartColumn)
                .ThenBy(static item => item.TargetTypeId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(Uncertainties.OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.StartLine).ThenBy(static item => item.Location.StartColumn)
                .ThenBy(static item => item.CandidateSymbolId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(RelatedPackageIds.Order(StringComparer.Ordinal).ToArray()));
    }
}

internal sealed record AuditFindingArea(
    string Id,
    string ProjectPath,
    ProjectRole Role,
    string Name,
    string? TypeId,
    string? FilePath,
    IReadOnlyList<AuditSourceLocation> Declarations,
    string IdentityReason,
    IReadOnlyList<string> TypeDocumentationIds,
    IReadOnlyList<string> ContextTypeAreaIds);

internal sealed record AuditPackagedFinding(
    string Id,
    ReviewFinding Finding,
    IReadOnlyList<string> PrimaryAreaIds,
    IReadOnlyList<AuditFindingAreaAssignment> Assignments);

internal sealed record AuditFindingAreaAssignment(
    string AreaId,
    ProjectRole Role,
    string ProjectPath,
    string SourcePath,
    string SymbolId,
    int Line,
    string? OccurrenceId,
    string Reason);

internal sealed record AuditTestTypeAssignment(
    string TypeId,
    string OuterTypeId,
    string AreaId,
    IReadOnlyList<string> ProductionContextAreaIds,
    bool HasBindingUncertainty,
    string Reason,
    IReadOnlyList<AuditSourceLocation> Declarations);

internal sealed record AuditPackageContextArea(AuditFindingArea Area, bool IsContextOnly, string PackageId);

internal sealed record AuditFindingPackage(
    string Id,
    IReadOnlyList<AuditFindingArea> Areas,
    IReadOnlyList<AuditPackagedFinding> Findings,
    IReadOnlyList<AuditTestTypeAssignment> TestTypes,
    IReadOnlyList<AuditSourceReference> DirectReferences,
    IReadOnlyList<AuditSourceUncertainty> DirectUncertainties,
    IReadOnlyList<string> RelatedPackageIds);

internal sealed record AuditFindingPackageView(
    bool IsChangedFiles,
    IReadOnlyList<AuditFindingArea> Areas,
    IReadOnlyList<AuditFindingPackage> Packages,
    IReadOnlyList<AuditPackageContextArea> ContextAreas,
    int FindingCount,
    bool? HasCSharpSnapshotChanges)
{
    public IReadOnlyDictionary<string, string> ProjectPaths { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

internal sealed record AuditFindingPackageViews(
    AuditFindingPackageView ChangedFiles,
    AuditFindingPackageView AllFindings);
