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

/// <summary>Measures member, class, and file size candidates for focused review.</summary>
public sealed class CodeSizeCandidatesAnalysis : IReviewAnalysis
{
    public IReviewFindingPresenter FindingPresenter { get; } = new CodeSizeCandidatesFindingPresenter();

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
        behaviorVersion: 2,
        purpose: "Identifies unusually large executable members, classes, and source files for focused review.",
        measurement: "memberCodeLines counts distinct token-start lines in the full executable declaration, including attributes and signature. decisionCount, decisionConstructCount, and maxDecisionNesting use ControlFlowMetrics on only the body or expression-body expression. The relative member path requires its length to meet both minMemberCodeLines and the project's nearest-rank percentile, plus the configured branching condition; extremeMemberCodeLines is independent. typeCodeLines sums CodeLineMetrics.CountOwnTypePart for every non-generated part of an explicit class or record class symbol in one project; the relative path uses minTypeCodeLines and the project's nearest-rank class percentile, while extremeTypeCodeLines is independent. fileLines is SourceText.Lines.Count and fileUtf8Bytes is the UTF-8 byte count of loaded SourceText without a BOM; either configured extreme threshold can select a file. All C# project roles, non-generated symbols, and non-generated .cs documents are measured. Test projects use testOptions per project; omitted testOptions values inherit the corresponding effective general option.",
        reviewQuestions: ["Is this executable body cohesive, and are its paths and tests easy to review?", "Do the members of this class serve one cohesive responsibility?", "Can relevant code in this file be located and edited with focused context?"],
        options: AnalysisOptions,
        testOptionNames: ["percentile", "minMemberCodeLines", "extremeMemberCodeLines", "minTypeCodeLines", "extremeTypeCodeLines", "extremeFileLines", "extremeFileUtf8Bytes"]);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<FindingDraft>();
        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp && !string.IsNullOrWhiteSpace(project.FilePath))
            .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(static project => project.Name, StringComparer.Ordinal);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectSettings = MemberSizeSelectionSettings.From(options.ForProject(project));

            var members = await ExecutableMemberCollector.CollectAsync(context, project, cancellationToken).ConfigureAwait(false);
            var selections = MemberSizeSelector.Select(members, projectSettings, cancellationToken);
            findings.AddRange(selections.Select(MemberSizeFindingFactory.Create));

            var typeMeasurements = await TypeSizeCollector.CollectAsync(context, project, cancellationToken).ConfigureAwait(false);
            var typeSelections = TypeSizeSelector.Select(typeMeasurements, projectSettings, cancellationToken);
            findings.AddRange(typeSelections.Select(TypeSizeFindingFactory.Create));

            var fileMeasurements = await FileSizeCollector.CollectAsync(context, project, cancellationToken).ConfigureAwait(false);
            var fileSelections = FileSizeSelector.Select(fileMeasurements, projectSettings, cancellationToken);
            findings.AddRange(fileSelections.Select(FileSizeFindingFactory.Create));
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
