namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System.Collections.Generic;
using System.Linq;
using System.Threading;

internal static class FileSizeSelector
{
    public static IReadOnlyList<FileSizeSelection> Select(
        IReadOnlyList<FileSizeMeasurement> files,
        MemberSizeSelectionSettings settings,
        CancellationToken cancellationToken)
    {
        var selections = new List<FileSizeSelection>();
        foreach (var file in files.OrderBy(static item => item.SourcePath, System.StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineCountPathSelected = file.LineCount >= settings.ExtremeFileLines;
            var byteCountPathSelected = file.Utf8Bytes >= settings.ExtremeFileUtf8Bytes;
            if (lineCountPathSelected || byteCountPathSelected)
            {
                selections.Add(new FileSizeSelection(file, settings.ExtremeFileLines,
                    settings.ExtremeFileUtf8Bytes, lineCountPathSelected, byteCountPathSelected));
            }
        }

        return Array.AsReadOnly(selections.ToArray());
    }
}

internal sealed record FileSizeSelection(
    FileSizeMeasurement File,
    int ExtremeLines,
    int ExtremeUtf8Bytes,
    bool LineCountPathSelected,
    bool ByteCountPathSelected);
