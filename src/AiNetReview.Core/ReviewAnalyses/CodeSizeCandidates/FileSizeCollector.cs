namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

internal static class FileSizeCollector
{
    public static async Task<IReadOnlyList<FileSizeMeasurement>> CollectAsync(
        ReviewContext context,
        Project project,
        CancellationToken cancellationToken)
    {
        var measurements = new List<FileSizeMeasurement>();
        foreach (var item in await CodeSizeDocuments.GetUniqueNonGeneratedByPhysicalPathAsync(project, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = item.Document;

            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!TryCreateEvidence(sourceText, out var evidence))
            {
                continue;
            }

            var sourcePath = context.GetProjectRelativePath(item.FilePath);
            var projectPath = context.GetProjectRelativePath(project.FilePath!);
            measurements.Add(new FileSizeMeasurement(
                projectPath,
                sourcePath,
                "file:" + sourcePath,
                sourceText.Lines.Count,
                Encoding.UTF8.GetByteCount(sourceText.ToString()),
                evidence));
        }

        return Array.AsReadOnly(measurements.ToArray());
    }

    private static bool TryCreateEvidence(SourceText sourceText, out FileSourceEvidence evidence)
    {
        for (var index = 0; index < sourceText.Lines.Count; index++)
        {
            var lineText = sourceText.Lines[index].ToString();
            if (string.IsNullOrWhiteSpace(lineText))
            {
                continue;
            }

            var snippet = lineText.Trim();
            if (snippet.Length > 180)
            {
                snippet = snippet[..180];
            }

            evidence = new FileSourceEvidence(index + 1, snippet);
            return true;
        }

        evidence = null!;
        return false;
    }
}

internal sealed record FileSizeMeasurement(
    string ProjectPath,
    string SourcePath,
    string SubjectId,
    int LineCount,
    int Utf8Bytes,
    FileSourceEvidence Evidence);

internal sealed record FileSourceEvidence(int Line, string Snippet);
