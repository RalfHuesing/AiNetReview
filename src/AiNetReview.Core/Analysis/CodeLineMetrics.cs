using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetReview.Core.Analysis;

/// <summary>Counts physical source lines that contain C# tokens.</summary>
public static class CodeLineMetrics
{
    /// <summary>Counts distinct token start lines below <paramref name="node"/>.</summary>
    public static int CountTokenStartLines(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return CountTokenStartLines(node.DescendantTokens(descendIntoTrivia: false));
    }

    /// <summary>Counts executable lines in a supported declaration, including its signature and attributes.</summary>
    public static int CountExecutableDeclaration(SyntaxNode declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration switch
        {
            MethodDeclarationSyntax method => HasBody(method.Body, method.ExpressionBody)
                ? CountTokenStartLines(method)
                : 0,
            ConstructorDeclarationSyntax constructor => HasBody(constructor.Body, constructor.ExpressionBody)
                ? CountTokenStartLines(constructor)
                : 0,
            AccessorDeclarationSyntax accessor => HasBody(accessor.Body, accessor.ExpressionBody)
                ? CountTokenStartLines(accessor)
                : 0,
            OperatorDeclarationSyntax op => HasBody(op.Body, op.ExpressionBody)
                ? CountTokenStartLines(op)
                : 0,
            ConversionOperatorDeclarationSyntax conversion => HasBody(conversion.Body, conversion.ExpressionBody)
                ? CountTokenStartLines(conversion)
                : 0,
            PropertyDeclarationSyntax property => CountProperty(property),
            IndexerDeclarationSyntax indexer => CountIndexer(indexer),
            _ => throw new ArgumentException("The syntax node is not a supported executable declaration.", nameof(declaration))
        };
    }

    /// <summary>Counts lines containing tokens owned by this type part, excluding nested types and delegates.</summary>
    public static int CountOwnTypePart(BaseTypeDeclarationSyntax declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return CountTokenStartLines(
            declaration.DescendantTokens(descendIntoTrivia: false)
                .Where(token => BelongsToTypePart(token, declaration)));
    }

    private static int CountProperty(PropertyDeclarationSyntax property)
    {
        return property.ExpressionBody is null ? 0 : CountTokenStartLines(property);
    }

    private static int CountIndexer(IndexerDeclarationSyntax indexer)
    {
        return indexer.ExpressionBody is null ? 0 : CountTokenStartLines(indexer);
    }

    private static bool HasBody(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody) =>
        body is not null || expressionBody is not null;

    private static bool BelongsToTypePart(SyntaxToken token, BaseTypeDeclarationSyntax declaration)
    {
        for (var ancestor = token.Parent; ancestor is not null && !ReferenceEquals(ancestor, declaration); ancestor = ancestor.Parent)
        {
            if (ancestor is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            {
                return false;
            }
        }

        return true;
    }

    private static int CountTokenStartLines(IEnumerable<SyntaxToken> tokens)
    {
        var lines = new HashSet<int>();
        foreach (var token in tokens)
        {
            if (!token.IsMissing && token.Span.Length > 0 && !token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EndOfFileToken))
            {
                lines.Add(token.GetLocation().GetLineSpan().StartLinePosition.Line);
            }
        }

        return lines.Count;
    }
}
