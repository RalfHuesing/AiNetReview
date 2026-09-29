namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

internal static class MemberSizeSelector
{
    public static IReadOnlyList<MemberSizeSelection> Select(
        IReadOnlyList<ExecutableMemberMeasurement> members,
        MemberSizeSelectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (members.Count == 0)
        {
            return Array.Empty<MemberSizeSelection>();
        }

        var percentileValue = NearestRank(members.Select(static member => member.CodeLines), settings.Percentile);
        var selections = new List<MemberSizeSelection>();
        foreach (var member in members
                     .OrderBy(static member => member.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static member => member.StartLine)
                     .ThenBy(static member => member.SubjectId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePathSelected = member.CodeLines >= settings.MinimumCodeLines
                && member.CodeLines >= percentileValue
                && IsBranchy(member);
            var extremePathSelected = member.CodeLines >= settings.ExtremeCodeLines;
            if (relativePathSelected || extremePathSelected)
            {
                selections.Add(new MemberSizeSelection(
                    member,
                    members.Count,
                    settings.Percentile,
                    percentileValue,
                    settings.MinimumCodeLines,
                    settings.ExtremeCodeLines,
                    relativePathSelected,
                    extremePathSelected));
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

    private static bool IsBranchy(ExecutableMemberMeasurement member) =>
        member.DecisionCount >= 8 && member.DecisionConstructCount >= 2
        || member.MaxDecisionNesting >= 4;
}
