namespace AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

/// <summary>Classifies source declarations and statically bound transparent forwarding edges for one project.</summary>
internal static class TransparentForwardingClassifier
{
    public static async Task<IReadOnlyList<ForwardingDeclaration>> ClassifyProjectAsync(
        ReviewContext context,
        Project project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        if (project.Language != LanguageNames.CSharp || ReviewSourceClassifier.IsTestProject(project))
        {
            return Array.Empty<ForwardingDeclaration>();
        }

        if (!project.SupportsCompilation)
        {
            throw new AnalysisFailedException($"Semantic forwarding edges could not be classified for project '{project.Name}'.");
        }

        Compilation compilation;
        try
        {
            compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AnalysisFailedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.", exception);
        }

        var declarations = new Dictionary<IMethodSymbol, List<SourceMethodDeclaration>>(SymbolEqualityComparer.Default);
        foreach (var document in project.Documents.OrderBy(static item => item.FilePath ?? item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetExtension(document.FilePath ?? document.Name), ".cs", StringComparison.OrdinalIgnoreCase)
                || await IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            SyntaxNode root;
            SemanticModel semanticModel;
            try
            {
                root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Semantic model could not be created for document '{document.Name}'.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AnalysisFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AnalysisFailedException($"Syntax or semantic information could not be read for document '{document.Name}'.", exception);
            }

            var sourcePath = GetSourcePath(context, document, project);
            foreach (var syntax in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticModel.GetDeclaredSymbol(syntax, cancellationToken) is not IMethodSymbol method
                    || method.MethodKind != MethodKind.Ordinary
                    || method.IsImplicitlyDeclared
                    || ReviewSourceClassifier.IsGeneratedSymbol(method))
                {
                    continue;
                }

                var definition = method.OriginalDefinition;
                if (!declarations.TryGetValue(definition, out var references))
                {
                    references = [];
                    declarations.Add(definition, references);
                }

                references.Add(new SourceMethodDeclaration(method, syntax, semanticModel, document, sourcePath));
            }
        }

        var selectedDeclarations = new Dictionary<IMethodSymbol, SourceMethodDeclaration>(SymbolEqualityComparer.Default);
        foreach (var pair in declarations)
        {
            selectedDeclarations.Add(pair.Key, SelectDeclaration(pair.Value));
        }
        var result = new List<ForwardingDeclaration>(selectedDeclarations.Count);
        foreach (var pair in selectedDeclarations.OrderBy(static pair => pair.Value.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Value.Syntax.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = pair.Value;
            var symbol = source.Symbol.OriginalDefinition;
            var docId = DocumentationCommentId.CreateDeclarationId(symbol);
            if (string.IsNullOrWhiteSpace(docId))
            {
                continue;
            }

            var forwardingTarget = TryClassifyForwarder(source, project, selectedDeclarations, cancellationToken);
            var line = source.Syntax.SyntaxTree.GetLineSpan(source.Syntax.Span).StartLinePosition.Line + 1;
            result.Add(new ForwardingDeclaration(
                project.Id,
                project.Name,
                symbol,
                source.Document,
                source.SourcePath,
                line,
                docId,
                forwardingTarget));
        }

        return result;
    }

