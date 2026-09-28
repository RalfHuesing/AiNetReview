namespace AiNetReview.Core.Rules.DeadCodeCandidates;

using Microsoft.CodeAnalysis;

/// <summary>Normalizes symbols to their original definitions for usage comparisons.</summary>
internal static class DeadCodeSymbolNormalizer
{
    public static ISymbol Normalize(ISymbol symbol) => symbol is IMethodSymbol { ReducedFrom: { } reduced }
        ? reduced.OriginalDefinition
        : symbol.OriginalDefinition;
}
