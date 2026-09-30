namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

internal static class CodeSizeDocuments
{
    public static IReadOnlyList<CodeSizeDocument> GetUniqueByPhysicalPath(Project project)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seenPaths = new HashSet<string>(pathComparer);
        var documents = project.Documents
            .Where(static document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Select(static document => new CodeSizeDocument(document, Path.GetFullPath(document.FilePath!)))
            .OrderBy(static item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Document.Name, StringComparer.Ordinal)
            .ThenBy(static item => item.Document.Id.Id)
            .Where(item => seenPaths.Add(item.FilePath))
            .ToArray();

        return Array.AsReadOnly(documents);
    }
}

internal sealed record CodeSizeDocument(Document Document, string FilePath);
