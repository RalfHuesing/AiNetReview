namespace AiNetReview.Core.Findings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>Indexes loaded C# source text and ownership for finding validation in one review run.</summary>
internal sealed class FindingSourceIndex
{
    private readonly Dictionary<string, IReadOnlyList<FindingSourceDocument>> sourceDocuments;
    private readonly Dictionary<ProjectId, string> projectPaths;

    private FindingSourceIndex(
        Dictionary<string, IReadOnlyList<FindingSourceDocument>> sourceDocuments,
        Dictionary<ProjectId, string> projectPaths)
    {
        this.sourceDocuments = sourceDocuments;
        this.projectPaths = projectPaths;
    }

    public static async Task<FindingSourceIndex> BuildAsync(ReviewContext context, CancellationToken cancellationToken)
    {
        var sourceDocuments = new Dictionary<string, List<FindingSourceDocument>>(PathComparer);
        var projectPaths = new Dictionary<ProjectId, string>();
        foreach (var project in context.Solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw new AnalysisFailedException("A C# project has no project file path.");
            }

            projectPaths.Add(project.Id, context.GetProjectRelativePath(project.FilePath));
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(document.FilePath) || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = context.GetProjectRelativePath(document.FilePath);
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (!sourceDocuments.TryGetValue(relativePath, out var documents))
                {
                    documents = [];
                    sourceDocuments.Add(relativePath, documents);
                }

                documents.Add(new FindingSourceDocument(project.Id, text));
            }

            var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var document in generatedDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var generatedPath = document.FilePath ?? document.Name;
                string relativePath;
                try
                {
                    relativePath = context.GetProjectRelativePath(generatedPath);
                }
                catch (AnalysisFailedException)
                {
                    relativePath = project.Name + "/" + Path.GetFileName(generatedPath);
                }

                if (!sourceDocuments.TryGetValue(relativePath, out var documents))
                {
                    documents = [];
                    sourceDocuments.Add(relativePath, documents);
                }

                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                documents.Add(new FindingSourceDocument(project.Id, text));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new FindingSourceIndex(
            sourceDocuments.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<FindingSourceDocument>)Array.AsReadOnly(pair.Value.ToArray()),
                PathComparer),
            projectPaths);
    }

    public bool TryGetProjectOwnedSource(string projectPath, string sourcePath, out FindingSourceDocument source)
    {
        source = null!;
        if (!IsCanonicalRelativePath(projectPath) || !IsCanonicalRelativePath(sourcePath)
            || !sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || !sourceDocuments.TryGetValue(sourcePath, out var documents))
        {
            return false;
        }

        foreach (var document in documents)
        {
            if (PathComparer.Equals(projectPaths[document.ProjectId], projectPath))
            {
                source = document;
                return true;
            }
        }

        return false;
    }

    public bool TryGetSolutionSources(string sourcePath, out IReadOnlyList<FindingSourceDocument> documents)
    {
        if (IsCanonicalRelativePath(sourcePath)
            && sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && sourceDocuments.TryGetValue(sourcePath, out var indexedDocuments))
        {
            documents = indexedDocuments;
            return true;
        }

        documents = Array.Empty<FindingSourceDocument>();
        return false;
    }

    private static bool IsCanonicalRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Contains('\\')
        && path.Split('/').All(static segment => segment.Length > 0 && segment is not "." and not "..");

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

internal sealed record FindingSourceDocument(ProjectId ProjectId, SourceText Text);
