namespace AiNetReview.Core.ReviewAnalyses.TypeDependencyHubCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;

/// <summary>Reports production types with broad direct production neighborhoods in both directions.</summary>
public sealed class TypeDependencyHubCandidatesAnalysis : IReviewAnalysis
{
    public IReviewFindingPresenter FindingPresenter { get; } = new TypeDependencyHubCandidatesFindingPresenter();

    private static readonly ReviewAnalysisOptionDescriptor MinFanInOption = PositiveIntegerOption(
        "minFanIn", "Minimum distinct production consumer types for a dependency hub.", 10);
    private static readonly ReviewAnalysisOptionDescriptor MinFanOutOption = PositiveIntegerOption(
        "minFanOut", "Minimum distinct production dependency types for a dependency hub.", 10);

    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "type-dependency-hub-candidates",
        title: "Type Dependency Hub Candidates",
        behaviorVersion: 1,
        purpose: "Shows the direct production consumer and dependency neighborhood of types with broad relationships in both directions.",
        measurement: "Reports production types whose distinct direct production fan-in and fan-out both meet their inclusive positive integer thresholds. Direct test consumers are separate context and do not affect production counts. Edges are statically bound dependencies in the loaded C# snapshot; generated, metadata, dynamic, and implicit compiler-created types are excluded.",
        reviewQuestions:
        [
            "Which listed production consumers and dependencies need consideration before changing this type's contract or behavior?",
            "Are the type's direct relationships in both directions intentional?",
        ],
        options: [MinFanInOption, MinFanOutOption],
        defaultEnabled: true);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var minFanIn = options["minFanIn"].GetInt32();
        var minFanOut = options["minFanOut"].GetInt32();
        var graph = await context.GetTypeDependencyGraphAsync(cancellationToken).ConfigureAwait(false);
        var candidates = Select(graph, minFanIn, minFanOut, cancellationToken);
        if (candidates.Count == 0)
        {
            return ReviewAnalysisResult.Empty;
        }

        var findings = new List<FindingDraft>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            findings.Add(await CreateFindingAsync(context, candidate, minFanIn, minFanOut, cancellationToken).ConfigureAwait(false));
        }

        return new ReviewAnalysisResult(findings);
    }

    internal static IReadOnlyList<TypeDependencyHubCandidate> Select(
        TypeDependencyGraph graph,
        int minFanIn,
        int minFanOut,
        CancellationToken cancellationToken)
    {
        var productionNodes = graph.ProductionNodes;
        var productionNodeSet = productionNodes.ToHashSet();
        var productionEdges = graph.ProductionEdges
            .Where(edge => productionNodeSet.Contains(edge.From) && productionNodeSet.Contains(edge.To))
            .ToArray();
        var testEdges = graph.TestContextEdges
            .Where(edge => productionNodeSet.Contains(edge.To))
            .ToArray();
        var incoming = productionNodes.ToDictionary(static node => node, static _ => new List<TypeDependencyEdge>());
        var outgoing = productionNodes.ToDictionary(static node => node, static _ => new List<TypeDependencyEdge>());
        var testConsumersByTarget = productionNodes.ToDictionary(static node => node, static _ => new List<TypeDependencyEdge>());
        foreach (var edge in productionEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            incoming[edge.To].Add(edge);
            outgoing[edge.From].Add(edge);
        }

        foreach (var edge in testEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            testConsumersByTarget[edge.To].Add(edge);
        }

        var candidates = new List<TypeDependencyHubCandidate>();
        foreach (var node in productionNodes.OrderBy(static node => node.StableId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var consumers = incoming[node].OrderBy(static edge => edge.From.StableId, StringComparer.Ordinal).ToArray();
            var dependencies = outgoing[node].OrderBy(static edge => edge.To.StableId, StringComparer.Ordinal).ToArray();
            if (consumers.Length < minFanIn || dependencies.Length < minFanOut)
            {
                continue;
            }

            var testConsumers = testConsumersByTarget[node]
                .OrderBy(static edge => edge.From.StableId, StringComparer.Ordinal).ToArray();
            candidates.Add(new TypeDependencyHubCandidate(node, consumers, dependencies, testConsumers,
                CountNeighborFiles(consumers.Select(static edge => edge.From), TypeDependencyFindingEvidence.PathComparer),
                CountNeighborFiles(dependencies.Select(static edge => edge.To), TypeDependencyFindingEvidence.PathComparer),
                consumers.Select(static edge => edge.From.ProjectId).Distinct().Count(),
                dependencies.Select(static edge => edge.To.ProjectId).Distinct().Count()));
        }

        return candidates;
    }

    private static async Task<FindingDraft> CreateFindingAsync(
        ReviewContext context,
        TypeDependencyHubCandidate candidate,
        int minFanIn,
        int minFanOut,
        CancellationToken cancellationToken)
    {
        var node = candidate.Node;
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var declarations = node.Declarations
            .OrderBy(declaration => GetProjectPath(context, declaration.ProjectId), pathComparer)
            .ThenBy(static declaration => declaration.SourcePath, pathComparer)
            .ThenBy(static declaration => declaration.Span.Start)
            .ToArray();
        if (declarations.Length == 0)
        {
            throw new AnalysisFailedException($"Dependency hub type '{node.StableId}' has no eligible declaration location.");
        }

        var representative = declarations[0];
        var projectPath = GetProjectPath(context, representative.ProjectId);
        var representativeLine = await GetLineAsync(context, representative.ProjectId, representative.SourcePath,
            representative.Span.Start, cancellationToken).ConfigureAwait(false);
        var evidence = new List<FindingEvidence>();
        var relatedSymbols = new List<FindingSymbol>();
        var subjectSymbols = new List<FindingSymbol>();
        foreach (var declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (line, snippet) = await GetLineAndSnippetAsync(context, declaration.ProjectId, declaration.SourcePath,
                declaration.Span.Start, cancellationToken).ConfigureAwait(false);
            var symbolId = GetStableSymbolId(node.Symbol);
            var symbol = new FindingSymbol(GetProjectPath(context, declaration.ProjectId), declaration.SourcePath, symbolId, line);
            subjectSymbols.Add(symbol);
            relatedSymbols.Add(symbol);
            evidence.Add(new FindingEvidence(declaration.SourcePath, line, symbolId,
                $"Production type declaration in project {GetProjectPath(context, declaration.ProjectId)}.", snippet));
        }

        await AddEdgeEvidenceAsync(context, candidate.ProductionConsumers, "Production consumer", evidence, relatedSymbols, cancellationToken).ConfigureAwait(false);
        await AddEdgeEvidenceAsync(context, candidate.ProductionDependencies, "Production dependency", evidence, relatedSymbols, cancellationToken).ConfigureAwait(false);
        await AddEdgeEvidenceAsync(context, candidate.TestConsumers, "Direct test consumer", evidence, relatedSymbols, cancellationToken).ConfigureAwait(false);

        var testConsumerCount = candidate.TestConsumers.Select(static edge => edge.From).Distinct().Count();
        var rationale = $"{node.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} has {candidate.ProductionConsumers.Count} direct production consumer types (minimum {minFanIn}) and {candidate.ProductionDependencies.Count} direct production dependency types (minimum {minFanOut}). Review the listed consumers and dependencies before changing its contract or behavior. {testConsumerCount} direct test consumer types are listed separately.";

        return new FindingDraft(projectPath, representative.SourcePath, GetStableSymbolId(node.Symbol),
            "production-type-dependency-hub", representativeLine, rationale,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["fanIn"] = candidate.ProductionConsumers.Count,
                ["fanOut"] = candidate.ProductionDependencies.Count,
                ["minFanIn"] = minFanIn,
                ["minFanOut"] = minFanOut,
                ["fanInNeighborFileCount"] = candidate.FanInNeighborFileCount,
                ["fanOutNeighborFileCount"] = candidate.FanOutNeighborFileCount,
                ["fanInNeighborProjectCount"] = candidate.FanInNeighborProjectCount,
                ["fanOutNeighborProjectCount"] = candidate.FanOutNeighborProjectCount,
                ["testConsumerCount"] = testConsumerCount,
            }, evidence, relatedSymbols, subjectSymbols);
    }

    private static async Task AddEdgeEvidenceAsync(
        ReviewContext context,
        IReadOnlyList<TypeDependencyEdge> edges,
        string role,
        ICollection<FindingEvidence> evidence,
        ICollection<FindingSymbol> relatedSymbols,
        CancellationToken cancellationToken)
    {
        foreach (var edge in edges)
        {
            foreach (var witness in edge.Witnesses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (line, snippet) = await GetLineAndSnippetAsync(context, witness.ProjectId, witness.SourcePath,
                    witness.Span.Start, cancellationToken).ConfigureAwait(false);
                var fromProject = GetProjectPath(context, edge.From.ProjectId);
                var toProject = GetProjectPath(context, edge.To.ProjectId);
                var fromSymbol = GetStableSymbolId(edge.From.Symbol);
                var toSymbol = GetStableSymbolId(edge.To.Symbol);
                evidence.Add(new FindingEvidence(witness.SourcePath, line,
                    $"{fromProject}::{fromSymbol} -> {toProject}::{toSymbol}",
                    $"{role}; {witness.Kind} dependency witnessed in project {GetProjectPath(context, witness.ProjectId)}.", snippet));
                relatedSymbols.Add(new FindingSymbol(GetProjectPath(context, witness.ProjectId), witness.SourcePath,
                    GetStableSymbolId(edge.From.Symbol), line));
                await AddNodeRelatedSymbolAsync(context, edge.From, relatedSymbols, cancellationToken).ConfigureAwait(false);
                await AddNodeRelatedSymbolAsync(context, edge.To, relatedSymbols, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task AddNodeRelatedSymbolAsync(
        ReviewContext context,
        TypeDependencyNode node,
        ICollection<FindingSymbol> relatedSymbols,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in node.Declarations.OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Span.Start))
        {
            var line = await GetLineAsync(context, declaration.ProjectId, declaration.SourcePath,
                declaration.Span.Start, cancellationToken).ConfigureAwait(false);
            relatedSymbols.Add(new FindingSymbol(GetProjectPath(context, declaration.ProjectId), declaration.SourcePath,
                GetStableSymbolId(node.Symbol), line));
        }
    }

    private static int CountNeighborFiles(IEnumerable<TypeDependencyNode> nodes, StringComparer pathComparer) =>
        nodes.SelectMany(static node => node.Declarations).Select(static declaration => declaration.SourcePath)
            .Distinct(pathComparer).Count();

    private static async Task<(int Line, string Snippet)> GetLineAndSnippetAsync(
        ReviewContext context,
        ProjectId projectId,
        string sourcePath,
        int position,
        CancellationToken cancellationToken) =>
        await TypeDependencyFindingEvidence.GetLineAndSnippetAsync(context, projectId, sourcePath, position, cancellationToken).ConfigureAwait(false);

    private static async Task<int> GetLineAsync(ReviewContext context, ProjectId projectId, string sourcePath, int position,
        CancellationToken cancellationToken) =>
        (await GetLineAndSnippetAsync(context, projectId, sourcePath, position, cancellationToken).ConfigureAwait(false)).Line;

    private static string GetProjectPath(ReviewContext context, ProjectId projectId)
        => TypeDependencyFindingEvidence.GetProjectPath(context, projectId);

    private static string GetStableSymbolId(INamedTypeSymbol symbol) => TypeDependencyFindingEvidence.GetStableSymbolId(symbol);

    private static ReviewAnalysisOptionDescriptor PositiveIntegerOption(string name, string description, int defaultValue) =>
        new(name, description, JsonSerializer.SerializeToElement(defaultValue),
            static value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0);
}

internal sealed record TypeDependencyHubCandidate(
    TypeDependencyNode Node,
    IReadOnlyList<TypeDependencyEdge> ProductionConsumers,
    IReadOnlyList<TypeDependencyEdge> ProductionDependencies,
    IReadOnlyList<TypeDependencyEdge> TestConsumers,
    int FanInNeighborFileCount,
    int FanOutNeighborFileCount,
    int FanInNeighborProjectCount,
    int FanOutNeighborProjectCount);
