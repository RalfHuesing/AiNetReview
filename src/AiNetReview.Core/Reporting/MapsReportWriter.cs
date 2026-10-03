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

/// <summary>Renders the prepared solution maps into one report run.</summary>
internal static class MapsReportWriter
{
    internal static async Task WriteMapsAsync(
        string runDirectory,
        ReviewMaps? maps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(runDirectory, "maps");
        Directory.CreateDirectory(directory);
        if (maps is null)
        {
            await MarkdownReportWriter.WriteUtf8Async(
                Path.Combine(directory, "index.md"),
                FormatUnavailableIndex(),
                cancellationToken).ConfigureAwait(false);
            await MarkdownReportWriter.WriteUtf8Async(
                Path.Combine(directory, "projects.md"),
                "# Projects\n\nProject maps were not supplied with this report result.\n",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var projects = maps.Projects.OrderBy(static project => project.ProjectPath, StringComparer.Ordinal).ToArray();
        var projectByKey = projects.ToDictionary(static project => project.Key, StringComparer.Ordinal);
        ValidateProjectKeys(projects);
        var filesByProject = maps.Files.GroupBy(static file => file.ProjectKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var typesByProject = maps.Types.GroupBy(static type => type.ProjectKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(type => type.Namespace, StringComparer.Ordinal)
                .ThenBy(type => type.FullyQualifiedName, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var typesById = maps.Types.ToDictionary(static type => type.Id, StringComparer.Ordinal);

        await MarkdownReportWriter.WriteUtf8Async(Path.Combine(directory, "index.md"),
            FormatIndex(projects, maps), cancellationToken).ConfigureAwait(false);
        await MarkdownReportWriter.WriteUtf8Async(Path.Combine(directory, "projects.md"),
            FormatProjects(projects, projectByKey, filesByProject, typesByProject, cancellationToken), cancellationToken).ConfigureAwait(false);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectFiles = filesByProject.GetValueOrDefault(project.Key, []);
            var projectTypes = typesByProject.GetValueOrDefault(project.Key, []);
            var projectEdges = maps.TypeEdges.Where(edge => string.Equals(typesById[edge.FromTypeId].ProjectKey, project.Key, StringComparison.Ordinal)
                    || string.Equals(typesById[edge.ToTypeId].ProjectKey, project.Key, StringComparison.Ordinal))
                .OrderBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal).ToArray();
            var projectDirectory = GetProjectDirectory(directory, project);
            Directory.CreateDirectory(projectDirectory);
            await MarkdownReportWriter.WriteUtf8Async(Path.Combine(projectDirectory, "structure.md"),
                FormatStructure(project, projectFiles, projectTypes, cancellationToken), cancellationToken).ConfigureAwait(false);
            await MarkdownReportWriter.WriteUtf8Async(Path.Combine(projectDirectory, "dependencies.md"),
                FormatDependencies(project, projectEdges, typesById, projectByKey, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatUnavailableIndex() =>
        "# Maps\n\nProject maps were not supplied with this report result.\n\n"
        + "Choose `audit/index.md` for findings. Project maps are unavailable because map data was not supplied.\n";

    private static string FormatIndex(IReadOnlyList<ReviewMapProject> projects, ReviewMaps maps)
    {
        var builder = new StringBuilder()
            .Append("# Maps\n\n")
            .Append("Prepared from the loaded solution snapshot: ")
            .Append(projects.Count.ToString(CultureInfo.InvariantCulture)).Append(" C# projects, ")
            .Append(maps.Files.Count.ToString(CultureInfo.InvariantCulture)).Append(" non-generated C# project-file entries, ")
            .Append(maps.Types.Count.ToString(CultureInfo.InvariantCulture)).Append(" source types.\n\n")
            .Append("Choose a map: `projects.md` for project roles and references; `audit/index.md` for findings.\n\n")
            .Append("Type dependencies are direct statically bound source-type edges. Production pages show production-to-production outgoing edges; incoming edges can include production sources and test consumers. Test pages show test-to-production consumer edges. Test-to-test and production-to-test edges are outside this graph. Generated code, metadata, dynamic targets, and runtime dispatch are not inferred.\n\n")
            .Append("File counts and UTF-8 byte totals are per-project; linked files can be listed in more than one project. Folder totals count descendant files recursively. Byte values encode loaded source text as UTF-8 without a BOM. Navigation paths are relative to the containing map; source, project, and witness paths are project-root-relative.\n\n")
            .Append("From `projects.md`, open each project's `structure.md` or `dependencies.md` route.\n");
        return builder.ToString();
    }

    private static string FormatProjects(
        IReadOnlyList<ReviewMapProject> projects,
        IReadOnlyDictionary<string, ReviewMapProject> projectByKey,
        IReadOnlyDictionary<string, ReviewMapSourceFile[]> filesByProject,
        IReadOnlyDictionary<string, ReviewMapType[]> typesByProject,
        CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            return "# Projects\n\nThe prepared snapshot contains no C# projects.\n";
        }

        var builder = new StringBuilder().Append("# Projects\n\nMaps home: `index.md`.\n\n");
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append("## ").Append(MarkdownReportWriter.FormatCodeSpan(project.ProjectPath)).Append("\n\n")
                .Append("Key: ").Append(MarkdownReportWriter.FormatCodeSpan(project.Key)).Append("; role: ")
                .Append(project.Role == ProjectRole.Tests ? "tests" : "production")
                .Append("; classification: ").Append(MarkdownReportWriter.FormatCodeSpan(project.ClassificationReason.ToString())).Append(".\n\n")
                .Append("Files: ").Append(filesByProject.GetValueOrDefault(project.Key, []).Length.ToString(CultureInfo.InvariantCulture))
                .Append("; types: ").Append(typesByProject.GetValueOrDefault(project.Key, []).Length.ToString(CultureInfo.InvariantCulture)).Append(".\n\n")
                .Append("Maps: ").Append(ProjectRoute(project, "structure.md"))
                .Append("; ").Append(ProjectRoute(project, "dependencies.md")).Append(".\n\n")
                .Append("Direct loaded C# project references: ");
            var references = project.References.OrderBy(static reference => reference.TargetProjectPath, StringComparer.Ordinal).ToArray();
            if (references.Length == 0)
            {
                builder.Append("none\n\n");
                continue;
            }

            builder.Append('\n');
            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!projectByKey.TryGetValue(reference.TargetProjectKey, out var target))
                {
                    throw new InvalidDataException($"Project reference target '{reference.TargetProjectKey}' is not present in prepared maps.");
                }
                builder.Append("- ").Append(MarkdownReportWriter.FormatCodeSpan(reference.TargetProjectPath))
                    .Append(" (").Append(reference.TargetRole == ProjectRole.Tests ? "tests" : "production")
                    .Append("); ").Append(ProjectRoute(target, "structure.md")).Append('\n');
            }
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string FormatStructure(
        ReviewMapProject project,
        IReadOnlyList<ReviewMapSourceFile> files,
        IReadOnlyList<ReviewMapType> types,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .Append("# Structure — ").Append(MarkdownReportWriter.FormatCodeSpan(project.ProjectPath)).Append("\n\n")
            .Append("Project: ").Append(MarkdownReportWriter.FormatCodeSpan(project.Key)).Append("; sibling map: `dependencies.md`; projects: `../../projects.md`. File sizes are UTF-8 byte counts of loaded source text without a BOM.\n\n")
            .Append("## Folders\n\n| Folder | Files | UTF-8 bytes |\n| --- | ---: | ---: |\n");

        var folderTotals = new SortedDictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal) { ["."] = (0, 0) };
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            folderTotals["."] = (folderTotals["."].Count + 1, folderTotals["."].Bytes + file.Utf8Bytes);
            var folder = Path.GetDirectoryName(file.RelativePath.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? string.Empty;
            while (!string.IsNullOrEmpty(folder))
            {
                var total = folderTotals.GetValueOrDefault(folder);
                folderTotals[folder] = (total.Count + 1, total.Bytes + file.Utf8Bytes);
                var separator = folder.LastIndexOf('/');
                folder = separator < 0 ? string.Empty : folder[..separator];
            }
        }

        foreach (var folder in folderTotals)
        {
            builder.Append("| ").Append(MarkdownReportWriter.FormatCodeSpan(folder.Key)).Append(" | ")
                .Append(folder.Value.Count.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(folder.Value.Bytes.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }

        builder.Append("\n## Files\n\n| File | Lines | UTF-8 bytes | Namespaces |\n| --- | ---: | ---: | --- |\n");
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append("| ").Append(MarkdownReportWriter.FormatCodeSpan(file.RelativePath)).Append(" | ")
                .Append(file.Lines.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(file.Utf8Bytes.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(file.Namespaces.Count == 0 ? "—" : string.Join(", ", file.Namespaces.Select(MarkdownReportWriter.FormatCodeSpan)))
                .Append(" |\n");
        }

        builder.Append("\n## Namespaces and declared types\n\n");
        if (types.Count == 0)
        {
            builder.Append("No source type declarations were prepared.\n");
            return builder.ToString();
        }

        foreach (var namespaceGroup in types.GroupBy(static type => type.Namespace, StringComparer.Ordinal).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append("### ").Append(MarkdownReportWriter.FormatCodeSpan(namespaceGroup.Key.Length == 0 ? "(global namespace)" : namespaceGroup.Key)).Append("\n\n");
            foreach (var type in namespaceGroup.OrderBy(static type => type.FullyQualifiedName, StringComparer.Ordinal))
            {
                builder.Append("- ").Append(MarkdownReportWriter.FormatCodeSpan(type.Name)).Append(" (")
                    .Append(type.Kind).Append(", ").Append(MarkdownReportWriter.FormatCodeSpan(type.Id)).Append("): ")
                    .Append(string.Join(", ", type.Declarations.OrderBy(static declaration => declaration.SourcePath, StringComparer.Ordinal)
                        .ThenBy(static declaration => declaration.Line)
                        .Select(declaration => MarkdownReportWriter.FormatCodeSpan(declaration.SourcePath + ":" + declaration.Line.ToString(CultureInfo.InvariantCulture)))))
                    .Append('\n');
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string FormatDependencies(
        ReviewMapProject project,
        IReadOnlyList<ReviewMapTypeEdge> edges,
        IReadOnlyDictionary<string, ReviewMapType> typesById,
        IReadOnlyDictionary<string, ReviewMapProject> projectByKey,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .Append("# Type dependencies — ").Append(MarkdownReportWriter.FormatCodeSpan(project.ProjectPath)).Append("\n\n")
            .Append("Project: ").Append(MarkdownReportWriter.FormatCodeSpan(project.Key)).Append("; sibling map: `structure.md`; projects: `../../projects.md`. Direct edges only; edge locations are source witnesses.\n\n");

        var outgoing = edges.Where(edge => string.Equals(typesById[edge.FromTypeId].ProjectKey, project.Key, StringComparison.Ordinal)).ToArray();
        var incoming = edges.Where(edge => string.Equals(typesById[edge.ToTypeId].ProjectKey, project.Key, StringComparison.Ordinal)).ToArray();
        builder.Append("Outgoing: ").Append(outgoing.Length.ToString(CultureInfo.InvariantCulture))
            .Append("; incoming: ").Append(incoming.Length.ToString(CultureInfo.InvariantCulture)).Append(".\n\n");
        if (outgoing.Length == 0 && incoming.Length == 0)
        {
            builder.Append("No in-scope direct type edges were prepared for this project.\n");
            return builder.ToString();
        }

        var foreignTypeIds = edges.SelectMany(edge => new[] { edge.FromTypeId, edge.ToTypeId })
            .Select(id => typesById[id])
            .Where(type => !string.Equals(type.ProjectKey, project.Key, StringComparison.Ordinal))
            .DistinctBy(static type => type.Id, StringComparer.Ordinal)
            .ToArray();
        builder.Append("## Foreign type IDs\n\n");
        if (foreignTypeIds.Length == 0)
        {
            builder.Append("None.\n\n");
        }
        else
        {
            builder.Append("| Owner project | IDs | Structure map |\n| --- | --- | --- |\n");
            foreach (var ownerGroup in foreignTypeIds.GroupBy(static type => type.ProjectKey, StringComparer.Ordinal)
                         .OrderBy(static group => group.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var owner = projectByKey[ownerGroup.Key];
                builder.Append("| ").Append(MarkdownReportWriter.FormatCodeSpan(owner.Key)).Append(" | ")
                    .Append(string.Join(", ", ownerGroup.Select(FormatTypeReference).Order(StringComparer.Ordinal)))
                    .Append(" | ").Append(MarkdownReportWriter.FormatCodeSpan(StructureRoute(owner)))
                    .Append(" |\n");
            }
            builder.Append('\n');
        }

        builder.Append("## Outgoing edges\n\nIDs resolve in namespace sections of the owning structure map; foreign IDs resolve in the table above. Each file heading applies to its line locations.\n\n");
        if (outgoing.Length == 0)
        {
            builder.Append("None.\n\n");
        }
        var witnessedOutgoing = outgoing.SelectMany(edge => edge.Witnesses.Select(witness => (Edge: edge, Witness: witness)))
            .GroupBy(static item => (item.Witness.ProjectKey, item.Witness.SourcePath))
            .OrderBy(static group => group.Key.ProjectKey, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.SourcePath, StringComparer.Ordinal)
            .ToArray();
        foreach (var fileGroup in witnessedOutgoing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append("### ");
            if (!string.Equals(fileGroup.Key.ProjectKey, project.Key, StringComparison.Ordinal))
            {
                builder.Append(MarkdownReportWriter.FormatCodeSpan(fileGroup.Key.ProjectKey)).Append(':');
            }
            builder.Append(MarkdownReportWriter.FormatCodeSpan(fileGroup.Key.SourcePath)).Append("\n\n");
            foreach (var edgeGroup in fileGroup.GroupBy(static item => (item.Edge.FromTypeId, item.Edge.ToTypeId))
                         .OrderBy(static group => group.Key.FromTypeId, StringComparer.Ordinal)
                         .ThenBy(static group => group.Key.ToTypeId, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var locations = edgeGroup.Select(static item => item.Witness)
                    .OrderBy(static witness => witness.Line)
                    .ThenBy(static witness => witness.Kind, StringComparer.Ordinal)
                    .Select(witness => "L" + witness.Line.ToString(CultureInfo.InvariantCulture) + " " + witness.Kind);
                builder.Append("- ").Append(MarkdownReportWriter.FormatCodeSpan(edgeGroup.Key.FromTypeId))
                    .Append(" → ").Append(MarkdownReportWriter.FormatCodeSpan(edgeGroup.Key.ToTypeId))
                    .Append(": ").Append(string.Join(", ", locations)).Append('\n');
            }
            builder.Append('\n');
        }

        var unwitnessed = outgoing.Where(static edge => edge.Witnesses.Count == 0)
            .OrderBy(static edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(static edge => edge.ToTypeId, StringComparer.Ordinal).ToArray();
        if (unwitnessed.Length > 0)
        {
            builder.Append("## Outgoing edges without retained witnesses\n\n");
            foreach (var edge in unwitnessed)
                builder.Append("- ").Append(MarkdownReportWriter.FormatCodeSpan(edge.FromTypeId)).Append(" → ")
                    .Append(MarkdownReportWriter.FormatCodeSpan(edge.ToTypeId)).Append('\n');
            builder.Append('\n');
        }

        builder.Append("## Incoming edges\n\n");
        if (incoming.Length == 0)
        {
            builder.Append("None.\n");
        }
        else
        {
            foreach (var sourceGroup in incoming.GroupBy(edge => typesById[edge.FromTypeId].ProjectKey, StringComparer.Ordinal)
                         .OrderBy(static group => group.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceProject = projectByKey[sourceGroup.Key];
                builder.Append("### ").Append(MarkdownReportWriter.FormatCodeSpan(sourceProject.Key))
                    .Append(" — outgoing map: ").Append(MarkdownReportWriter.FormatCodeSpan(DependencyRoute(sourceProject))).Append("\n\n");
                foreach (var targetGroup in sourceGroup.GroupBy(static edge => edge.ToTypeId, StringComparer.Ordinal)
                             .OrderBy(static group => group.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    builder.Append("- ").Append(MarkdownReportWriter.FormatCodeSpan(targetGroup.Key)).Append(" ← ")
                        .Append(string.Join(", ", targetGroup.Select(static edge => edge.FromTypeId).Order(StringComparer.Ordinal)
                            .Select(MarkdownReportWriter.FormatCodeSpan))).Append('\n');
                }
                builder.Append('\n');
            }
        }
        return builder.ToString();
    }

    private static string FormatTypeReference(ReviewMapType type) =>
        MarkdownReportWriter.FormatCodeSpan(type.Id);

    private static string ProjectRoute(ReviewMapProject project, string file) =>
        MarkdownReportWriter.FormatCodeSpan((project.Role == ProjectRole.Tests ? "tests" : "production") + "/" + project.Key + "/" + file);

    private static string DependencyRoute(ReviewMapProject source) =>
        "../../" + (source.Role == ProjectRole.Tests ? "tests" : "production") + "/" + source.Key + "/dependencies.md";

    private static string StructureRoute(ReviewMapProject owner) =>
        "../../" + (owner.Role == ProjectRole.Tests ? "tests" : "production") + "/" + owner.Key + "/structure.md";

    private static string GetProjectDirectory(string mapsDirectory, ReviewMapProject project) =>
        Path.Combine(mapsDirectory, project.Role == ProjectRole.Tests ? "tests" : "production", project.Key);

    private static void ValidateProjectKeys(IReadOnlyList<ReviewMapProject> projects)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var key = project.Key;
            if (string.IsNullOrWhiteSpace(key) || key is "." or ".."
                || key.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
                || !keys.Add(key))
            {
                throw new InvalidDataException("Prepared map project keys must be unique, non-empty path-safe ASCII identifiers.");
            }
        }
    }
}
