namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

/// <summary>Builds the current solution's direct, statically bound source-type dependencies.</summary>
internal static class TypeDependencyGraphBuilder
{
    public static async Task<TypeDependencyGraph> BuildAsync(ReviewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var projects = context.Solution.Projects.Where(static project => project.Language == LanguageNames.CSharp).ToArray();
        if (projects.Length == 0)
        {
            throw new AnalysisFailedException("Type dependencies could not be enumerated because the solution has no C# projects.");
        }

        var sourceTreeProjects = new Dictionary<SyntaxTree, HashSet<ProjectId>>();
        var declarations = new List<TypeDeclaration>();
        var excludedGeneratedTypes = new HashSet<TypeIdentity>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!project.SupportsCompilation)
            {
                throw new AnalysisFailedException($"Type dependencies could not be enumerated for project '{project.Name}': compilation is unavailable.");
            }

            Compilation compilation;
            Document[] generatedDocuments;
            try
            {
                compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Type dependencies could not be enumerated for project '{project.Name}': compilation is unavailable.");
                generatedDocuments = (await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false)).Cast<Document>().ToArray();
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
                throw new AnalysisFailedException($"Type dependencies could not be enumerated for project '{project.Name}'.", exception);
            }

            var isTestProject = ReviewSourceClassifier.IsTestProject(project);
            var state = new ProjectState(project, isTestProject);
            var generatedIds = generatedDocuments.Select(static document => document.Id).ToHashSet();
            foreach (var document in project.Documents.Concat(generatedDocuments)
                         .Where(IsCSharpDocument)
                         .OrderBy(static document => document.FilePath ?? document.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyntaxNode root;
                SemanticModel semanticModel;
                bool isGenerated;
                try
                {
                    root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw DocumentFailure(project, document, 0, "syntax root is unavailable");
                    semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                        ?? compilation.GetSemanticModel(root.SyntaxTree, ignoreAccessibility: true);
                    if (semanticModel is null)
                    {
                        throw DocumentFailure(project, document, 0, "semantic model is unavailable");
                    }

                    isGenerated = generatedIds.Contains(document.Id)
                        || await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false);
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
                    throw new AnalysisFailedException($"Type dependencies could not be read in project '{project.Name}', source '{GetSourcePath(context, document, project)}': {exception.Message}", exception);
                }

                foreach (var syntax in EnumerateTypeDeclarations(root, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var symbol = semanticModel.GetDeclaredSymbol(syntax, cancellationToken) as INamedTypeSymbol;
                    if (symbol is null || symbol.TypeKind == TypeKind.Error)
                    {
                        throw DocumentFailure(project, document, syntax.SpanStart, "named type declaration could not be bound");
                    }

                    var identity = TypeIdentity.Create(project.Id, symbol);
                    if (isGenerated || ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                    {
                        excludedGeneratedTypes.Add(identity);
                        continue;
                    }

                    var sourcePath = GetSourcePath(context, document, project, syntax.SpanStart);
                    var declaration = new TypeDeclaration(
                        symbol,
                        state,
                        document,
                        syntax,
                        semanticModel,
                        sourcePath);
                    declarations.Add(declaration);
                }

                if (!sourceTreeProjects.TryGetValue(root.SyntaxTree, out var treeProjects))
                {
                    treeProjects = [];
                    sourceTreeProjects.Add(root.SyntaxTree, treeProjects);
                }
                treeProjects.Add(project.Id);
            }
        }

        var nodes = new Dictionary<TypeIdentity, TypeDependencyNode>();
        foreach (var declaration in declarations.OrderBy(DeclarationOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = TypeIdentity.Create(declaration.Project.Project.Id, declaration.Symbol);
            if (!nodes.TryGetValue(identity, out var node))
            {
                node = new TypeDependencyNode(declaration.Symbol, declaration.Project.Project.Id, declaration.Project.Project.Name,
                    declaration.Project.Project.FilePath ?? declaration.Project.Project.Name,
                    declaration.Project.IsTestProject, new List<TypeDependencyDeclarationLocation>());
                nodes.Add(identity, node);
            }

            node.AddDeclaration(new TypeDependencyDeclarationLocation(
                declaration.SourcePath,
                declaration.Syntax.Span,
                declaration.Project.Project.Id,
                declaration.Project.Project.Name));
        }

        var collector = new EdgeCollector(context, sourceTreeProjects, nodes, excludedGeneratedTypes, cancellationToken);
        foreach (var declaration in declarations.OrderBy(DeclarationOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceIdentity = TypeIdentity.Create(declaration.Project.Project.Id, declaration.Symbol);
            var source = nodes[sourceIdentity];
            collector.CollectTypeSyntax(declaration, source);
            collector.CollectOperations(declaration, source);
        }

        var edges = collector.ToEdges();
        foreach (var node in nodes.Values)
        {
            node.Freeze();
        }

        return new TypeDependencyGraph(
            Array.AsReadOnly(nodes.Values.OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray()),
            edges);
    }

    private static bool IsCSharpDocument(Document document) =>
        document.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || (document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ?? false);

    private static IEnumerable<SyntaxNode> EnumerateTypeDeclarations(SyntaxNode root, CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodesAndSelf()
                     .Where(static node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return node;
        }
    }

    private static string DeclarationOrder(TypeDeclaration declaration) =>
        $"{declaration.Project.Project.FilePath ?? declaration.Project.Project.Name}\n{declaration.SourcePath}\n{declaration.Syntax.SpanStart:D10}\n{TypeIdentity.Create(declaration.Project.Project.Id, declaration.Symbol).StableId}";

    private static string GetSourcePath(ReviewContext context, Document document, Project project, int position = 0)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            return $"{project.Name}/{document.Name}".Replace('\\', '/');
        }

        try
        {
            return context.GetProjectRelativePath(document.FilePath);
        }
        catch (AnalysisFailedException exception)
        {
            throw new AnalysisFailedException(
                $"Type dependency analysis failed in project '{project.Name}', source '{document.FilePath}', position {position}: {exception.Message}", exception);
        }
    }

    private static AnalysisFailedException DocumentFailure(Project project, Document document, int position, string reason) =>
        new($"Type dependency analysis failed in project '{project.Name}', source '{document.FilePath ?? document.Name}', position {position}: {reason}.");

    private sealed class ProjectState(Project project, bool isTestProject)
    {
        public Project Project { get; } = project;
        public bool IsTestProject { get; } = isTestProject;
    }

    private sealed record TypeDeclaration(
        INamedTypeSymbol Symbol,
        ProjectState Project,
        Document Document,
        SyntaxNode Syntax,
        SemanticModel SemanticModel,
        string SourcePath);

    private sealed class EdgeCollector(
        ReviewContext context,
        IReadOnlyDictionary<SyntaxTree, HashSet<ProjectId>> sourceTreeProjects,
        IReadOnlyDictionary<TypeIdentity, TypeDependencyNode> nodes,
        IReadOnlySet<TypeIdentity> excludedGeneratedTypes,
        CancellationToken cancellationToken)
    {
        private readonly Dictionary<(TypeIdentity From, TypeIdentity To), Dictionary<TypeDependencyEvidenceKind, TypeDependencyWitness>> edges = [];

        public void CollectTypeSyntax(TypeDeclaration declaration, TypeDependencyNode source)
        {
            foreach (var typeSyntax in declaration.Syntax.DescendantNodesAndSelf(
                         descendIntoChildren: child => ReferenceEquals(child, declaration.Syntax)
                             || child is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax)
                     .OfType<TypeSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsInGeneratedMember(typeSyntax, declaration, cancellationToken))
                {
                    continue;
                }

                if (IsExcludedSyntax(typeSyntax, declaration.Syntax))
                {
                    continue;
                }

                if (IsSpecialConstraintType(typeSyntax, declaration))
                {
                    continue;
                }

                if (typeSyntax is IdentifierNameSyntax { Identifier.ValueText: "var" })
                {
                    continue;
                }

                if (!IsSyntacticTypePosition(typeSyntax, declaration.Syntax))
                {
                    continue;
                }

                var typeInfo = declaration.SemanticModel.GetTypeInfo(typeSyntax, cancellationToken);
                var type = typeInfo.Type;
                if (type is null)
                {
                    var symbolInfo = declaration.SemanticModel.GetSymbolInfo(typeSyntax, cancellationToken);
                    if (symbolInfo.Symbol is not null && symbolInfo.Symbol is not ITypeSymbol)
                    {
                        // Qualified type syntax also contains namespace prefixes; member names can share TypeSyntax nodes.
                        continue;
                    }

                    throw DocumentFailure(declaration.Project.Project, declaration.Document, typeSyntax.SpanStart,
                        $"required explicit type use '{typeSyntax}' ({typeSyntax.Kind()}) could not be bound");
                }

                if (type.TypeKind == TypeKind.Error)
                {
                    throw DocumentFailure(declaration.Project.Project, declaration.Document, typeSyntax.SpanStart,
                        $"required explicit type use '{typeSyntax}' ({typeSyntax.Kind()}) could not be bound");
                }

                var kind = typeSyntax.Parent is SimpleBaseTypeSyntax simpleBase
                        && ReferenceEquals(simpleBase.Type, typeSyntax)
                    || typeSyntax.Parent is PrimaryConstructorBaseTypeSyntax primaryBase
                        && ReferenceEquals(primaryBase.Type, typeSyntax)
                    ? TypeDependencyEvidenceKind.Inheritance
                    : TypeDependencyEvidenceKind.ExplicitTypeUse;
                AddTypeReferences(source, type, declaration.Project, typeSyntax, kind);
            }
        }

        private bool IsSpecialConstraintType(TypeSyntax typeSyntax, TypeDeclaration declaration)
        {
            if (typeSyntax is not IdentifierNameSyntax identifier
                || identifier.Identifier.Text is not ("notnull" or "unmanaged")
                || typeSyntax.Parent is not TypeConstraintSyntax constraint
                || !ReferenceEquals(constraint.Type, typeSyntax)
                || constraint.Parent is not TypeParameterConstraintClauseSyntax clause)
            {
                return false;
            }

            ISymbol? owner = clause.Parent switch
            {
                TypeDeclarationSyntax typeDeclaration => declaration.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken),
                MethodDeclarationSyntax methodDeclaration => declaration.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken),
                LocalFunctionStatementSyntax localFunction => declaration.SemanticModel.GetDeclaredSymbol(localFunction, cancellationToken),
                DelegateDeclarationSyntax delegateDeclaration => declaration.SemanticModel.GetDeclaredSymbol(delegateDeclaration, cancellationToken),
                _ => null,
            };
            var typeParameter = owner switch
            {
                INamedTypeSymbol namedType => namedType.TypeParameters.FirstOrDefault(parameter => parameter.Name == clause.Name.Identifier.ValueText),
                IMethodSymbol method => method.TypeParameters.FirstOrDefault(parameter => parameter.Name == clause.Name.Identifier.ValueText),
                _ => null,
            };

            return typeParameter is not null
                && (identifier.Identifier.ValueText == "notnull" && typeParameter.HasNotNullConstraint
                    || identifier.Identifier.ValueText == "unmanaged" && typeParameter.HasUnmanagedTypeConstraint);
        }

        public void CollectOperations(TypeDeclaration declaration, TypeDependencyNode source)
        {
            foreach (var body in GetOperationRoots(declaration.Syntax))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsInGeneratedMember(body, declaration, cancellationToken))
                {
                    continue;
                }

                var operation = declaration.SemanticModel.GetOperation(body, cancellationToken);
                if (operation is null)
                {
                    // Some expression wrappers (for example nullable suppression) do not have their own operation.
                    var expression = body is ArrowExpressionClauseSyntax arrow ? arrow.Expression : body;
                    operation = declaration.SemanticModel.GetOperation(expression, cancellationToken);
                }

                if (operation is null)
                {
                    throw DocumentFailure(declaration.Project.Project, declaration.Document, body.SpanStart,
                        $"executable member '{body.Kind()} {body}' could not be semantically modeled");
                }

                new DependencyOperationWalker(this, source, declaration, cancellationToken).Visit(operation);
            }
        }

        private static bool IsInGeneratedMember(SyntaxNode syntax, TypeDeclaration declaration, CancellationToken cancellationToken)
        {
            for (var current = syntax; current is not null && !ReferenceEquals(current, declaration.Syntax); current = current.Parent)
            {
                if (current is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
                {
                    var variables = current switch
                    {
                        FieldDeclarationSyntax field => field.Declaration.Variables,
                        EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables,
                        _ => default,
                    };
                    foreach (var variable in variables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var variableSymbol = declaration.SemanticModel.GetDeclaredSymbol(variable, cancellationToken);
                        if (variableSymbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(variableSymbol))
                        {
                            return true;
                        }
                    }

                    continue;
                }

                if (current is VariableDeclaratorSyntax fieldVariable
                    && fieldVariable.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
                {
                    var variableSymbol = declaration.SemanticModel.GetDeclaredSymbol(fieldVariable, cancellationToken);
                    if (variableSymbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(variableSymbol))
                    {
                        return true;
                    }
                }
                else if (current is BaseMethodDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax
                    or EventDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax)
                {
                    var symbol = declaration.SemanticModel.GetDeclaredSymbol(current, cancellationToken);
                    if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public IReadOnlyList<TypeDependencyEdge> ToEdges() => Array.AsReadOnly(edges
            .OrderBy(pair => nodes[pair.Key.From].StableId, StringComparer.Ordinal)
            .ThenBy(pair => nodes[pair.Key.To].StableId, StringComparer.Ordinal)
            .Select(pair => new TypeDependencyEdge(
                nodes[pair.Key.From],
                nodes[pair.Key.To],
                Array.AsReadOnly(pair.Value.OrderBy(static witness => witness.Key)
                    .Select(static witness => witness.Value).ToArray())))
            .ToArray());

        private static IEnumerable<SyntaxNode> GetOperationRoots(SyntaxNode typeSyntax)
        {
            foreach (var node in typeSyntax.DescendantNodesAndSelf(
                         descendIntoChildren: child => ReferenceEquals(child, typeSyntax)
                             || child is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax))
            {
                switch (node)
                {
                    case ConstructorDeclarationSyntax constructor:
                        if (constructor.Body is not null)
                        {
                            yield return constructor.Body;
                        }
                        if (constructor.ExpressionBody is not null)
                        {
                            yield return constructor.ExpressionBody;
                        }
                        if (constructor.Initializer is not null)
                        {
                            yield return constructor.Initializer;
                        }
                        break;
                    case BaseMethodDeclarationSyntax method when method.Body is not null:
                        yield return method.Body;
                        break;
                    case BaseMethodDeclarationSyntax method when method.ExpressionBody is not null:
                        yield return method.ExpressionBody;
                        break;
                    case AccessorDeclarationSyntax accessor when accessor.Body is not null:
                        yield return accessor.Body;
                        break;
                    case AccessorDeclarationSyntax accessor when accessor.ExpressionBody is not null:
                        yield return accessor.ExpressionBody;
                        break;
                    case PropertyDeclarationSyntax property when property.Initializer is not null:
                        yield return property.Initializer;
                        break;
                    case PropertyDeclarationSyntax property when property.ExpressionBody is not null:
                        yield return property.ExpressionBody;
                        break;
                    case IndexerDeclarationSyntax indexer when indexer.ExpressionBody is not null:
                        yield return indexer.ExpressionBody;
                        break;
                    case PrimaryConstructorBaseTypeSyntax primaryBase:
                        yield return primaryBase;
                        break;
                    case VariableDeclaratorSyntax variable when variable.Initializer is not null
                        && variable.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax:
                        yield return variable.Initializer;
                        break;
                }
            }
        }

        private static bool IsSyntacticTypePosition(TypeSyntax typeSyntax, SyntaxNode declaration)
        {
            SyntaxNode current = typeSyntax;
            while (current.Parent is { } parent && !ReferenceEquals(current, declaration))
            {
                switch (parent)
                {
                    case QualifiedNameSyntax:
                    case AliasQualifiedNameSyntax:
                    case NullableTypeSyntax:
                    case FunctionPointerTypeSyntax:
                        current = parent;
                        continue;
                    case ArrayTypeSyntax array when ReferenceEquals(array.ElementType, current):
                    case PointerTypeSyntax pointer when ReferenceEquals(pointer.ElementType, current):
                    case RefTypeSyntax reference when ReferenceEquals(reference.Type, current):
                        current = parent;
                        continue;
                    case TypeArgumentListSyntax arguments when arguments.Arguments.Contains(current):
                        return true;
                    case FunctionPointerParameterSyntax parameter when ReferenceEquals(parameter.Type, current):
                        return true;
                    case TupleElementSyntax tupleElement when ReferenceEquals(tupleElement.Type, current):
                        return true;
                    case SimpleBaseTypeSyntax:
                    case TypeConstraintSyntax:
                        return parent is SimpleBaseTypeSyntax simpleBase && ReferenceEquals(simpleBase.Type, current)
                            || parent is TypeConstraintSyntax typeConstraint && ReferenceEquals(typeConstraint.Type, current);
                    case ParameterSyntax parameter when ReferenceEquals(parameter.Type, current):
                    case FunctionPointerParameterSyntax:
                        return true;
                    case MethodDeclarationSyntax method when ReferenceEquals(method.ReturnType, current):
                    case LocalFunctionStatementSyntax local when ReferenceEquals(local.ReturnType, current):
                    case DelegateDeclarationSyntax @delegate when ReferenceEquals(@delegate.ReturnType, current):
                    case PropertyDeclarationSyntax property when ReferenceEquals(property.Type, current):
                    case IndexerDeclarationSyntax indexer when ReferenceEquals(indexer.Type, current):
                    case EventDeclarationSyntax @event when ReferenceEquals(@event.Type, current):
                    case VariableDeclarationSyntax variable when ReferenceEquals(variable.Type, current):
                    case CatchDeclarationSyntax catchDeclaration when ReferenceEquals(catchDeclaration.Type, current):
                    case CastExpressionSyntax cast when ReferenceEquals(cast.Type, current):
                    case TypeOfExpressionSyntax typeOf when ReferenceEquals(typeOf.Type, current):
                    case SizeOfExpressionSyntax sizeOf when ReferenceEquals(sizeOf.Type, current):
                    case DefaultExpressionSyntax @default when ReferenceEquals(@default.Type, current):
                    case ObjectCreationExpressionSyntax creation when ReferenceEquals(creation.Type, current):
                    case ArrayCreationExpressionSyntax arrayCreation when ReferenceEquals(arrayCreation.Type, current):
                    case StackAllocArrayCreationExpressionSyntax stackAlloc when ReferenceEquals(stackAlloc.Type, current):
                    case DeclarationPatternSyntax declarationPattern when ReferenceEquals(declarationPattern.Type, current):
                    case TypePatternSyntax typePattern when ReferenceEquals(typePattern.Type, current):
                    case RecursivePatternSyntax recursivePattern when ReferenceEquals(recursivePattern.Type, current):
                    case DeclarationExpressionSyntax declarationExpression when ReferenceEquals(declarationExpression.Type, current):
                    case OperatorDeclarationSyntax operatorDeclaration when ReferenceEquals(operatorDeclaration.ReturnType, current):
                    case ConversionOperatorDeclarationSyntax conversion when ReferenceEquals(conversion.Type, current):
                        return true;
                    case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression) && ReferenceEquals(binary.Right, current):
                        return true;
                    case InvocationExpressionSyntax:
                    case MemberAccessExpressionSyntax:
                    case ArgumentSyntax:
                    case AttributeSyntax:
                    case NameColonSyntax:
                        return false;
                    default:
                        current = parent;
                        continue;
                }
            }

            return false;
        }

        private static bool IsExcludedSyntax(TypeSyntax typeSyntax, SyntaxNode declaration)
        {
            for (var current = typeSyntax.Parent; current is not null && !ReferenceEquals(current, declaration); current = current.Parent)
            {
                if (current is AttributeSyntax)
                {
                    return true;
                }

                if (current is InvocationExpressionSyntax invocation
                    && invocation.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == "nameof")
                {
                    return true;
                }
            }

            return false;
        }

        private void AddTypeReferences(
            TypeDependencyNode source,
            ITypeSymbol type,
            ProjectState sourceProject,
            SyntaxNode syntax,
            TypeDependencyEvidenceKind kind,
            HashSet<ITypeSymbol>? visited = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            visited ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            if (!visited.Add(type))
            {
                return;
            }

            switch (type)
            {
                case IArrayTypeSymbol array:
                    AddTypeReferences(source, array.ElementType, sourceProject, syntax, kind, visited);
                    break;
                case IPointerTypeSymbol pointer:
                    AddTypeReferences(source, pointer.PointedAtType, sourceProject, syntax, kind, visited);
                    break;
                case IFunctionPointerTypeSymbol functionPointer:
                    AddTypeReferences(source, functionPointer.Signature.ReturnType, sourceProject, syntax, kind, visited);
                    foreach (var parameter in functionPointer.Signature.Parameters)
                    {
                        AddTypeReferences(source, parameter.Type, sourceProject, syntax, kind, visited);
                    }
                    break;
                case INamedTypeSymbol named:
                    var normalized = named.OriginalDefinition;
                    AddTarget(source, normalized, sourceProject, syntax, kind);
                    if (named.ContainingType is { } containingType)
                    {
                        foreach (var containingArgument in containingType.TypeArguments)
                        {
                            AddTypeReferences(source, containingArgument, sourceProject, syntax, kind, visited);
                        }
                    }
                    foreach (var argument in named.TypeArguments)
                    {
                        AddTypeReferences(source, argument, sourceProject, syntax, kind, visited);
                    }
                    break;
            }
        }

        private void AddTarget(
            TypeDependencyNode source,
            INamedTypeSymbol target,
            ProjectState sourceProject,
            SyntaxNode syntax,
            TypeDependencyEvidenceKind kind)
        {
            if (target.TypeKind == TypeKind.Error || target.IsAnonymousType || target.SpecialType != SpecialType.None)
            {
                if (target.TypeKind == TypeKind.Error)
                {
                    throw Failure(sourceProject.Project, syntax, "required static type binding resolved to an error type");
                }
                return;
            }

            var targetAssembly = target.ContainingAssembly;
            if (targetAssembly is null || !target.Locations.Any(static location => location.IsInSource))
            {
                return;
            }

            if (target.IsImplicitlyDeclared || !HasExplicitTypeDeclaration(target))
            {
                return;
            }

            var ownerIdentities = target.DeclaringSyntaxReferences
                .Select(static reference => reference.SyntaxTree)
                .Where(sourceTreeProjects.ContainsKey)
                .SelectMany(tree => sourceTreeProjects[tree])
                .Select(projectId => TypeIdentity.Create(projectId, target))
                .Where(identity => nodes.ContainsKey(identity) || excludedGeneratedTypes.Contains(identity))
                .Distinct()
                .ToArray();
            if (ownerIdentities.Length == 0)
            {
                if (ReviewSourceClassifier.IsGeneratedSymbol(target))
                {
                    return;
                }
                throw Failure(sourceProject.Project, syntax,
                    $"eligible source type '{target.ToDisplayString()}' could not be mapped to a loaded declaring project and graph node");
            }

            if (ownerIdentities.Length > 1)
            {
                throw Failure(sourceProject.Project, syntax,
                    $"source type '{target.ToDisplayString()}' has ambiguous loaded declaring projects");
            }

            var targetIdentity = ownerIdentities[0];
            if (!nodes.TryGetValue(targetIdentity, out var targetNode))
            {
                if (excludedGeneratedTypes.Contains(targetIdentity))
                {
                    return;
                }
                throw Failure(sourceProject.Project, syntax,
                    $"eligible source type '{target.ToDisplayString()}' could not be mapped to a graph node");
            }
            if (source.IsTestProject)
            {
                if (targetNode.IsTestProject)
                {
                    return;
                }
            }
            else if (targetNode.IsTestProject)
            {
                return;
            }

            if (TypeIdentity.Create(source.ProjectId, source.Symbol) == targetIdentity)
            {
                return;
            }

            var pair = (TypeIdentity.Create(source.ProjectId, source.Symbol), targetIdentity);
            if (!edges.TryGetValue(pair, out var witnesses))
            {
                witnesses = [];
                edges.Add(pair, witnesses);
            }

            var document = sourceProject.Project.GetDocument(syntax.SyntaxTree);
            var path = document?.FilePath is { Length: > 0 } filePath
                ? GetSourcePath(context, document, sourceProject.Project, syntax.SpanStart)
                : syntax.SyntaxTree.FilePath?.Replace('\\', '/') ?? sourceProject.Project.Name;
            var witness = new TypeDependencyWitness(kind, sourceProject.Project.Id, sourceProject.Project.Name,
                sourceProject.Project.FilePath ?? sourceProject.Project.Name, path, syntax.Span);
            if (!witnesses.TryGetValue(kind, out var existing) || WitnessOrder(witness).CompareTo(WitnessOrder(existing), StringComparison.Ordinal) < 0)
            {
                witnesses[kind] = witness;
            }
        }

        private void AddMember(
            TypeDependencyNode source,
            ISymbol? target,
            ProjectState sourceProject,
            SyntaxNode syntax)
        {
            if (target is null)
            {
                throw Failure(sourceProject.Project, syntax, "required static member binding is unavailable");
            }

            var containingType = target.ContainingType;
            if (containingType is null)
            {
                return;
            }

            AddTarget(source, containingType.OriginalDefinition, sourceProject, syntax, TypeDependencyEvidenceKind.MemberUse);
        }

        private static string WitnessOrder(TypeDependencyWitness witness) =>
            $"{witness.ProjectPath.Replace('\\', '/')}\n{witness.SourcePath}\n{witness.Span.Start:D10}\n{witness.Kind}";

        private static bool HasExplicitTypeDeclaration(INamedTypeSymbol symbol) => symbol.DeclaringSyntaxReferences
            .Any(static reference => reference.GetSyntax() is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

        private static AnalysisFailedException Failure(Project project, SyntaxNode syntax, string reason) =>
            new($"Type dependency analysis failed in project '{project.Name}', source '{syntax.SyntaxTree.FilePath ?? "<unknown>"}', position {syntax.SpanStart}: {reason}.");

        private sealed class DependencyOperationWalker(
            EdgeCollector owner,
            TypeDependencyNode source,
            TypeDeclaration declaration,
            CancellationToken cancellationToken) : OperationWalker
        {
            public override void Visit(IOperation? operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation is null || operation.Kind == OperationKind.NameOf
                    || IsInGeneratedMember(operation.Syntax, declaration, cancellationToken))
                {
                    return;
                }

                if (operation.Kind == OperationKind.Invalid && IsRequiredBindingSyntax(operation, declaration, cancellationToken))
                {
                    throw Failure(declaration.Project.Project, operation.Syntax, $"required static operation binding is invalid ('{operation.Syntax}')");
                }

                base.Visit(operation);
            }

            public override void VisitInvocation(IInvocationOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.AddMember(source, operation.TargetMethod, declaration.Project, operation.Syntax);
                base.VisitInvocation(operation);
            }

            public override void VisitObjectCreation(IObjectCreationOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.Constructor is not null)
                {
                    owner.AddMember(source, operation.Constructor, declaration.Project, operation.Syntax);
                }
                else if (operation.Type is INamedTypeSymbol type)
                {
                    owner.AddTarget(source, type.OriginalDefinition, declaration.Project, operation.Syntax, TypeDependencyEvidenceKind.MemberUse);
                }
                base.VisitObjectCreation(operation);
            }

            public override void VisitMethodReference(IMethodReferenceOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.AddMember(source, operation.Method, declaration.Project, operation.Syntax);
                base.VisitMethodReference(operation);
            }

            public override void VisitPropertyReference(IPropertyReferenceOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.AddMember(source, operation.Property, declaration.Project, operation.Syntax);
                base.VisitPropertyReference(operation);
            }

            public override void VisitFieldReference(IFieldReferenceOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.AddMember(source, operation.Field, declaration.Project, operation.Syntax);
                base.VisitFieldReference(operation);
            }

            public override void VisitEventReference(IEventReferenceOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.AddMember(source, operation.Event, declaration.Project, operation.Syntax);
                base.VisitEventReference(operation);
            }

            public override void VisitBinaryOperator(IBinaryOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.OperatorMethod is not null)
                {
                    owner.AddMember(source, operation.OperatorMethod, declaration.Project, operation.Syntax);
                }
                base.VisitBinaryOperator(operation);
            }

            public override void VisitUnaryOperator(IUnaryOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.OperatorMethod is not null)
                {
                    owner.AddMember(source, operation.OperatorMethod, declaration.Project, operation.Syntax);
                }
                base.VisitUnaryOperator(operation);
            }

            public override void VisitConversion(IConversionOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.OperatorMethod is not null)
                {
                    owner.AddMember(source, operation.OperatorMethod, declaration.Project, operation.Syntax);
                }
                base.VisitConversion(operation);
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.OperatorMethod is not null)
                {
                    owner.AddMember(source, operation.OperatorMethod, declaration.Project, operation.Syntax);
                }
                base.VisitCompoundAssignment(operation);
            }

            public override void VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.OperatorMethod is not null)
                {
                    owner.AddMember(source, operation.OperatorMethod, declaration.Project, operation.Syntax);
                }
                base.VisitIncrementOrDecrement(operation);
            }

            private static bool IsRequiredBindingSyntax(IOperation operation, TypeDeclaration declaration, CancellationToken cancellationToken)
            {
                if (operation.Syntax is IdentifierNameSyntax identifier)
                {
                    var convertedType = declaration.SemanticModel.GetTypeInfo(identifier, cancellationToken).ConvertedType;
                    if (convertedType?.TypeKind == TypeKind.Delegate)
                    {
                        return true;
                    }
                }

                return operation.Syntax is InvocationExpressionSyntax
                    or ObjectCreationExpressionSyntax
                    or ImplicitObjectCreationExpressionSyntax
                    or MemberAccessExpressionSyntax
                    or ElementAccessExpressionSyntax
                    or BinaryExpressionSyntax
                    or PrefixUnaryExpressionSyntax
                    or PostfixUnaryExpressionSyntax
                    or AssignmentExpressionSyntax
                    or CastExpressionSyntax;
            }
        }
    }

    private sealed record TypeIdentity(ProjectId ProjectId, string StableId)
    {
        public static TypeIdentity Create(ProjectId projectId, INamedTypeSymbol symbol) =>
            new(projectId, DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition)
                ?? $"{symbol.ContainingNamespace?.ToDisplayString()}::{symbol.MetadataName}:{symbol.ContainingAssembly?.Identity.GetDisplayName()}");
    }
}

