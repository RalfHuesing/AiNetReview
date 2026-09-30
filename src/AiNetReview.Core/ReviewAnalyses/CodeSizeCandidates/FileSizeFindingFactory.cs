namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using AiNetReview.Core.Findings;

internal static class FileSizeFindingFactory
{
    private const string ReviewQuestion = "Can relevant code in this file be located and edited with focused context?";

    public static FindingDraft Create(FileSizeSelection selection)
    {
        var file = selection.File;
        var rationale = $"Measured fileLines is {file.LineCount} and fileUtf8Bytes is {file.Utf8Bytes} without a BOM.";
        if (selection.LineCountPathSelected)
        {
            rationale += $" Included by the independent line-count path: fileLines meets extremeFileLines {selection.ExtremeLines}.";
        }

        if (selection.ByteCountPathSelected)
        {
            rationale += $" Included by the independent UTF-8 byte path: fileUtf8Bytes meets extremeFileUtf8Bytes {selection.ExtremeUtf8Bytes}.";
        }

        rationale += $" Review question: {ReviewQuestion}";
        return new FindingDraft(
            file.ProjectPath,
            file.SourcePath,
            file.SubjectId,
            "file-size",
            file.Evidence.Line,
            rationale,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["fileLines"] = file.LineCount,
                ["fileUtf8Bytes"] = file.Utf8Bytes,
                ["extremeFileLines"] = selection.ExtremeLines,
                ["extremeFileUtf8Bytes"] = selection.ExtremeUtf8Bytes,
                ["lineCountPathSelected"] = selection.LineCountPathSelected ? 1 : 0,
                ["byteCountPathSelected"] = selection.ByteCountPathSelected ? 1 : 0,
            },
            [new FindingEvidence(
                file.SourcePath,
                file.Evidence.Line,
                "Large source file",
                "This loaded source line provides non-empty evidence for the measured file.",
                file.Evidence.Snippet)],
            [new FindingSymbol(file.ProjectPath, file.SourcePath, file.SubjectId, file.Evidence.Line)]);
    }
}
