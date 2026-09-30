namespace AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

/// <summary>Builds the semantic, source-only graph used to attribute possible test paths.</summary>
internal static class MissingTestEvidenceSemanticGraphBuilder
{
    public static async Task<MissingTestEvidenceSemanticGraph> BuildAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        var nodes = new Dictionary<IMethodSymbol, MissingTestEvidenceGraphNode>(SymbolEqualityComparer.Default);
        var functions = new List<GraphFunction>();
        var testProjects = new HashSet<ProjectId>();
        var projectCount = 0;

        foreach (var project in solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectCount++;
            if (!project.SupportsCompilation)
            {
                throw new AnalysisFailedException($"Semantic test paths could not be enumerated for project '{project.Name}'.");
            }

            Compilation compilation;
            Document[] generatedDocuments;
            try
            {
                compilation = RequireCompilation(
                    await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false),
                    project.Name);
                generatedDocuments = (await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false))
                    .Cast<Document>()
                    .ToArray();
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
                throw new AnalysisFailedException($"Semantic test paths could not be enumerated for project '{project.Name}'.", exception);
            }

            var isTestProject = ReviewSourceClassifier.IsTestProject(project);
            if (isTestProject)
            {
                testProjects.Add(project.Id);
            }

            var generatedIds = generatedDocuments.Select(static document => document.Id).ToHashSet();
            foreach (var document in project.Documents.Concat(generatedDocuments)
                         .OrderBy(static document => document.FilePath ?? document.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!document.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    && !(document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                SyntaxNode root;
                SemanticModel semanticModel;
                try
                {
                    root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                    semanticModel = RequireSemanticModel(
                        await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                            ?? compilation.GetSemanticModel(root.SyntaxTree, ignoreAccessibility: true),
                        document.Name);
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
                    throw new AnalysisFailedException($"Semantic test paths could not be read for document '{document.Name}'.", exception);
                }

                var generatedDocument = generatedIds.Contains(document.Id)
                    || await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                foreach (var function in GetExecutableFunctions(root, semanticModel, cancellationToken))
                {
                    var methodKey = Normalize(function.Method);
                    var generated = generatedDocument || ReviewSourceClassifier.IsGeneratedSymbol(function.Method);
                    var node = new MissingTestEvidenceGraphNode(
                        function.Method,
                        project.Id,
                        project.Name,
                        isTestProject,
                        generated,
                        document.Id,
                        document.FilePath,
                        function.Declaration.Span);
                    if (!nodes.ContainsKey(methodKey))
                    {
                        nodes.Add(methodKey, node);
                        functions.Add(function with { Node = node });
                    }
                }
            }
        }

        if (projectCount == 0)
        {
            throw new AnalysisFailedException("Solution has no C# projects for semantic test path analysis.");
        }

        var collector = new GraphCollector(nodes, cancellationToken);
        foreach (var function in functions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            collector.Collect(function);
        }

        var roots = nodes.Values
            .Where(node => testProjects.Contains(node.ProjectId) && !node.IsGenerated)
            .Where(node => TestFrameworkClassifier.IsActiveTestRoot(
                solution.GetProject(node.ProjectId)!,
                node.Method))
            .OrderBy(static node => node.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static node => node.DeclarationSpan.Start)
            .ThenBy(static node => GetStableSymbolId(node.Method), StringComparer.Ordinal)
            .ToArray();

        return new MissingTestEvidenceSemanticGraph(
            nodes.Values.OrderBy(static node => GetStableSymbolId(node.Method), StringComparer.Ordinal).ToArray(),
            collector.Edges
                .OrderBy(static edge => GetStableSymbolId(edge.From), StringComparer.Ordinal)
                .ThenBy(static edge => GetStableSymbolId(edge.To), StringComparer.Ordinal)
                .ThenBy(static edge => edge.SourceFilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static edge => edge.SourceSpan.Start)
                .ThenBy(static edge => edge.Kind)
                .ToArray(),
            collector.UncertaintyInputs
                .OrderBy(static input => GetStableSymbolId(input.Source), StringComparer.Ordinal)
                .ThenBy(static input => input.SourceFilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static input => input.SourceSpan.Start)
                .ThenBy(static input => input.Kind)
                .ToArray(),
            roots);
    }

    internal static Compilation RequireCompilation(Compilation? compilation, string projectName) =>
        compilation ?? throw new AnalysisFailedException($"Compilation could not be created for project '{projectName}'.");

    internal static SemanticModel RequireSemanticModel(SemanticModel? semanticModel, string documentName) =>
        semanticModel ?? throw new AnalysisFailedException($"Semantic model could not be created for document '{documentName}'.");

    private static IEnumerable<GraphFunction> GetExecutableFunctions(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method
                && method.MethodKind != MethodKind.LocalFunction)
            {
                yield return new GraphFunction(declaration, body, method, semanticModel, null!);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return new GraphFunction(declaration, body, method, semanticModel, null!);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return new GraphFunction(declaration, body, method, semanticModel, null!);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.ExpressionBody is { } expressionBody
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IPropertySymbol { GetMethod: { } method })
            {
                yield return new GraphFunction(declaration, expressionBody.Expression, method, semanticModel, null!);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<IndexerDeclarationSyntax>())
        {
            if (declaration.ExpressionBody is { } expressionBody
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IPropertySymbol { GetMethod: { } method })
            {
                yield return new GraphFunction(declaration, expressionBody.Expression, method, semanticModel, null!);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<OperatorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return new GraphFunction(declaration, body, method, semanticModel, null!);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>())
        {
            if (GetBody(declaration) is { } body
                && semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
            {
                yield return new GraphFunction(declaration, body, method, semanticModel, null!);
            }
        }
    }

    private static SyntaxNode? GetBody(BaseMethodDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;

    private static SyntaxNode? GetBody(AccessorDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;

    private static SyntaxNode? GetBody(MethodDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;

    private static IOperation? GetBodyOperation(
        SemanticModel semanticModel,
        SyntaxNode body,
        CancellationToken cancellationToken)
    {
        // Roslyn does not expose operations for these semantically transparent expression wrappers.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (body)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    body = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    body = suppression.Operand;
                    break;
                default:
                    return semanticModel.GetOperation(body, cancellationToken);
            }
        }
    }

    private static IMethodSymbol Normalize(IMethodSymbol method)
    {
        if (method.ReducedFrom is { } reduced)
        {
            method = reduced;
        }

        if (method.PartialDefinitionPart is { } definition)
        {
            method = definition;
        }

        return method.OriginalDefinition;
    }

    private static string GetStableSymbolId(IMethodSymbol method) =>
        DocumentationCommentId.CreateDeclarationId(Normalize(method))
        ?? Normalize(method).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private sealed record GraphFunction(
        SyntaxNode Declaration,
        SyntaxNode Body,
        IMethodSymbol Method,
        SemanticModel SemanticModel,
        MissingTestEvidenceGraphNode? Node);

    private sealed class GraphCollector(
        IReadOnlyDictionary<IMethodSymbol, MissingTestEvidenceGraphNode> nodes,
        CancellationToken cancellationToken)
    {
        private readonly HashSet<MissingTestEvidenceGraphEdge> edges = [];
        private readonly HashSet<MissingTestEvidenceUncertaintyInput> uncertaintyInputs = [];

        public IReadOnlyCollection<MissingTestEvidenceGraphEdge> Edges => edges;

        public IReadOnlyCollection<MissingTestEvidenceUncertaintyInput> UncertaintyInputs => uncertaintyInputs;

        public void Collect(GraphFunction function)
        {
            var source = function.Node!;
            var operation = GetBodyOperation(function.SemanticModel, function.Body, cancellationToken);
            if (operation is null)
            {
                throw new AnalysisFailedException($"Semantic operation could not be created for '{source.Method.ToDisplayString()}'.");
            }

            new EdgeOperationWalker(this, function.SemanticModel, source, cancellationToken).Visit(operation);

            if (function.Declaration is ConstructorDeclarationSyntax { Initializer: { } initializer })
            {
                var initializerOperation = function.SemanticModel.GetOperation(initializer, cancellationToken);
                if (initializerOperation is null)
                {
                    throw new AnalysisFailedException($"Constructor initializer could not be resolved for '{source.Method.ToDisplayString()}'.");
                }

                new EdgeOperationWalker(this, function.SemanticModel, source, cancellationToken).Visit(initializerOperation);
            }
        }

        private void AddEdge(
            MissingTestEvidenceGraphNode source,
            IMethodSymbol? target,
            MissingTestEvidenceGraphEdgeKind kind,
            SyntaxNode syntax)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target is null || !nodes.TryGetValue(Normalize(target), out var targetNode))
            {
                return;
            }

            edges.Add(new MissingTestEvidenceGraphEdge(
                source.Method,
                targetNode.Method,
                kind,
                source.ProjectId,
                syntax.SyntaxTree.FilePath,
                syntax.Span));
        }

        private void AddUncertainty(
            MissingTestEvidenceGraphNode source,
            MissingTestEvidenceUncertaintyKind kind,
            SyntaxNode syntax,
            IMethodSymbol? affectedMethod = null,
            bool isGlobal = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uncertaintyInputs.Add(new MissingTestEvidenceUncertaintyInput(
                source.Method,
                kind,
                affectedMethod is null ? null : Normalize(affectedMethod),
                isGlobal,
                source.ProjectId,
                syntax.SyntaxTree.FilePath,
                syntax.Span));
        }

        private static bool IsPotentialDispatch(IMethodSymbol method) =>
            !method.IsSealed
            && (method.IsVirtual || method.IsAbstract || method.IsOverride || method.ContainingType?.TypeKind == TypeKind.Interface);

        private sealed class EdgeOperationWalker(
            GraphCollector owner,
            SemanticModel semanticModel,
            MissingTestEvidenceGraphNode source,
            CancellationToken cancellationToken) : OperationWalker
        {
            public override void VisitInvocation(IInvocationOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCall(operation.TargetMethod, MissingTestEvidenceGraphEdgeKind.Invocation, operation.Syntax);
                base.VisitInvocation(operation);
            }

            public override void VisitObjectCreation(IObjectCreationOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCall(operation.Constructor, MissingTestEvidenceGraphEdgeKind.ObjectCreation, operation.Syntax);
                base.VisitObjectCreation(operation);
            }

            public override void VisitPropertyReference(IPropertyReferenceOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (read, write) = GetPropertyAccess(operation);
                if (read)
                {
                    AddCall(operation.Property.GetMethod, MissingTestEvidenceGraphEdgeKind.PropertyGet, operation.Syntax);
                }

                if (write)
                {
                    AddCall(operation.Property.SetMethod, MissingTestEvidenceGraphEdgeKind.PropertySet, operation.Syntax);
                }

                base.VisitPropertyReference(operation);
            }

            public override void VisitEventAssignment(IEventAssignmentOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var eventSymbol = (operation.EventReference as IEventReferenceOperation)?.Event;
                var accessor = operation.Adds ? eventSymbol?.AddMethod : eventSymbol?.RemoveMethod;
                AddCall(
                    accessor,
                    operation.Adds ? MissingTestEvidenceGraphEdgeKind.EventAdd : MissingTestEvidenceGraphEdgeKind.EventRemove,
                    operation.Syntax);
                base.VisitEventAssignment(operation);
            }

            public override void VisitMethodReference(IMethodReferenceOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.Syntax.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().Any(
                    static invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }))
                {
                    base.VisitMethodReference(operation);
                    return;
                }

                owner.AddUncertainty(
                    source,
                    MissingTestEvidenceUncertaintyKind.MethodGroup,
                    operation.Syntax,
                    operation.Method);
                base.VisitMethodReference(operation);
            }

            public override void VisitInvalid(IInvalidOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsUnresolvedCallSyntax(operation.Syntax))
                {
                    var info = semanticModel.GetSymbolInfo(operation.Syntax, cancellationToken);
                    var candidateMethods = info.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
                    if (candidateMethods.Length == 0)
                    {
                        owner.AddUncertainty(source, MissingTestEvidenceUncertaintyKind.UnresolvedBinding, operation.Syntax, isGlobal: true);
                    }
                    else
                    {
                        foreach (var method in candidateMethods)
                        {
                            owner.AddUncertainty(source, MissingTestEvidenceUncertaintyKind.UnresolvedBinding, operation.Syntax, method);
                        }
                    }
                }

                base.VisitInvalid(operation);
            }

            public override void VisitUnaryOperator(IUnaryOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddOperator(operation.OperatorMethod, operation.Syntax);
                base.VisitUnaryOperator(operation);
            }

            public override void VisitBinaryOperator(IBinaryOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddOperator(operation.OperatorMethod, operation.Syntax);
                base.VisitBinaryOperator(operation);
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddOperator(operation.OperatorMethod, operation.Syntax);
                base.VisitCompoundAssignment(operation);
            }

            public override void VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddOperator(operation.OperatorMethod, operation.Syntax);
                base.VisitIncrementOrDecrement(operation);
            }

            public override void VisitConversion(IConversionOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var conversion = operation.OperatorMethod;
                if (conversion is not null)
                {
                    AddCall(conversion, MissingTestEvidenceGraphEdgeKind.UserConversion, operation.Syntax);
                }

                base.VisitConversion(operation);
            }

            private void AddCall(IMethodSymbol? method, MissingTestEvidenceGraphEdgeKind kind, SyntaxNode syntax)
            {
                if (method is null)
                {
                    return;
                }

                owner.AddEdge(source, method, kind, syntax);
                if (IsPotentialDispatch(method))
                {
                    owner.AddUncertainty(
                        source,
                        MissingTestEvidenceUncertaintyKind.VirtualOrInterfaceDispatch,
                        syntax,
                        method,
                        isGlobal: true);
                }
            }

            private void AddOperator(IMethodSymbol? method, SyntaxNode syntax)
            {
                if (method is not null)
                {
                    owner.AddEdge(source, method, MissingTestEvidenceGraphEdgeKind.UserOperator, syntax);
                }
            }

            private static bool IsUnresolvedCallSyntax(SyntaxNode syntax) =>
                syntax is InvocationExpressionSyntax
                    or ObjectCreationExpressionSyntax
                    or ImplicitObjectCreationExpressionSyntax
                    or ElementAccessExpressionSyntax
                    or MemberAccessExpressionSyntax
                    or BinaryExpressionSyntax
                    or PrefixUnaryExpressionSyntax
                    or PostfixUnaryExpressionSyntax
                    or AssignmentExpressionSyntax
                    or CastExpressionSyntax;

            private static (bool Read, bool Write) GetPropertyAccess(IPropertyReferenceOperation operation)
            {
                var parent = operation.Parent;
                if (parent is ISimpleAssignmentOperation simple && ReferenceEquals(simple.Target, operation))
                {
                    return (false, true);
                }

                if (parent is ICompoundAssignmentOperation compound && ReferenceEquals(compound.Target, operation)
                    || parent is IIncrementOrDecrementOperation increment && ReferenceEquals(increment.Target, operation))
                {
                    return (true, true);
                }

                if (parent is IArgumentOperation argument
                    && ReferenceEquals(argument.Value, operation)
                    && argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref)
                {
                    return argument.Parameter.RefKind == RefKind.Out ? (false, true) : (true, true);
                }

                return (true, false);
            }
        }
    }
}

