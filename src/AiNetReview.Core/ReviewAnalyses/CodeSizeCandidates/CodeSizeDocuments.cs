namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;

internal static class CodeSizeDocuments
{
    public static async Task<IReadOnlyList<CodeSizeDocument>> GetUniqueNonGeneratedByPhysicalPathAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seenPaths = new HashSet<string>(pathComparer);
        var candidates = project.Documents
            .Where(static document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Select(static document => new CodeSizeDocument(document, Path.GetFullPath(document.FilePath!)))
            .OrderBy(static item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Document.Name, StringComparer.Ordinal)
            .ThenBy(static item => item.Document.Id.Id)
            .ToArray();
        var documents = new List<CodeSizeDocument>();
        foreach (var item in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || await ReviewSourceClassifier.IsGeneratedDocumentAsync(item.Document, cancellationToken).ConfigureAwait(false)
                || !seenPaths.Add(item.FilePath))
            {
                continue;
            }

            documents.Add(item);
        }

        return Array.AsReadOnly(documents.ToArray());
    }
}

internal sealed record CodeSizeDocument(Document Document, string FilePath);
