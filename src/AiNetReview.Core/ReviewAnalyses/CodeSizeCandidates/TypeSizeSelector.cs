namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

internal static class TypeSizeSelector
{
    public static IReadOnlyList<TypeSizeSelection> Select(
        IReadOnlyList<TypeSizeMeasurement> types,
        MemberSizeSelectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (types.Count == 0)
        {
            return Array.Empty<TypeSizeSelection>();
        }

        var percentileValue = NearestRank(types.Select(static type => type.CodeLines), settings.Percentile);
        var selections = new List<TypeSizeSelection>();
        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePathSelected = type.CodeLines >= settings.MinimumTypeCodeLines
                && type.CodeLines >= percentileValue;
            var extremePathSelected = type.CodeLines >= settings.ExtremeTypeCodeLines;
            if (relativePathSelected || extremePathSelected)
            {
                selections.Add(new TypeSizeSelection(type, types.Count, settings.Percentile, percentileValue,
                    settings.MinimumTypeCodeLines, settings.ExtremeTypeCodeLines, relativePathSelected, extremePathSelected));
            }
        }

        return selections;
    }

    private static int NearestRank(IEnumerable<int> values, int percentile)
    {
        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(percentile / 100d * sorted.Length);
        return sorted[rank - 1];
    }
}

internal sealed record TypeSizeSelection(
    TypeSizeMeasurement Type,
    int GroupTypeCount,
    int Percentile,
    int PercentileValue,
    int MinimumCodeLines,
    int ExtremeCodeLines,
    bool RelativePathSelected,
    bool ExtremePathSelected);
