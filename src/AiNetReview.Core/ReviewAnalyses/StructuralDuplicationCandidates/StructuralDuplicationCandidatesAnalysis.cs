namespace AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis.Text;

/// <summary>Reports exact repeated statement fragments after local and parameter name normalization.</summary>
public sealed class StructuralDuplicationCandidatesAnalysis : IReviewAnalysis
{
    public IReviewFindingPresenter FindingPresenter { get; } = new StructuralDuplicationCandidatesFindingPresenter();

    private const string ReviewQuestion = "Is this repeated structure intentional, or would a shared implementation improve maintenance without hiding meaningful differences?";
    private const string Explanation = "Statement and control-flow shape, operators, literals, and member names are retained; bound local and parameter names are normalized. Matching member spelling does not establish equivalent API behavior.";

    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "structural-duplication-candidates",
        title: "Structural Duplication Candidates",
        behaviorVersion: 2,
        purpose: "Reports exact repeated statement fragments in production and test C# source after normalizing bound local and parameter names. Statement and control-flow shape, operators, literals, and member names are retained; matching member spelling does not establish equivalent API behavior.",
        measurement: "Fragments contain at least three direct sibling statements and 60 original syntax tokens. All normalized syntax, binding distinctions, owner exclusions, exact grouping, and containment rules are fixed by the analysis contract; no custom thresholds are supported.",
        reviewQuestions: [ReviewQuestion]);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var groups = await StructuralDuplicateDetector.ScanAsync(context, cancellationToken).ConfigureAwait(false);
        if (groups.Count == 0)
        {
            return ReviewAnalysisResult.Empty;
        }

        var findings = new List<FindingDraft>(groups.Count);
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrences = group.Occurrences;
            var representative = occurrences[0];
            var evidence = occurrences.Select(CreateEvidence).ToArray();
            var symbols = occurrences.Select(occurrence => new FindingSymbol(
                occurrence.ProjectPath,
                occurrence.SourcePath,
                occurrence.OwnerId,
                GetFirstLine(occurrence),
                occurrence.StartOffset.ToString(CultureInfo.InvariantCulture) + ":" + occurrence.SpanLength.ToString(CultureInfo.InvariantCulture)));
            var executableCount = occurrences.Select(static occurrence => occurrence.OwnerKey).Distinct().Count();
            findings.Add(new FindingDraft(
                representative.ProjectPath,
                representative.SourcePath,
                representative.OwnerId,
                "structural-duplicate:" + representative.StartOffset.ToString(CultureInfo.InvariantCulture)
                    + ":" + representative.SpanLength.ToString(CultureInfo.InvariantCulture),
                GetFirstLine(representative),
                $"This group contains {occurrences.Count} exact repeated statement fragments in {executableCount} executable members. {Explanation} {ReviewQuestion}",
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["memberCount"] = occurrences.Count,
                    ["executableCount"] = executableCount,
                    ["statementCount"] = group.StatementCount,
                    ["tokenCount"] = group.TokenCount,
                },
                evidence,
                symbols));
        }

        return new ReviewAnalysisResult(findings);
    }

    private static FindingEvidence CreateEvidence(StructuralDuplicateDetector.StructuralDuplicateOccurrence occurrence)
    {
        var start = occurrence.SourceText.Lines.GetLinePosition(occurrence.StartOffset);
        var end = occurrence.SourceText.Lines.GetLinePosition(occurrence.EndOffset);
        var line = occurrence.SourceText.Lines[start.Line];
        var snippet = line.ToString().Trim();
        if (snippet.Length == 0)
        {
            throw new AnalysisFailedException($"Structural fragment source line {start.Line + 1} in '{occurrence.SourcePath}' is empty.");
        }

        return new FindingEvidence(
            occurrence.SourcePath,
            start.Line + 1,
            occurrence.OwnerId,
            "Repeated statement fragment.",
            snippet,
            SourceRange: new FindingSourceRange(occurrence.ProjectPath, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1));
    }

    private static int GetFirstLine(StructuralDuplicateDetector.StructuralDuplicateOccurrence occurrence) =>
        occurrence.SourceText.Lines.GetLineFromPosition(occurrence.StartOffset).LineNumber + 1;
}
