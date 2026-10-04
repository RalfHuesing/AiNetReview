namespace AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>Reports current clusters of similar executable C# method bodies for human review.</summary>
public sealed class DuplicateCodeCandidatesAnalysis : IReviewAnalysis
{
    public IReviewFindingPresenter FindingPresenter { get; } = new DuplicateCodeCandidatesFindingPresenter();

    private const int DefaultMinimumTokens = 30;
    private const string DefaultMinimumSimilarity = "exact";

    private static readonly ReviewAnalysisOptionDescriptor MinimumTokensOption = new(
        "minTokens",
        "Minimum number of body tokens required for a method to be considered (positive integer).",
        JsonSerializer.SerializeToElement(DefaultMinimumTokens),
        static value => value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var minimumTokens)
            && minimumTokens > 0);

    private static readonly ReviewAnalysisOptionDescriptor MinimumSimilarityOption = ReviewAnalysisOptionDescriptor.String(
        "minimumSimilarity",
        "Minimum Jaccard similarity level for a candidate pair: exact (0.95), near (0.80), or fuzzy (0.65).",
        DefaultMinimumSimilarity,
        static value => value is "exact" or "near" or "fuzzy");

    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "duplicate-code-candidates",
        title: "Duplicate Code Candidates",
        behaviorVersion: 2,
        purpose: "Flags clusters of substantially similar executable C# method bodies across production and test projects.",
        measurement: "Methods in production and test projects with at least minTokens body tokens are compared using distinct 5-token n-grams and Jaccard similarity. minimumSimilarity selects exact (0.95), near (0.80), or fuzzy (0.65). Each finding represents a connected cluster formed only from pairs that meet the selected threshold; similarityScore is the lowest qualifying pair score in that cluster. Every member includes its method identity, project-root-relative source location, token count, and source-line evidence.",
        reviewQuestions:
        [
            "Are these methods intentionally similar, or would a shared implementation improve the code?",
            "Do differences between these methods represent meaningful behavior that should remain separate?",
        ],
        options: [MinimumTokensOption, MinimumSimilarityOption]);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var minimumTokens = options["minTokens"].GetInt32();
        var minimumSimilarity = options["minimumSimilarity"].GetString()!;
        var clusters = await DuplicateCodeDetector.ScanAsync(
            context,
            minimumSimilarity,
            minimumTokens,
            cancellationToken).ConfigureAwait(false);
        if (clusters.Count == 0)
        {
            return ReviewAnalysisResult.Empty;
        }

        var sources = await LoadSourcesAsync(context, cancellationToken).ConfigureAwait(false);
        var threshold = GetThreshold(minimumSimilarity);
        var findings = new List<FindingDraft>(clusters.Count);
        foreach (var cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var representative = cluster.Members[0];
            var evidence = cluster.Members.Select(member => CreateEvidence(member, sources));
            findings.Add(new FindingDraft(
                representative.ProjectPath,
                representative.SourcePath,
                representative.Identity,
                "duplicate-code-cluster",
                representative.Line,
                $"This cluster contains {cluster.Members.Count} executable methods with similar bodies. Its conservative similarity score is {cluster.Score.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)}; the selected minimum similarity threshold is {minimumSimilarity} ({threshold.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}). Similarity alone does not prove these methods should be merged. Review each member and its source evidence.",
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["memberCount"] = cluster.Members.Count,
                    ["similarityScore"] = cluster.Score,
                    ["minimumSimilarityThreshold"] = threshold,
                },
                evidence,
                cluster.Members.Select(static member => new FindingSymbol(member.ProjectPath, member.SourcePath, member.Identity, member.Line))));
        }

        return new ReviewAnalysisResult(findings);
    }

    private static async Task<IReadOnlyDictionary<(string ProjectPath, string SourcePath), SourceText>> LoadSourcesAsync(
        ReviewContext context,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<(string ProjectPath, string SourcePath), SourceText>();
        foreach (var project in context.Solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' has no project file path.");
            }

            var projectPath = context.GetProjectRelativePath(project.FilePath);
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(document.FilePath)
                    || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sourcePath = context.GetProjectRelativePath(document.FilePath);
                sources[(projectPath, sourcePath)] = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return sources;
    }

    private static FindingEvidence CreateEvidence(
        DuplicateCodeDetector.DuplicateMethodFingerprint member,
        IReadOnlyDictionary<(string ProjectPath, string SourcePath), SourceText> sources)
    {
        if (!sources.TryGetValue((member.ProjectPath, member.SourcePath), out var sourceText))
        {
            throw new AnalysisFailedException(
                $"Duplicate method source '{member.SourcePath}' is not present in project '{member.ProjectPath}' in the loaded solution.");
        }

        if (member.Line < 1 || member.Line > sourceText.Lines.Count)
        {
            throw new AnalysisFailedException($"Duplicate method line {member.Line} is outside source '{member.SourcePath}'.");
        }

        var snippet = sourceText.Lines[member.Line - 1].ToString().Trim();
        if (snippet.Length == 0)
        {
            throw new AnalysisFailedException($"Duplicate method source line {member.Line} in '{member.SourcePath}' is empty.");
        }

        return new FindingEvidence(
            member.SourcePath,
            member.Line,
            "Duplicate method",
            $"{member.TokenCount} body tokens",
            snippet,
            RelatedSymbol: new FindingSymbol(member.ProjectPath, member.SourcePath, member.Identity, member.Line));
    }

    private static double GetThreshold(string minimumSimilarity) => minimumSimilarity switch
    {
        "exact" => 0.95,
        "near" => 0.80,
        "fuzzy" => 0.65,
        _ => throw new ArgumentOutOfRangeException(nameof(minimumSimilarity)),
    };
}
