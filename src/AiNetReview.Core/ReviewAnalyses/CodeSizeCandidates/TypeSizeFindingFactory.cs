namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using AiNetReview.Core.Findings;

internal static class TypeSizeFindingFactory
{
    private const string ReviewQuestion = "Do the members of this class serve one cohesive responsibility?";

    public static FindingDraft Create(TypeSizeSelection selection)
    {
        var type = selection.Type;
        var representative = type.Parts[0];
        var representativeEvidence = representative.ToEvidence();
        var rationale = $"Measured typeCodeLines is {type.CodeLines} across {type.Parts.Count} non-generated declaration parts.";
        if (selection.RelativePathSelected)
        {
            rationale += $" Included by the relative type-size path: typeCodeLines meets minimum {selection.MinimumCodeLines} and nearest-rank P{selection.Percentile} value {selection.PercentileValue}.";
        }

        if (selection.ExtremePathSelected)
        {
            rationale += $" Included by the independent extreme type-size path: typeCodeLines meets extremeTypeCodeLines {selection.ExtremeCodeLines}.";
        }

        rationale += $" The production project group contains {selection.GroupTypeCount} eligible classes; percentile ties are included. Review question: {ReviewQuestion}";

        return new FindingDraft(
            type.ProjectPath,
            representative.SourcePath,
            type.SubjectId,
            "type-size",
            representative.StartLine,
            rationale,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["typeCodeLines"] = type.CodeLines,
                ["typePartCount"] = type.Parts.Count,
                ["groupTypeCount"] = selection.GroupTypeCount,
                ["percentile"] = selection.Percentile,
                ["typePercentileValue"] = selection.PercentileValue,
                ["minTypeCodeLines"] = selection.MinimumCodeLines,
                ["extremeTypeCodeLines"] = selection.ExtremeCodeLines,
                ["relativePathSelected"] = selection.RelativePathSelected ? 1 : 0,
                ["extremePathSelected"] = selection.ExtremePathSelected ? 1 : 0,
            },
            type.Parts.Select(static part => part.ToEvidence()),
            [new FindingSymbol(type.ProjectPath, representative.SourcePath, type.SubjectId, representative.StartLine)]);
    }
}
