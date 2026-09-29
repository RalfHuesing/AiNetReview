namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Configuration;
using Microsoft.CodeAnalysis;

internal static class SourceSnapshotLoader
{
    internal static async Task<IReadOnlyList<SourceFileSnapshot>> CaptureAsync(
        Solution solution,
        IReadOnlyList<MarkupDocumentSnapshot> markupDocuments,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(PathComparer);
        foreach (var document in solution.Projects
                     .Where(static project => project.Language == LanguageNames.CSharp)
                     .SelectMany(static project => project.Documents))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                throw new AnalysisFailedException("A C# source document has no physical path.");
            }

            paths.Add(ProjectPathResolver.Canonicalize(document.FilePath));
        }

        foreach (var document in markupDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            paths.Add(ProjectPathResolver.Canonicalize(document.FilePath));
        }

        var snapshots = new List<SourceFileSnapshot>(paths.Count);
        foreach (var path in paths.OrderBy(static path => path, PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ProjectPathResolver.IsWithin(projectRoot, path))
            {
                throw new AnalysisFailedException("A source snapshot file is outside the configured project root.");
            }

            var relativePath = ProjectPathResolver.ToRelativeForwardSlashes(projectRoot, path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            snapshots.Add(new SourceFileSnapshot(relativePath, Convert.ToHexString(hash).ToLowerInvariant()));
        }

        return Array.AsReadOnly(snapshots.ToArray());
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
