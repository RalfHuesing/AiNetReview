namespace AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>Reports structurally nontrivial production functions without a direct static test path.</summary>
public sealed class MissingTestEvidenceCandidatesAnalysis : IReviewAnalysis
{
    private const string NoPathCategory = "no-static-test-path";
    private const string IndirectOnlyCategory = "indirect-test-path-only";

    private static readonly ReviewAnalysisOptionDescriptor MinDecisionCountOption = PositiveIntegerOption(
        "minDecisionCount", "Minimum decision count for a nontrivial candidate.", 3);
    private static readonly ReviewAnalysisOptionDescriptor MinDecisionNestingOption = PositiveIntegerOption(
        "minDecisionNesting", "Minimum nested-decision depth for a nontrivial candidate.", 2);
    private static readonly ReviewAnalysisOptionDescriptor MinIndirectDecisionCountOption = PositiveIntegerOption(
        "minIndirectDecisionCount", "Minimum decision count for an indirect-only candidate.", 5);
    private static readonly ReviewAnalysisOptionDescriptor MinIndirectDecisionNestingOption = PositiveIntegerOption(
        "minIndirectDecisionNesting", "Minimum nested-decision depth for an indirect-only candidate.", 3);

    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "missing-test-evidence-candidates",
        title: "Missing Test Evidence Candidates",
        behaviorVersion: 1,
        purpose: "Identifies structurally nontrivial production functions without a direct static path from a recognized test root.",
        measurement: "Reports one review finding for each eligible function with no resolved test path, or with indirect paths only when it also meets the indirect complexity thresholds. Static paths and complexity are measured from the loaded C# snapshot; this is not runtime coverage evidence.",
        reviewQuestions:
        [
            "Which behaviors, boundaries, and failure cases of this function deserve tests?",
            "Could a test reach this function through a public entry point, especially when the function is private?",
        ],
        options: [MinDecisionCountOption, MinDecisionNestingOption, MinIndirectDecisionCountOption, MinIndirectDecisionNestingOption],
        defaultEnabled: true);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var minDecisionCount = options["minDecisionCount"].GetInt32();
        var minDecisionNesting = options["minDecisionNesting"].GetInt32();
        var minIndirectDecisionCount = options["minIndirectDecisionCount"].GetInt32();
        var minIndirectDecisionNesting = options["minIndirectDecisionNesting"].GetInt32();
        var candidates = await MissingTestEvidenceCandidateSelector.SelectAsync(
            context.Solution,
            minDecisionCount,
            minDecisionNesting,
            minIndirectDecisionCount,
            minIndirectDecisionNesting,
            cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return ReviewAnalysisResult.Empty;
        }

        var graph = await MissingTestEvidenceSemanticGraphBuilder.BuildAsync(context.Solution, cancellationToken).ConfigureAwait(false);
        var paths = MissingTestEvidencePathClassifier.Classify(graph, cancellationToken);
        var findings = new List<FindingDraft>();
        foreach (var candidate in candidates.OrderBy(static item => item.Document.FilePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Declaration.SpanStart)
                     .ThenBy(static item => GetStableSymbolId(item.Method), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.TryGetValue(candidate.Method, out var path))
            {
                throw new AnalysisFailedException($"Test-path classification is missing candidate '{GetStableSymbolId(candidate.Method)}'.");
            }

            if (path.Kind == MissingTestEvidencePathKind.Direct
                || path.Kind == MissingTestEvidencePathKind.Indirect && !candidate.MeetsIndirectThresholds)
            {
                continue;
            }

            findings.Add(await CreateFindingAsync(context, candidate, path, minDecisionCount, minDecisionNesting,
                minIndirectDecisionCount, minIndirectDecisionNesting, cancellationToken).ConfigureAwait(false));
        }

        return findings.Count == 0 ? ReviewAnalysisResult.Empty : new ReviewAnalysisResult(findings);
    }

    private static async Task<FindingDraft> CreateFindingAsync(
        ReviewContext context,
        MissingTestEvidenceFunctionCandidate candidate,
        MissingTestEvidencePathResult path,
        int minDecisionCount,
        int minDecisionNesting,
        int minIndirectDecisionCount,
        int minIndirectDecisionNesting,
        CancellationToken cancellationToken)
    {
        var document = candidate.Document;
        if (string.IsNullOrWhiteSpace(document.FilePath) || string.IsNullOrWhiteSpace(document.Project.FilePath))
        {
            throw new AnalysisFailedException($"Candidate '{GetStableSymbolId(candidate.Method)}' has no source or project path.");
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var declarationLine = sourceText.Lines.GetLineFromPosition(candidate.Declaration.SpanStart);
        var sourcePath = context.GetProjectRelativePath(document.FilePath);
        var projectPath = context.GetProjectRelativePath(document.Project.FilePath);
        var symbolId = GetStableSymbolId(candidate.Method);
        var category = path.Kind == MissingTestEvidencePathKind.NoPath ? NoPathCategory : IndirectOnlyCategory;
        var categoryLabel = path.Kind == MissingTestEvidencePathKind.NoPath ? "no static test path" : "indirect test path only";
        var uncertaintyLabel = path.IsAttributionUncertain ? " Attribution uncertain: unresolved bindings, method groups, or virtual/interface dispatch could provide a stronger path." : string.Empty;
        var pathNodes = new List<string>();
        if (path.Kind == MissingTestEvidencePathKind.Indirect)
        {
            foreach (var node in path.Path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pathDocument = await GetPathDocumentAsync(context.Solution, node, cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Indirect test path source document is unavailable for '{GetStableSymbolId(node.Method)}'.");
                var pathLocation = GetDocumentSourcePath(context, node.ProjectName, pathDocument);
                var pathText = await pathDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var pathLine = (pathText.Lines.GetLineFromPosition(node.DeclarationSpan.Start).LineNumber + 1)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);

                pathNodes.Add($"{GetStableSymbolId(node.Method)} ({pathLocation}:{pathLine})");
            }
        }

        var pathDetail = pathNodes.Count > 0
            ? " Shortest resolved test path: " + string.Join(" -> ", pathNodes) + "."
            : string.Empty;
        var rationale = $"{categoryLabel}: decisionCount={candidate.Measurement.DecisionCount}, maxDecisionNesting={candidate.Measurement.MaxDecisionNesting}; effective thresholds minDecisionCount={minDecisionCount}, minDecisionNesting={minDecisionNesting}, minIndirectDecisionCount={minIndirectDecisionCount}, minIndirectDecisionNesting={minIndirectDecisionNesting}.{uncertaintyLabel}{pathDetail} Static association is not runtime coverage. Review which behavior, boundary, and failure cases deserve tests, including reachability through a public entry point for private functions.";
        var declarationSnippet = declarationLine.ToString().Trim();
        if (declarationSnippet.Length == 0)
        {
            throw new AnalysisFailedException($"Candidate declaration line is empty in '{sourcePath}'.");
        }

        var evidence = new List<FindingEvidence>
        {
            new(sourcePath, declarationLine.LineNumber + 1, symbolId,
                $"Eligible production function reported as {categoryLabel}.", declarationSnippet),
        };

        var relatedSymbols = new List<FindingSymbol>
        {
            new(projectPath, sourcePath, symbolId, declarationLine.LineNumber + 1),
        };
        if (path.Kind == MissingTestEvidencePathKind.Indirect)
        {
            foreach (var node in path.Path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node.DocumentId is null || string.IsNullOrWhiteSpace(node.FilePath))
                {
                    throw new AnalysisFailedException($"Indirect test path contains a node without a source location: '{GetStableSymbolId(node.Method)}'.");
                }

                var pathDocument = await GetPathDocumentAsync(context.Solution, node, cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Indirect test path source document is unavailable for '{GetStableSymbolId(node.Method)}'.");

                var pathText = await pathDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var line = pathText.Lines.GetLineFromPosition(node.DeclarationSpan.Start);
                var pathSource = GetDocumentSourcePath(context, node.ProjectName, pathDocument);
                if (string.IsNullOrWhiteSpace(pathDocument.Project.FilePath))
                {
                    throw new AnalysisFailedException($"Indirect test path project file is unavailable for '{GetStableSymbolId(node.Method)}'.");
                }

                var pathProject = context.GetProjectRelativePath(pathDocument.Project.FilePath);
                var pathId = GetStableSymbolId(node.Method);
                evidence.Add(new FindingEvidence(pathSource, line.LineNumber + 1, pathId,
                    "Function on the shortest resolved indirect test path.", line.ToString().Trim()));
                relatedSymbols.Add(new FindingSymbol(pathProject, pathSource, pathId, line.LineNumber + 1));
            }
        }

        return new FindingDraft(
            projectPath,
            sourcePath,
            symbolId,
            category,
            declarationLine.LineNumber + 1,
            rationale,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["decisionCount"] = candidate.Measurement.DecisionCount,
                ["maxDecisionNesting"] = candidate.Measurement.MaxDecisionNesting,
                ["minDecisionCount"] = minDecisionCount,
                ["minDecisionNesting"] = minDecisionNesting,
                ["minIndirectDecisionCount"] = minIndirectDecisionCount,
                ["minIndirectDecisionNesting"] = minIndirectDecisionNesting,
                ["attributionUncertain"] = path.IsAttributionUncertain ? 1 : 0,
            },
            evidence,
            relatedSymbols,
            subjectSymbols: [new FindingSymbol(projectPath, sourcePath, symbolId, declarationLine.LineNumber + 1)]);
    }

    private static async Task<Document?> GetPathDocumentAsync(
        Solution solution,
        MissingTestEvidenceGraphNode node,
        CancellationToken cancellationToken)
    {
        if (solution.GetDocument(node.DocumentId) is { } document)
        {
            return document;
        }

        var project = solution.GetProject(node.ProjectId);
        if (project is null)
        {
            return null;
        }

        var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false);
        return generatedDocuments.FirstOrDefault(candidate => candidate.Id == node.DocumentId);
    }

    private static string GetDocumentSourcePath(ReviewContext context, string projectName, Document document)
    {
        var sourcePath = document.FilePath ?? document.Name;
        try
        {
            return context.GetProjectRelativePath(sourcePath);
        }
        catch (AnalysisFailedException)
        {
            return projectName + "/" + System.IO.Path.GetFileName(sourcePath);
        }
    }

    private static string GetStableSymbolId(IMethodSymbol method)
    {
        var normalized = method.OriginalDefinition;
        if (normalized.PartialDefinitionPart is { } definition)
        {
            normalized = definition.OriginalDefinition;
        }

        return DocumentationCommentId.CreateDeclarationId(normalized)
            ?? normalized.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static ReviewAnalysisOptionDescriptor PositiveIntegerOption(string name, string description, int defaultValue) => new(
        name,
        description,
        JsonSerializer.SerializeToElement(defaultValue),
        static value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0);
}
