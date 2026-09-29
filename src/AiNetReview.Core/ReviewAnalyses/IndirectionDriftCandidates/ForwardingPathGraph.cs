namespace AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;

/// <summary>Selects maximal root-to-endpoint paths from one project's statically bound forwarding edges.</summary>
internal static class ForwardingPathGraph
{
    public static IReadOnlyList<ForwardingPath> SelectQualifyingPaths(
        IReadOnlyList<ForwardingDeclaration> declarations,
        string projectPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var bySymbol = new Dictionary<IMethodSymbol, ForwardingDeclaration>(SymbolEqualityComparer.Default);
        foreach (var declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bySymbol.Add(declaration.Symbol, declaration);
        }
        var incoming = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declaration.Target is { } target)
            {
                incoming.Add(target.Symbol);
            }
        }

        var roots = declarations
            .Where(declaration => declaration.Target is not null && !incoming.Contains(declaration.Symbol))
            .OrderBy(static declaration => declaration.SourcePath, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.DeclarationLine)
            .ThenBy(static declaration => declaration.DocId, StringComparer.Ordinal);
        var paths = new List<ForwardingPath>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = TryFollow(root, bySymbol, projectPath, cancellationToken);
            if (path is null)
            {
                continue;
            }

            var distinctTypeCount = path.Members
                .Select(static member => member.Symbol.ContainingType)
                .Distinct(SymbolEqualityComparer.Default)
                .Count();
            var distinctFileCount = path.Members.Select(static member => member.SourcePath).Distinct(StringComparer.Ordinal).Count();
            if (path.ForwardingEdgeCount >= 2 && distinctTypeCount >= 3 && distinctFileCount >= 3)
            {
                paths.Add(path with { DistinctTypeCount = distinctTypeCount, DistinctFileCount = distinctFileCount });
            }
        }

        return paths;
    }

    private static ForwardingPath? TryFollow(
        ForwardingDeclaration root,
        IReadOnlyDictionary<IMethodSymbol, ForwardingDeclaration> bySymbol,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var members = new List<ForwardingDeclaration>();
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var current = root;
        var edgeCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current.Symbol))
            {
                return null;
            }

            members.Add(current);
            if (current.Target is not { } target)
            {
                return new ForwardingPath(projectPath, members, edgeCount, 0, 0);
            }

            if (!bySymbol.TryGetValue(target.Symbol, out var targetDeclaration))
            {
                throw new AnalysisFailedException($"Forwarding target '{target.DocId}' has no source declaration in the loaded project.");
            }

            current = targetDeclaration;
            edgeCount++;
        }
    }
}

internal sealed record ForwardingPath(
    string ProjectPath,
    IReadOnlyList<ForwardingDeclaration> Members,
    int ForwardingEdgeCount,
    int DistinctTypeCount,
    int DistinctFileCount);
