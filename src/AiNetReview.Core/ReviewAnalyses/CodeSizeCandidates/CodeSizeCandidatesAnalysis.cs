namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;

/// <summary>Measures executable member size candidates without registering the analysis in production.</summary>
internal sealed class CodeSizeCandidatesAnalysis : IReviewAnalysis
{
    private static readonly ReviewAnalysisOptionDescriptor[] AnalysisOptions =
    [
        IntegerOption("percentile", "Nearest-rank percentile for member and type size groups (50 through 99).", 90, 50, 99),
        IntegerOption("minMemberCodeLines", "Minimum token-start lines for the relative member selection path.", 80, 1, int.MaxValue),
        IntegerOption("extremeMemberCodeLines", "Independent extreme token-start-line threshold for members.", 300, 1, int.MaxValue),
        IntegerOption("minTypeCodeLines", "Minimum token-start lines for the relative type selection path.", 300, 1, int.MaxValue),
        IntegerOption("extremeTypeCodeLines", "Independent extreme token-start-line threshold for types.", 800, 1, int.MaxValue),
        IntegerOption("extremeFileLines", "Independent extreme source-line threshold for files.", 1000, 1, int.MaxValue),
        IntegerOption("extremeFileUtf8Bytes", "Independent extreme UTF-8 byte threshold for files.", 131072, 1, int.MaxValue),
    ];

    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "code-size-candidates",
        title: "Code Size Candidates",
        behaviorVersion: 1,
        purpose: "Identifies unusually large executable members for focused review.",
        measurement: "memberCodeLines counts distinct token-start lines in the full executable declaration, including attributes and signature. decisionCount, decisionConstructCount, and maxDecisionNesting use ControlFlowMetrics on only the body or expression-body expression. The relative path requires the member length to meet both minMemberCodeLines and the project's nearest-rank percentile, plus decisionCount >= 8 with at least two constructs or maxDecisionNesting >= 4. The independent extreme path requires extremeMemberCodeLines and does not depend on control flow. Eligible members are explicit methods, constructors, accessors, operators, conversions, and expression-bodied properties or indexers. Local functions, lambdas, anonymous methods, destructors, declarations without bodies, and non-implementing partial methods are excluded. Only production C# projects and non-generated source symbols/documents are measured.",
        reviewQuestions: ["Is this executable body cohesive, and are its paths and tests easy to review?"],
        options: AnalysisOptions);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = MemberSizeSelectionSettings.From(options);
        var findings = new List<FindingDraft>();
        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp && !string.IsNullOrWhiteSpace(project.FilePath))
            .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(static project => project.Name, StringComparer.Ordinal);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReviewSourceClassifier.IsTestProject(project))
            {
                continue;
            }

            var members = await ExecutableMemberCollector.CollectAsync(context, project, cancellationToken).ConfigureAwait(false);
            var selections = MemberSizeSelector.Select(members, settings, cancellationToken);
            findings.AddRange(selections.Select(MemberSizeFindingFactory.Create));
        }

        return new ReviewAnalysisResult(findings);
    }

    private static ReviewAnalysisOptionDescriptor IntegerOption(string name, string description, int defaultValue, int minimum, int maximum) =>
        new(
            name,
            description,
            JsonSerializer.SerializeToElement(defaultValue),
            value => value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var integer)
                && integer >= minimum
                && integer <= maximum);
}
