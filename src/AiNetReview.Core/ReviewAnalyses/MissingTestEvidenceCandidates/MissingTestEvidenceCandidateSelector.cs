namespace AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Selects nontrivial production functions for missing-test-evidence review.</summary>
internal static class MissingTestEvidenceCandidateSelector
{
    public static async Task<IReadOnlyList<MissingTestEvidenceFunctionCandidate>> SelectAsync(
        Solution solution,
        int minDecisionCount,
        int minDecisionNesting,
        int minIndirectDecisionCount,
        int minIndirectDecisionNesting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = new List<MissingTestEvidenceFunctionCandidate>();
        var projects = solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp && !ReviewSourceClassifier.IsTestProject(project))
            .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(static project => project.Name, StringComparer.Ordinal);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in project.Documents.OrderBy(static item => item.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(document.FilePath)
                    || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Semantic model could not be created for document '{document.Name}'.");

                foreach (var (declaration, body, method) in GetExecutableFunctions(root, semanticModel, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ReviewSourceClassifier.IsGeneratedSymbol(method))
                    {
                        continue;
                    }

                    var measurement = ControlFlowMetrics.Measure(body);
                    var meetsNontrivialGate = measurement.DecisionCount >= minDecisionCount
                        || measurement.MaxDecisionNesting >= minDecisionNesting;
                    if (!meetsNontrivialGate)
                    {
                        continue;
                    }

                    var meetsIndirectGate = measurement.DecisionCount >= minIndirectDecisionCount
                        || measurement.MaxDecisionNesting >= minIndirectDecisionNesting;
                    candidates.Add(new MissingTestEvidenceFunctionCandidate(
                        document,
                        declaration,
                        body,
                        method,
                        measurement,
                        meetsIndirectGate));
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<(SyntaxNode Declaration, SyntaxNode Body, IMethodSymbol Method)> GetExecutableFunctions(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return (declaration, body, method);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return (declaration, body, method);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.AccessorList is { } accessors)
            {
                foreach (var accessor in accessors.Accessors.Where(static accessor => accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                    || accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration)
                    || accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration)))
                {
                    if (GetBody(accessor) is { } body
                        && semanticModel.GetDeclaredSymbol(accessor, cancellationToken) is IMethodSymbol method)
                    {
                        yield return (accessor, body, method);
                    }
                }
            }
            else if (declaration.ExpressionBody is { } expressionBody
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IPropertySymbol { GetMethod: { } method })
            {
                yield return (declaration, expressionBody.Expression, method);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<IndexerDeclarationSyntax>())
        {
            if (declaration.AccessorList is { } accessorList)
            {
                foreach (var accessor in accessorList.Accessors.Where(static accessor => accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                    || accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration)
                    || accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration)))
                {
                    if (GetBody(accessor) is { } body
                        && semanticModel.GetDeclaredSymbol(accessor, cancellationToken) is IMethodSymbol method)
                    {
                        yield return (accessor, body, method);
                    }
                }
            }
            else if (declaration.ExpressionBody is { } expressionBody
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IPropertySymbol { GetMethod: { } method })
            {
                yield return (declaration, expressionBody.Expression, method);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<EventDeclarationSyntax>())
        {
            if (declaration.AccessorList is not { } accessorList)
            {
                continue;
            }

            foreach (var accessor in accessorList.Accessors.Where(static accessor => accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAccessorDeclaration)
                || accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.RemoveAccessorDeclaration)))
            {
                if (GetBody(accessor) is { } body
                    && semanticModel.GetDeclaredSymbol(accessor, cancellationToken) is IMethodSymbol method)
                {
                    yield return (accessor, body, method);
                }
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<OperatorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return (declaration, body, method);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return (declaration, body, method);
            }
        }
    }

    private static SyntaxNode? GetBody(BaseMethodDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;

    private static SyntaxNode? GetBody(AccessorDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;

    private static SyntaxNode? GetBody(MethodDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;
}

internal sealed record MissingTestEvidenceFunctionCandidate(
    Document Document,
    SyntaxNode Declaration,
    SyntaxNode Body,
    IMethodSymbol Method,
    ControlFlowMeasurement Measurement,
    bool MeetsIndirectThresholds);
