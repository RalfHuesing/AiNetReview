namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Finds deterministic token-similarity clusters in a loaded solution snapshot.</summary>
internal static class DuplicateCodeDetector
{
    private const int NgramSize = 5;
    private const int MinimumSharedNgrams = 3;
    private const int DefaultMinimumTokens = 30;

    internal static async Task<IReadOnlyList<DuplicateCodeCluster>> ScanAsync(
        ReviewContext context,
        string similarity,
        int minimumTokens = DefaultMinimumTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var threshold = similarity switch
        {
            "exact" => 0.95,
            "near" => 0.80,
            "fuzzy" => 0.65,
            _ => throw new ArgumentOutOfRangeException(nameof(similarity)),
        };
        if (minimumTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumTokens));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = await CollectAsync(context, minimumTokens, cancellationToken).ConfigureAwait(false);
        var edges = FindEdges(candidates, threshold, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var clusters = BuildClusters(candidates, edges, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return clusters;
    }

    private static async Task<List<DuplicateMethodFingerprint>> CollectAsync(
        ReviewContext context,
        int minimumTokens,
        CancellationToken cancellationToken)
    {
        var result = new List<DuplicateMethodFingerprint>();
        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp)
            .Where(static project => !ReviewSourceClassifier.IsTestProject(project))
            .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(static project => project.Name, StringComparer.Ordinal);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' has no project file path.");
            }