internal sealed class TypeDependencyNode(
    INamedTypeSymbol symbol,
    ProjectId projectId,
    string projectName,
    string projectPath,
    bool isTestProject,
    List<TypeDependencyDeclarationLocation> declarations)
{
    private List<TypeDependencyDeclarationLocation>? declarationBuilder = declarations;

    public INamedTypeSymbol Symbol { get; } = symbol;
    public ProjectId ProjectId { get; } = projectId;
    public string ProjectName { get; } = projectName;
    public bool IsTestProject { get; } = isTestProject;
    public IReadOnlyList<TypeDependencyDeclarationLocation> Declarations { get; private set; } = declarations.AsReadOnly();

    public void AddDeclaration(TypeDependencyDeclarationLocation declaration) =>
        (declarationBuilder ?? throw new InvalidOperationException("Type dependency declarations are already finalized.")).Add(declaration);

    public void Freeze()
    {
        if (declarationBuilder is null)
        {
            return;
        }

        Declarations = Array.AsReadOnly(declarationBuilder.ToArray());
        declarationBuilder = null;
    }
    public string StableId { get; } = $"{symbol.ContainingAssembly?.Identity.Name}:{projectPath.Replace('\\', '/')}/{projectName}:{DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition) ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
}

internal sealed record TypeDependencyDeclarationLocation(string SourcePath, TextSpan Span, ProjectId ProjectId, string ProjectName);

internal sealed record TypeDependencyWitness(
    TypeDependencyEvidenceKind Kind,
    ProjectId ProjectId,
    string ProjectName,
    string ProjectPath,
    string SourcePath,
    TextSpan Span);

internal sealed record TypeDependencyEdge(
    TypeDependencyNode From,
    TypeDependencyNode To,
    IReadOnlyList<TypeDependencyWitness> Witnesses);

internal sealed record TypeDependencyGraph(
    IReadOnlyList<TypeDependencyNode> Nodes,
    IReadOnlyList<TypeDependencyEdge> Edges)
{
    public IReadOnlyList<TypeDependencyNode> ProductionNodes => Nodes.Where(static node => !node.IsTestProject).ToArray();
    public IReadOnlyList<TypeDependencyEdge> ProductionEdges => Edges.Where(static edge => !edge.From.IsTestProject).ToArray();
    public IReadOnlyList<TypeDependencyEdge> TestContextEdges => Edges.Where(static edge => edge.From.IsTestProject).ToArray();
}

internal enum TypeDependencyEvidenceKind
{
    Inheritance,
    ExplicitTypeUse,
    MemberUse,
}
