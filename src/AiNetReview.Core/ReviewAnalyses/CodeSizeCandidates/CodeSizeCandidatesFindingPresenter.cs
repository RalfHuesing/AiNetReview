namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System.Collections.Generic;
using System.Text;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;

internal sealed class CodeSizeCandidatesFindingPresenter : IReviewFindingPresenter
{
    public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
        new(
        [
            ReviewFindingPresentationFormatting.ControlFlowCountingNote,
            "\nCode-size counting and selection: Member code lines are distinct physical source lines with a non-missing C# token start in the full executable declaration; tokenless comment and blank lines do not count, while signature, attributes, and braces count where their tokens start. A multiline literal counts its token-start line; continuation lines count only if another token starts there. Type code lines sum the same token-start line counts across each non-generated part of an explicit class or record class symbol, excluding nested types and delegates. File lines are physical source lines; file bytes are UTF-8 bytes without a BOM.\n\nWithin each C# project, members meet the relative size criterion when `memberCodeLines >= max(minMemberCodeLines, project nearest-rank memberCodeLines value at percentile)` and `((decisionCount >= 8 AND decisionConstructCount >= 2) OR maxDecisionNesting >= 4)`; `extremeMemberCodeLines` is an independent inclusive threshold. Types meet the relative criterion at `typeCodeLines >= max(minTypeCodeLines, project nearest-rank typeCodeLines value at percentile)` or the independent `extremeTypeCodeLines` threshold. Files meet either inclusive threshold: `fileLines >= extremeFileLines` or `fileUtf8Bytes >= extremeFileUtf8Bytes`.\n",
        ]);

    public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) =>
        ReviewFindingPresentationFormatting.SignalOrRelatedSymbols(
            finding, Signal(finding.Finding), omitSubjectLine: finding.Finding.Discriminator == "file-size");

    private static string Signal(FindingDraft finding)
    {
        var discriminator = finding.Discriminator;
        if (discriminator == "member-size")
        {
            var signal = new StringBuilder("Member: ")
                .Append(N(finding, "memberCodeLines")).Append(" code lines; ").Append(N(finding, "decisionCount"))
                .Append(" decisions across ").Append(N(finding, "decisionConstructCount")).Append(" constructs; nesting ")
                .Append(N(finding, "maxDecisionNesting"));
            if (M(finding, "relativePathSelected") > 0)
            {
                signal.Append("; relative length-and-control-flow criterion (minimum ").Append(N(finding, "minMemberCodeLines"))
                    .Append(", P").Append(N(finding, "percentile")).Append(" value ").Append(N(finding, "memberPercentileValue")).Append(')');
            }

            if (M(finding, "extremePathSelected") > 0)
            {
                signal.Append("; extreme member-size threshold (").Append(N(finding, "extremeMemberCodeLines")).Append(')');
            }

            return signal.ToString();
        }

        if (discriminator == "type-size")
        {
            var signal = new StringBuilder("Class: ").Append(N(finding, "typeCodeLines")).Append(" code lines across ")
                .Append(N(finding, "typePartCount")).Append(" declaration parts");
            if (M(finding, "relativePathSelected") > 0)
            {
                signal.Append("; relative type-size criterion (minimum ").Append(N(finding, "minTypeCodeLines"))
                    .Append(", P").Append(N(finding, "percentile")).Append(" value ").Append(N(finding, "typePercentileValue")).Append(')');
            }

            if (M(finding, "extremePathSelected") > 0)
            {
                signal.Append("; extreme type-size threshold (").Append(N(finding, "extremeTypeCodeLines")).Append(')');
            }

            return signal.ToString();
        }

        if (discriminator == "file-size")
        {
            var signal = new StringBuilder("File: ").Append(N(finding, "fileLines")).Append(" lines; ")
                .Append(N(finding, "fileUtf8Bytes")).Append(" UTF-8 bytes");
            if (M(finding, "lineCountPathSelected") > 0)
            {
                signal.Append("; line-count threshold (").Append(N(finding, "extremeFileLines")).Append(')');
            }

            if (M(finding, "byteCountPathSelected") > 0)
            {
                signal.Append("; UTF-8 byte-count threshold (").Append(N(finding, "extremeFileUtf8Bytes")).Append(')');
            }

            return signal.ToString();
        }

        return finding.Rationale;
    }

    private static double M(FindingDraft finding, string key) => ReviewFindingPresentationFormatting.Metric(finding, key);

    private static string N(FindingDraft finding, string key) => ReviewFindingPresentationFormatting.Number(M(finding, key));
}
