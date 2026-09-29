namespace AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

/// <summary>Classifies paths from recognized test roots without guessing unresolved runtime targets.</summary>
internal static class MissingTestEvidencePathClassifier
{
    public static IReadOnlyDictionary<IMethodSymbol, MissingTestEvidencePathResult> Classify(
        MissingTestEvidenceSemanticGraph graph,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = graph.Nodes.ToDictionary(static node => node.Method, MethodSymbolComparer.Instance);
        var outgoing = graph.Edges
            .GroupBy(static edge => edge.From, MethodSymbolComparer.Instance)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.To)
                    .Distinct(MethodSymbolComparer.Instance)
                    .OrderBy(static method => GetStableSymbolId(method), StringComparer.Ordinal)
                    .ToArray(),
                MethodSymbolComparer.Instance);
        var directPaths = new Dictionary<IMethodSymbol, IReadOnlyList<MissingTestEvidenceGraphNode>>(MethodSymbolComparer.Instance);
        var indirectPaths = new Dictionary<IMethodSymbol, IReadOnlyList<MissingTestEvidenceGraphNode>>(MethodSymbolComparer.Instance);
        var visited = new HashSet<TraversalState>(TraversalStateComparer.Instance);
        var queue = new Queue<TraversalState>();

        foreach (var root in graph.Roots.OrderBy(static node => GetStableSymbolId(node.Method), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = new TraversalState(root, HasProductionPredecessor: false, [root]);
            if (visited.Add(state))
            {
                queue.Enqueue(state);
            }
        }

        while (queue.TryDequeue(out var state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasProductionPredecessor = state.HasProductionPredecessor;
            if (hasProductionPredecessor)
            {
                indirectPaths.TryAdd(state.Node.Method, state.Path);
            }
            else if (!state.Node.IsTestProject)
            {
                directPaths.TryAdd(state.Node.Method, state.Path);
            }

            if (!outgoing.TryGetValue(state.Node.Method, out var targets))
            {
                continue;
            }

            var nextHasProductionPredecessor = hasProductionPredecessor || !state.Node.IsTestProject;
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!nodes.TryGetValue(target, out var targetNode))
                {
                    continue;
                }

                var next = new TraversalState(
                    targetNode,
                    nextHasProductionPredecessor,
                    [.. state.Path, targetNode]);
                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        var reachable = visited.Select(static state => state.Node.Method).ToHashSet(MethodSymbolComparer.Instance);
        var globallyUncertain = graph.UncertaintyInputs.Any(input => input.IsGlobal && reachable.Contains(input.Source));
        var affectedUncertainty = graph.UncertaintyInputs
            .Where(input => !input.IsGlobal && input.AffectedMethod is not null && reachable.Contains(input.Source))
            .Select(static input => input.AffectedMethod!)
            .ToHashSet(MethodSymbolComparer.Instance);
        var uncertainQueue = new Queue<IMethodSymbol>(affectedUncertainty);
        while (uncertainQueue.TryDequeue(out var uncertainMethod))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!outgoing.TryGetValue(uncertainMethod, out var downstream))
            {
                continue;
            }

            foreach (var target in downstream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (affectedUncertainty.Add(target))
                {
                    uncertainQueue.Enqueue(target);
                }
            }
        }

        return graph.Nodes.ToDictionary(
            static node => node.Method,
            node =>
            {
                var classification = directPaths.TryGetValue(node.Method, out var directPath)
                    ? MissingTestEvidencePathKind.Direct
                    : indirectPaths.ContainsKey(node.Method)
                        ? MissingTestEvidencePathKind.Indirect
                        : MissingTestEvidencePathKind.NoPath;
                var path = classification switch
                {
                    MissingTestEvidencePathKind.Direct => directPath!,
                    MissingTestEvidencePathKind.Indirect => indirectPaths[node.Method],
                    _ => Array.Empty<MissingTestEvidenceGraphNode>(),
                };
                var uncertain = globallyUncertain || affectedUncertainty.Contains(node.Method);
                return new MissingTestEvidencePathResult(classification, path, uncertain);
            },
            MethodSymbolComparer.Instance);
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

    private sealed record TraversalState(
        MissingTestEvidenceGraphNode Node,
        bool HasProductionPredecessor,
        IReadOnlyList<MissingTestEvidenceGraphNode> Path);

    private sealed class TraversalStateComparer : IEqualityComparer<TraversalState>
    {
        public static TraversalStateComparer Instance { get; } = new();

        public bool Equals(TraversalState? left, TraversalState? right) =>
            left is not null
            && right is not null
            && left.HasProductionPredecessor == right.HasProductionPredecessor
            && MethodSymbolComparer.Instance.Equals(left.Node.Method, right.Node.Method);

        public int GetHashCode(TraversalState state) =>
            HashCode.Combine(MethodSymbolComparer.Instance.GetHashCode(state.Node.Method), state.HasProductionPredecessor);
    }

    private sealed class MethodSymbolComparer : IEqualityComparer<IMethodSymbol>
    {
        public static MethodSymbolComparer Instance { get; } = new();

        public bool Equals(IMethodSymbol? left, IMethodSymbol? right) =>
            SymbolEqualityComparer.Default.Equals(left, right);

        public int GetHashCode(IMethodSymbol method) =>
            SymbolEqualityComparer.Default.GetHashCode(method);
    }
}

internal enum MissingTestEvidencePathKind
{
    NoPath,
    Direct,
    Indirect,
}

internal sealed record MissingTestEvidencePathResult(
    MissingTestEvidencePathKind Kind,
    IReadOnlyList<MissingTestEvidenceGraphNode> Path,
    bool IsAttributionUncertain);
