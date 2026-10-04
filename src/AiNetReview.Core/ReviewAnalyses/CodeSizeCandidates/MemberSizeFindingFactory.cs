namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using AiNetReview.Core.Findings;

internal static class MemberSizeFindingFactory
{
    private const string ReviewQuestion = "Is this executable body cohesive, and are its paths and tests easy to review?";

    public static FindingDraft Create(MemberSizeSelection selection)
    {
        var member = selection.Member;
        var rationale =
            $"Measured memberCodeLines is {member.CodeLines}; decisionCount is {member.DecisionCount}, decisionConstructCount is {member.DecisionConstructCount}, and maxDecisionNesting is {member.MaxDecisionNesting} as control-flow context.";
        if (selection.RelativePathSelected)
        {
            rationale = Append(rationale,
                $"Included by the relative length-and-control-flow path: memberCodeLines meets minimum {selection.MinimumCodeLines} and nearest-rank P{selection.Percentile} value {selection.PercentileValue}, and meets decisionCount >= 8 with decisionConstructCount >= 2 or maxDecisionNesting >= 4.");
        }

        if (selection.ExtremePathSelected)
        {
            rationale = Append(rationale,
                $"Included by the independent extreme-length path: memberCodeLines {member.CodeLines} meets extremeMemberCodeLines {selection.ExtremeCodeLines}, regardless of control flow.");
        }

        rationale = Append(rationale,
            $"The production project group contains {selection.GroupMemberCount} eligible executable members; percentile ties are included. Review question: {ReviewQuestion}");

        return new FindingDraft(
            member.ProjectPath,
            member.SourcePath,
            member.SubjectId,
            "member-size",
            member.StartLine,
            rationale,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["memberCodeLines"] = member.CodeLines,
                ["decisionCount"] = member.DecisionCount,
                ["decisionConstructCount"] = member.DecisionConstructCount,
                ["maxDecisionNesting"] = member.MaxDecisionNesting,
                ["groupMemberCount"] = selection.GroupMemberCount,
                ["percentile"] = selection.Percentile,
                ["memberPercentileValue"] = selection.PercentileValue,
                ["minMemberCodeLines"] = selection.MinimumCodeLines,
                ["extremeMemberCodeLines"] = selection.ExtremeCodeLines,
                ["relativePathSelected"] = selection.RelativePathSelected ? 1 : 0,
                ["extremePathSelected"] = selection.ExtremePathSelected ? 1 : 0,
            },
            [new FindingEvidence(
                member.SourcePath,
                member.Evidence.Line,
                "Executable member declaration",
                "This loaded source line identifies the measured member declaration.",
                member.Evidence.Snippet,
                OmitWhenRedundantWithSubject: true)]);
    }

    private static string Append(string existing, string addition) =>
        string.IsNullOrEmpty(existing) ? addition : existing + " " + addition;
}