internal sealed record MissingTestEvidenceSemanticGraph(
    IReadOnlyList<MissingTestEvidenceGraphNode> Nodes,
    IReadOnlyList<MissingTestEvidenceGraphEdge> Edges,
    IReadOnlyList<MissingTestEvidenceUncertaintyInput> UncertaintyInputs,
    IReadOnlyList<MissingTestEvidenceGraphNode> Roots);

internal sealed record MissingTestEvidenceGraphNode(
    IMethodSymbol Method,
    ProjectId ProjectId,
    string ProjectName,
    bool IsTestProject,
    bool IsGenerated,
    DocumentId DocumentId,
    string? FilePath,
    TextSpan DeclarationSpan);

internal sealed record MissingTestEvidenceGraphEdge(
    IMethodSymbol From,
    IMethodSymbol To,
    MissingTestEvidenceGraphEdgeKind Kind,
    ProjectId ProjectId,
    string? SourceFilePath,
    TextSpan SourceSpan);

internal enum MissingTestEvidenceGraphEdgeKind
{
    Invocation,
    ObjectCreation,
    PropertyGet,
    PropertySet,
    EventAdd,
    EventRemove,
    UserOperator,
    UserConversion,
}

internal sealed record MissingTestEvidenceUncertaintyInput(
    IMethodSymbol Source,
    MissingTestEvidenceUncertaintyKind Kind,
    IMethodSymbol? AffectedMethod,
    bool IsGlobal,
    ProjectId ProjectId,
    string? SourceFilePath,
    TextSpan SourceSpan);

internal enum MissingTestEvidenceUncertaintyKind
{
    MethodGroup,
    UnresolvedBinding,
    VirtualOrInterfaceDispatch,
}