    private static string GetSourcePath(ReviewContext context, Document document, Project project)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath) || string.IsNullOrWhiteSpace(project.FilePath))
        {
            throw new AnalysisFailedException("A source method declaration has no document or project path.");
        }

        return context.GetProjectRelativePath(document.FilePath);
    }

    private static async Task<bool> IsGeneratedDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        try
        {
            return await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AnalysisFailedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AnalysisFailedException($"Source information could not be read for document '{document.Name}'.", exception);
        }
    }

    private static SourceMethodDeclaration SelectDeclaration(IReadOnlyList<SourceMethodDeclaration> declarations) =>
        declarations.OrderByDescending(static declaration => declaration.Syntax.Body is not null || declaration.Syntax.ExpressionBody is not null)
            .ThenBy(static declaration => declaration.SourcePath, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.Syntax.SpanStart)
            .First();

    private static ForwardingTarget? TryClassifyForwarder(
        SourceMethodDeclaration source,
        Project project,
        IReadOnlyDictionary<IMethodSymbol, SourceMethodDeclaration> declarations,
        CancellationToken cancellationToken)
    {
        var method = source.Symbol;
        if (method.IsAsync || method.IsAbstract || method.IsExtern)
        {
            return null;
        }

        var invocation = GetOnlyInvocation(source.Syntax, method.ReturnsVoid);
        if (invocation is null)
        {
            return null;
        }

        var semanticModel = source.SemanticModel;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return null;
        }

        var target = operation.TargetMethod;
        if (target.MethodKind != MethodKind.Ordinary
            || target.IsExtensionMethod
            || target.ReducedFrom is not null
            || target.ContainingType is null
            || SymbolEqualityComparer.Default.Equals(target.ContainingType, method.ContainingType)
            || !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, method.ContainingAssembly))
        {
            return null;
        }

        var targetDefinition = target.OriginalDefinition;
        if (!declarations.TryGetValue(targetDefinition, out var targetDeclaration)
            || targetDeclaration.Document.Project.Id != project.Id
            || !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(targetDeclaration.Document.FilePath ?? targetDeclaration.Document.Name), ".cs")
            || ReviewSourceClassifier.IsGeneratedSymbol(target))
        {
            return null;
        }

        if (!HasAllowedReceiver(invocation, operation, method, semanticModel, cancellationToken)
            || !HasIdentityReturn(method, target, invocation, semanticModel)
            || !HasForwardedArguments(invocation, operation, method, target, semanticModel))
        {
            return null;
        }

        var targetDocId = DocumentationCommentId.CreateDeclarationId(targetDefinition);
        if (string.IsNullOrWhiteSpace(targetDocId))
        {
            return null;
        }

        return new ForwardingTarget(targetDefinition, targetDocId, targetDeclaration.SourcePath, targetDeclaration.Syntax);
    }

    private static InvocationExpressionSyntax? GetOnlyInvocation(MethodDeclarationSyntax method, bool returnsVoid)
    {
        if (method.ExpressionBody is not null)
        {
            return UnwrapParentheses(method.ExpressionBody.Expression) as InvocationExpressionSyntax;
        }

        if (method.Body is not { Statements.Count: 1 } body)
        {
            return null;
        }

        return body.Statements[0] switch
        {
            ReturnStatementSyntax { Expression: { } expression } when !returnsVoid => UnwrapParentheses(expression) as InvocationExpressionSyntax,
            ExpressionStatementSyntax expressionStatement when returnsVoid => UnwrapParentheses(expressionStatement.Expression) as InvocationExpressionSyntax,
            _ => null,
        };
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasIdentityReturn(
        IMethodSymbol wrapper,
        IMethodSymbol target,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (wrapper.ReturnsVoid)
        {
            return target.ReturnsVoid;
        }

        if (target.ReturnsVoid
            || !SymbolEqualityComparer.IncludeNullability.Equals(wrapper.ReturnType, target.ReturnType))
        {
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(invocation);
        if (typeInfo.Type is null || typeInfo.ConvertedType is null
            || !SymbolEqualityComparer.IncludeNullability.Equals(typeInfo.Type, wrapper.ReturnType)
            || !SymbolEqualityComparer.IncludeNullability.Equals(typeInfo.ConvertedType, wrapper.ReturnType))
        {
            return false;
        }

        return semanticModel.ClassifyConversion(invocation, wrapper.ReturnType).IsIdentity;
    }

    private static bool HasForwardedArguments(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        IMethodSymbol wrapper,
        IMethodSymbol target,
        SemanticModel semanticModel)
    {
        if (wrapper.Parameters.Length != target.Parameters.Length
            || invocation.ArgumentList.Arguments.Count != wrapper.Parameters.Length
            || operation.Arguments.Length != wrapper.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < wrapper.Parameters.Length; index++)
        {
            var wrapperParameter = wrapper.Parameters[index];
            var targetParameter = target.Parameters[index];
            var syntaxArgument = invocation.ArgumentList.Arguments[index];
            var argument = operation.Arguments[index];
            if (wrapperParameter.RefKind != RefKind.None
                || targetParameter.RefKind != RefKind.None
                || !syntaxArgument.RefKindKeyword.IsKind(SyntaxKind.None)
                || syntaxArgument.NameColon is not null && argument.Parameter?.Ordinal != index
                || argument.Parameter?.Ordinal != index
                || argument.Value.Syntax is not IdentifierNameSyntax identifier
                || semanticModel.GetSymbolInfo(identifier).Symbol is not IParameterSymbol passedParameter
                || !SymbolEqualityComparer.Default.Equals(passedParameter, wrapperParameter)
                || !argument.InConversion.IsIdentity
                || !semanticModel.ClassifyConversion(identifier, targetParameter.Type).IsIdentity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAllowedReceiver(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        IMethodSymbol wrapper,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.IsKind(SyntaxKind.PointerMemberAccessExpression)
            || operation.Instance is null && !operation.TargetMethod.IsStatic)
        {
            return false;
        }

        var receiver = memberAccess.Expression;
        if (receiver is ThisExpressionSyntax)
        {
            return true;
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol;
        if (receiverSymbol is IParameterSymbol parameter
            && wrapper.Parameters.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)))
        {
            return true;
        }

        if (receiverSymbol is IFieldSymbol field
            && !field.IsStatic
            && (receiver is IdentifierNameSyntax
                || receiver is MemberAccessExpressionSyntax fieldAccess && fieldAccess.Expression is ThisExpressionSyntax))
        {
            return true;
        }

        return (receiverSymbol is INamedTypeSymbol
                || receiverSymbol is IAliasSymbol { Target: INamedTypeSymbol })
            && operation.TargetMethod.IsStatic;
    }

    private sealed record SourceMethodDeclaration(
        IMethodSymbol Symbol,
        MethodDeclarationSyntax Syntax,
        SemanticModel SemanticModel,
        Document Document,
        string SourcePath);
}

internal sealed record ForwardingDeclaration(
    ProjectId ProjectId,
    string ProjectName,
    IMethodSymbol Symbol,
    Document Document,
    string SourcePath,
    int DeclarationLine,
    string DocId,
    ForwardingTarget? Target);

internal sealed record ForwardingTarget(
    IMethodSymbol Symbol,
    string DocId,
    string SourcePath,
    MethodDeclarationSyntax Declaration);
