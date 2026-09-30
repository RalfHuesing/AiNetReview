namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using AiNetReview.Core.ReviewAnalyses;

internal sealed record MemberSizeSelectionSettings(
    int Percentile,
    int MinimumCodeLines,
    int ExtremeCodeLines,
    int MinimumTypeCodeLines,
    int ExtremeTypeCodeLines,
    int ExtremeFileLines,
    int ExtremeFileUtf8Bytes)
{
    public static MemberSizeSelectionSettings From(ReviewAnalysisOptions options) => new(
        options["percentile"].GetInt32(),
        options["minMemberCodeLines"].GetInt32(),
        options["extremeMemberCodeLines"].GetInt32(),
        options["minTypeCodeLines"].GetInt32(),
        options["extremeTypeCodeLines"].GetInt32(),
        options["extremeFileLines"].GetInt32(),
        options["extremeFileUtf8Bytes"].GetInt32());
}

internal sealed record ExecutableMemberMeasurement(
    string ProjectPath,
    string SourcePath,
    string SubjectId,
    int StartLine,
    int CodeLines,
    int DecisionCount,
    int DecisionConstructCount,
    int MaxDecisionNesting,
    MemberSourceEvidence Evidence);

internal sealed record MemberSourceEvidence(int Line, string Snippet);

internal sealed record MemberSizeSelection(
    ExecutableMemberMeasurement Member,
    int GroupMemberCount,
    int Percentile,
    int PercentileValue,
    int MinimumCodeLines,
    int ExtremeCodeLines,
    bool RelativePathSelected,
    bool ExtremePathSelected);