            var projectPath = context.GetProjectRelativePath(project.FilePath);
            foreach (var document in project.Documents.OrderBy(static document => document.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(document.FilePath)
                    || !string.Equals(Path.GetExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)
                    || await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var sourcePath = context.GetProjectRelativePath(document.FilePath);
                foreach (var declaration in root.DescendantNodes().Where(IsSupportedDeclaration))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var body = GetBody(declaration);
                    if (body is null)
                    {
                        continue;
                    }

                    if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol symbol
                        || symbol.IsImplicitlyDeclared
                        || !IsSupportedMethodKind(symbol.MethodKind)
                        || ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                    {
                        continue;
                    }

                    var tokens = body.DescendantTokens().ToArray();
                    if (tokens.Length < minimumTokens)
                    {
                        continue;
                    }

                    var hashes = BuildNgrams(tokens);
                    if (hashes.Count == 0)
                    {
                        continue;
                    }

                    var declarationLocation = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
                    var line = declarationLocation?.GetLineSpan().StartLinePosition.Line + 1
                        ?? declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var identity = DocumentationCommentId.CreateDeclarationId(symbol)
                        ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    result.Add(new DuplicateMethodFingerprint(
                        projectPath,
                        sourcePath,
                        identity,
                        line,
                        tokens.Length,
                        hashes));
                }
            }
        }

        return result
            .OrderBy(static item => item.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Identity, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsSupportedDeclaration(SyntaxNode node) => node is
        MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax;

    private static SyntaxNode? GetBody(SyntaxNode declaration) => declaration switch
    {
        BaseMethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody,
        AccessorDeclarationSyntax accessor => accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody,
        LocalFunctionStatementSyntax localFunction => localFunction.Body ?? (SyntaxNode?)localFunction.ExpressionBody,
        _ => null,
    };

    private static bool IsSupportedMethodKind(MethodKind kind) => kind is
        MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
        or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd
        or MethodKind.EventRemove or MethodKind.LocalFunction;

    private static HashSet<ulong> BuildNgrams(IReadOnlyList<SyntaxToken> tokens)
    {
        var hashes = new HashSet<ulong>();
        for (var start = 0; start <= tokens.Count - NgramSize; start++)
        {
            hashes.Add(HashNgram(tokens, start));
        }

        return hashes;
    }

    private static ulong HashNgram(IReadOnlyList<SyntaxToken> tokens, int start)
    {
        unchecked
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offsetBasis;
            for (var index = start; index < start + NgramSize; index++)
            {
                foreach (var character in tokens[index].Text)
                {
                    hash ^= character;
                    hash *= prime;
                }

                hash ^= '\u0001';
                hash *= prime;
            }

            return hash;
        }
    }

    private static List<DuplicateMethodEdge> FindEdges(
        IReadOnlyList<DuplicateMethodFingerprint> candidates,
        double threshold,
        CancellationToken cancellationToken)
    {
        var invertedIndex = new Dictionary<ulong, List<int>>();
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var hash in candidates[candidateIndex].Ngrams)
            {
                if (!invertedIndex.TryGetValue(hash, out var indices))
                {
                    indices = [];
                    invertedIndex.Add(hash, indices);
                }

                indices.Add(candidateIndex);
            }
        }

        var sharedCounts = new Dictionary<(int A, int B), int>();
        foreach (var indices in invertedIndex.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var left = 0; left < indices.Count; left++)
            {
                for (var right = left + 1; right < indices.Count; right++)
                {
                    var pair = (indices[left], indices[right]);
                    sharedCounts[pair] = sharedCounts.GetValueOrDefault(pair) + 1;
                }
            }
        }

        var edges = new List<DuplicateMethodEdge>();
        foreach (var (pair, sharedCount) in sharedCounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sharedCount < MinimumSharedNgrams)
            {
                continue;
            }

            var score = ComputeJaccard(candidates[pair.A].Ngrams, candidates[pair.B].Ngrams);
            if (score >= threshold)
            {
                edges.Add(new DuplicateMethodEdge(pair.A, pair.B, score));
            }
        }

        return edges;
    }

    private static double ComputeJaccard(HashSet<ulong> left, HashSet<ulong> right)
    {
        var (smaller, larger) = left.Count <= right.Count ? (left, right) : (right, left);
        var intersection = smaller.Count(larger.Contains);
        return (double)intersection / (left.Count + right.Count - intersection);
    }

    private static IReadOnlyList<DuplicateCodeCluster> BuildClusters(
        IReadOnlyList<DuplicateMethodFingerprint> candidates,
        IReadOnlyList<DuplicateMethodEdge> edges,
        CancellationToken cancellationToken)
    {
        var unionFind = new DuplicateUnionFind(candidates.Count);
        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            unionFind.Union(edge.A, edge.B);
        }

        var groups = new Dictionary<int, List<int>>();
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = unionFind.Find(index);
            if (!groups.TryGetValue(root, out var members))
            {
                members = [];
                groups.Add(root, members);
            }

            members.Add(index);
        }

        return groups.Values
            .Where(static members => members.Count > 1)
            .Select(members =>
            {
                var memberSet = members.ToHashSet();
                var score = edges
                    .Where(edge => memberSet.Contains(edge.A) && memberSet.Contains(edge.B))
                    .Min(static edge => edge.Score);
                return new DuplicateCodeCluster(
                    members.Select(index => candidates[index])
                        .OrderBy(static item => item.ProjectPath, StringComparer.Ordinal)
                        .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
                        .ThenBy(static item => item.Line)
                        .ThenBy(static item => item.Identity, StringComparer.Ordinal)
                        .ToArray(),
                    score);
            })
            .OrderByDescending(static cluster => cluster.Score)
            .ThenBy(static cluster => cluster.Members[0].ProjectPath, StringComparer.Ordinal)
            .ThenBy(static cluster => cluster.Members[0].SourcePath, StringComparer.Ordinal)
            .ThenBy(static cluster => cluster.Members[0].Line)
            .ThenBy(static cluster => cluster.Members[0].Identity, StringComparer.Ordinal)
            .ToArray();
    }

    internal sealed record DuplicateMethodFingerprint(
        string ProjectPath,
        string SourcePath,
        string Identity,
        int Line,
        int TokenCount,
        HashSet<ulong> Ngrams);

    private sealed record DuplicateMethodEdge(int A, int B, double Score);
    internal sealed record DuplicateCodeCluster(IReadOnlyList<DuplicateMethodFingerprint> Members, double Score);

    private sealed class DuplicateUnionFind(int size)
    {
        private readonly int[] parents = Enumerable.Range(0, size).ToArray();

        public int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }

            return index;
        }

        public void Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot != rightRoot)
            {
                parents[leftRoot] = rightRoot;
            }
        }
    }
}
