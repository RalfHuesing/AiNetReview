namespace AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

/// <summary>Tracks protected and uncertain symbols for dead-code candidate analysis.</summary>
internal sealed class DeadCodeSymbolTracker
{
    private readonly HashSet<ISymbol> protectedSymbols = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> uncertainSymbols = new(SymbolEqualityComparer.Default);

    public IReadOnlyCollection<ISymbol> ProtectedSymbols => protectedSymbols;

    public IReadOnlyCollection<ISymbol> UncertainSymbols => uncertainSymbols;

    public bool IsProtected(ISymbol symbol) => protectedSymbols.Contains(DeadCodeSymbolNormalizer.Normalize(symbol));

    public bool HasUncertainty(ISymbol symbol) => uncertainSymbols.Contains(DeadCodeSymbolNormalizer.Normalize(symbol));

    public void Protect(ISymbol symbol)
    {
        symbol = DeadCodeSymbolNormalizer.Normalize(symbol);
        protectedSymbols.Add(symbol);
        if (symbol.ContainingType is { } containingType)
        {
            protectedSymbols.Add(DeadCodeSymbolNormalizer.Normalize(containingType));
        }
    }

    public void MarkUncertain(ISymbol symbol) => uncertainSymbols.Add(DeadCodeSymbolNormalizer.Normalize(symbol));

    public void UnionWith(DeadCodeMarkupUsage markupUsage)
    {
        protectedSymbols.UnionWith(markupUsage.ProtectedSymbols);
        uncertainSymbols.UnionWith(markupUsage.UncertainSymbols);
    }
}
