namespace AiNetReview.Core.ReviewAnalyses.TypeDependencyCycleCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;

/// <summary>Reports maximal production type groups with mutual direct dependencies.</summary>
public sealed class TypeDependencyCycleCandidatesAnalysis : IReviewAnalysis
{
    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "type-dependency-cycle-candidates",
        title: "Cyclic Type Dependency Candidates",
        behaviorVersion: 1,
        purpose: "Identifies mutually dependent production type groups whose contracts and behavior may deserve joint review.",
        measurement: "Reports maximal strongly connected components with at least three production types and at least three distinct canonical declaration source files. Edges are direct statically bound dependencies in the loaded C# snapshot; test, generated, metadata, dynamic, and implicit compiler-created types are excluded.",
        reviewQuestions: ["Are the participating contracts and their mutual dependencies intentional?", "Which relationships within this group need to be considered before changing one of its contracts or behaviors?"],
        defaultEnabled: true);

    public async Task<ReviewAnalysisResult> ExecuteAsync(ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var graph = await TypeDependencyGraphBuilder.BuildAsync(context, cancellationToken).ConfigureAwait(false);
        var components = TypeDependencyCycleSelector.Select(graph, cancellationToken);
        if (components.Count == 0)
        {
            return ReviewAnalysisResult.Empty;
        }

        var findings = new List<FindingDraft>(components.Count);
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            findings.Add(await CreateFindingAsync(context, component, cancellationToken).ConfigureAwait(false));
        }

        return new ReviewAnalysisResult(findings);
    }

    private static async Task<FindingDraft> CreateFindingAsync(ReviewContext context, TypeDependencyCycleComponent component, CancellationToken cancellationToken)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var declarations = component.Nodes.SelectMany(node => node.Declarations.Select(declaration => (Node: node, Declaration: declaration)))
            .OrderBy(item => GetProjectPath(context, item.Declaration.ProjectId), pathComparer)
            .ThenBy(static item => item.Declaration.SourcePath, pathComparer)
            .ThenBy(static item => item.Declaration.Span.Start)
            .ThenBy(static item => item.Node.StableId, StringComparer.Ordinal)
            .ToArray();
        var representative = declarations[0];
        var projectPath = GetProjectPath(context, representative.Declaration.ProjectId);
        var representativeLine = await GetLineAsync(context, representative.Declaration.ProjectId, representative.Declaration.SourcePath,
            representative.Declaration.Span.Start, cancellationToken).ConfigureAwait(false);
        var edgeLines = new Dictionary<(TypeDependencyEdge Edge, TypeDependencyWitness Witness), (int Line, string Snippet)>();
        foreach (var edge in component.InternalEdges)
        {
            foreach (var witness in edge.Witnesses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                edgeLines[(edge, witness)] = await GetLineAndSnippetAsync(context, witness.ProjectId, witness.SourcePath,
                    witness.Span.Start, cancellationToken).ConfigureAwait(false);
            }
        }

        var edgeCount = component.InternalEdges.Count;
        var cycleText = string.Join(" -> ", component.IllustrativeCycle.Select(static node => node.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        var rationale = $"{component.Nodes.Count} production types in {component.DeclarationFileCount} distinct declaration files form a mutually dependent group ({edgeCount} directed dependencies). Example: {cycleText}. Review the participating contracts and whether this dependency structure is intentional.";
        var evidence = new List<FindingEvidence>();
        var relatedSymbols = new List<FindingSymbol>();
        var subjects = new List<FindingSymbol>();
        foreach (var item in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineAndSnippet = await GetLineAndSnippetAsync(context, item.Declaration.ProjectId, item.Declaration.SourcePath,
                item.Declaration.Span.Start, cancellationToken).ConfigureAwait(false);
            var ownerPath = GetProjectPath(context, item.Declaration.ProjectId);
            var symbolId = GetStableSymbolId(item.Node.Symbol);
            var symbol = new FindingSymbol(ownerPath, item.Declaration.SourcePath, symbolId, lineAndSnippet.Line);
            subjects.Add(symbol);
            relatedSymbols.Add(symbol);
            evidence.Add(new FindingEvidence(item.Declaration.SourcePath, lineAndSnippet.Line, symbolId,
                $"Declaration of participating production type in project {ownerPath}.", lineAndSnippet.Snippet));
        }

        foreach (var edge in component.InternalEdges)
        {
            foreach (var witness in edge.Witnesses)
            {
                var location = edgeLines[(edge, witness)];
                var edgeLabel = $"{GetProjectPath(context, edge.From.ProjectId)}::{GetStableSymbolId(edge.From.Symbol)} -> {GetProjectPath(context, edge.To.ProjectId)}::{GetStableSymbolId(edge.To.Symbol)}";
                evidence.Add(new FindingEvidence(witness.SourcePath, location.Line, edgeLabel,
                    $"{witness.Kind} dependency in project {GetProjectPath(context, witness.ProjectId)}.", location.Snippet));
                relatedSymbols.Add(new FindingSymbol(GetProjectPath(context, witness.ProjectId), witness.SourcePath,
                    GetStableSymbolId(edge.From.Symbol), location.Line));
            }
        }

        return new FindingDraft(projectPath, representative.Declaration.SourcePath, GetStableSymbolId(representative.Node.Symbol),
            "strongly-connected-production-type-group", representativeLine,
            rationale + " All internal edge witnesses and every participating declaration are listed below.",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["typeCount"] = component.Nodes.Count,
                ["declarationFileCount"] = component.DeclarationFileCount,
                ["projectCount"] = component.Nodes.Select(static node => node.ProjectId).Distinct().Count(),
                ["internalEdgeCount"] = component.InternalEdges.Count,
            }, evidence, relatedSymbols, subjects);
    }

    private static async Task<(int Line, string Snippet)> GetLineAndSnippetAsync(ReviewContext context, ProjectId projectId, string sourcePath, int position, CancellationToken cancellationToken)
        => await TypeDependencyFindingEvidence.GetLineAndSnippetAsync(context, projectId, sourcePath, position, cancellationToken).ConfigureAwait(false);

    private static async Task<int> GetLineAsync(ReviewContext context, ProjectId projectId, string sourcePath, int position, CancellationToken cancellationToken) =>
        await TypeDependencyFindingEvidence.GetLineAsync(context, projectId, sourcePath, position, cancellationToken).ConfigureAwait(false);

    private static string GetProjectPath(ReviewContext context, ProjectId projectId) => TypeDependencyFindingEvidence.GetProjectPath(context, projectId);

    private static string GetStableSymbolId(INamedTypeSymbol symbol) => TypeDependencyFindingEvidence.GetStableSymbolId(symbol);

    private static StringComparer StringComparerForPaths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal static class TypeDependencyCycleSelector
{
    internal static IReadOnlyList<TypeDependencyCycleComponent> Select(TypeDependencyGraph graph, CancellationToken cancellationToken)
    {
        var nodes = graph.ProductionNodes.OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray();
        var edges = graph.ProductionEdges;
        var adjacency = nodes.ToDictionary(static node => node, static _ => new List<TypeDependencyNode>());
        foreach (var edge in edges) adjacency[edge.From].Add(edge.To);
        foreach (var neighbors in adjacency.Values) neighbors.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.StableId, right.StableId));
        var components = StronglyConnectedComponents(nodes, adjacency, cancellationToken);
        var componentByNode = new Dictionary<TypeDependencyNode, int>();
        for (var index = 0; index < components.Count; index++)
        {
            foreach (var node in components[index]) componentByNode[node] = index;
        }
        var internalEdgesByComponent = Enumerable.Range(0, components.Count).Select(static _ => new List<TypeDependencyEdge>()).ToArray();
        foreach (var edge in edges)
        {
            if (componentByNode[edge.From] == componentByNode[edge.To])
            {
                internalEdgesByComponent[componentByNode[edge.From]].Add(edge);
            }
        }
        var result = new List<TypeDependencyCycleComponent>();
        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var componentNodes = components[componentIndex];
            if (componentNodes.Count < 3) continue;
            var files = componentNodes.SelectMany(static node => node.Declarations).Select(static declaration => declaration.SourcePath)
                .Distinct(StringComparerForPaths).Count();
            if (files < 3) continue;
            var internalEdges = internalEdgesByComponent[componentIndex]
                .OrderBy(static edge => edge.From.StableId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.To.StableId, StringComparer.Ordinal).ToArray();
            result.Add(new TypeDependencyCycleComponent(componentNodes, files, internalEdges, FindIllustrativeCycle(componentNodes, internalEdges, cancellationToken)));
        }
        return result.OrderBy(static component => component.Nodes[0].StableId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<TypeDependencyNode>> StronglyConnectedComponents(
        IReadOnlyList<TypeDependencyNode> nodes,
        IReadOnlyDictionary<TypeDependencyNode, List<TypeDependencyNode>> adjacency,
        CancellationToken cancellationToken)
    {
        var index = 0;
        var indices = new Dictionary<TypeDependencyNode, int>();
        var lowLinks = new Dictionary<TypeDependencyNode, int>();
        var stack = new Stack<TypeDependencyNode>();
        var onStack = new HashSet<TypeDependencyNode>();
        var result = new List<IReadOnlyList<TypeDependencyNode>>();
        void Visit(TypeDependencyNode node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indices[node] = lowLinks[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var target in adjacency[node])
            {
                if (!indices.ContainsKey(target)) { Visit(target); lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]); }
                else if (onStack.Contains(target)) lowLinks[node] = Math.Min(lowLinks[node], indices[target]);
            }
            if (lowLinks[node] != indices[node]) return;
            var component = new List<TypeDependencyNode>();
            TypeDependencyNode member;
            do { member = stack.Pop(); onStack.Remove(member); component.Add(member); } while (!ReferenceEquals(member, node));
            result.Add(component.OrderBy(static item => item.StableId, StringComparer.Ordinal).ToArray());
        }

        foreach (var node in nodes) if (!indices.ContainsKey(node)) Visit(node);
        return result;
    }

    private static IReadOnlyList<TypeDependencyNode> FindIllustrativeCycle(
        IReadOnlyList<TypeDependencyNode> nodes,
        IReadOnlyList<TypeDependencyEdge> edges,
        CancellationToken cancellationToken)
    {
        var start = nodes.OrderBy(static node => node.StableId, StringComparer.Ordinal).First();
        var adjacency = edges.GroupBy(static edge => edge.From)
            .ToDictionary(static group => group.Key, static group => group.Select(static edge => edge.To).OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray());
        var first = adjacency[start][0];
        var previous = new Dictionary<TypeDependencyNode, TypeDependencyNode?> { [first] = null };
        var queue = new Queue<TypeDependencyNode>();
        queue.Enqueue(first);
        while (queue.Count > 0 && !previous.ContainsKey(start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var targets)) continue;
            foreach (var target in targets)
            {
                if (previous.ContainsKey(target)) continue;
                previous[target] = current;
                queue.Enqueue(target);
            }
        }
        if (!previous.ContainsKey(start)) throw new AnalysisFailedException("A selected strongly connected component did not contain an illustrative cycle.");
        var path = new List<TypeDependencyNode>();
        for (TypeDependencyNode? current = start; current is not null; current = previous[current]) path.Add(current);
        path.Reverse();
        path.Insert(0, start);
        return path;
    }

    private static StringComparer StringComparerForPaths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal sealed record TypeDependencyCycleComponent(
    IReadOnlyList<TypeDependencyNode> Nodes,
    int DeclarationFileCount,
    IReadOnlyList<TypeDependencyEdge> InternalEdges,
    IReadOnlyList<TypeDependencyNode> IllustrativeCycle);
