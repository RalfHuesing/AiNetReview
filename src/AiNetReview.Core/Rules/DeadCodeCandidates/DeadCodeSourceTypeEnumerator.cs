namespace AiNetReview.Core.Rules.DeadCodeCandidates;

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>Enumerates source types in a compilation assembly, including nested types.</summary>
internal static class DeadCodeSourceTypeEnumerator
{
    internal static IEnumerable<INamedTypeSymbol> FindSourceTypes(Compilation compilation) => SourceTypes(compilation.Assembly.GlobalNamespace);

    internal static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (var nested in SourceTypes(childNamespace)) yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in NestedTypes(type)) yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol parent)
    {
        foreach (var nested in parent.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in NestedTypes(nested)) yield return descendant;
        }
    }

}
