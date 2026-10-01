namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

/// <summary>Finds exact statement fragments after normalizing bound local and parameter names.</summary>
internal static class StructuralDuplicateDetector
{
    private const int MinimumStatementCount = 3;
    private const int MinimumTokenCount = 60;

    internal static async Task<IReadOnlyList<StructuralDuplicateGroup>> ScanAsync(
        ReviewContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var occurrences = new List<StructuralDuplicateOccurrence>();
        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp)
            .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(static project => project.Name, StringComparer.Ordinal);


        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' has no project file path.");
            }

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
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
                if (semanticModel is null)
                {
                    throw new AnalysisFailedException($"Semantic model could not be created for document '{document.Name}'.");
                }

                var sourcePath = context.GetProjectRelativePath(document.FilePath);
                foreach (var declaration in root.DescendantNodesAndSelf().Where(IsOwnerDeclaration))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var body = GetBlockBody(declaration);
                    if (body is null
                        || semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol owner
                        || owner.IsImplicitlyDeclared
                        || !IsSupportedMethodKind(owner.MethodKind)
                        || ReviewSourceClassifier.IsGeneratedSymbol(owner))
                    {
                        continue;
                    }

                    var ownerStart = declaration.SpanStart;
                    var ownerId = DocumentationCommentId.CreateDeclarationId(owner)
                        ?? owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "@"
                            + ownerStart.ToString(CultureInfo.InvariantCulture);
                    occurrences.AddRange(CollectFromOwner(body, semanticModel, projectPath, sourcePath, ownerId, ownerStart, cancellationToken));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return BuildGroups(occurrences, cancellationToken);
    }

    private static List<StructuralDuplicateOccurrence> CollectFromOwner(
        BlockSyntax body,
        SemanticModel semanticModel,
        string projectPath,
        string sourcePath,
        string ownerId,
        int ownerStart,
        CancellationToken cancellationToken)
    {
        // The callback above is intentionally side-effect free; collect each syntax list here so
        // one owner has a single stable location for all of its fragment candidates.
        var found = new List<StructuralDuplicateOccurrence>();
        VisitStatementLists(body, body, list => found.AddRange(CollectFromList(
            list, semanticModel, projectPath, sourcePath, ownerId, ownerStart, cancellationToken)), cancellationToken);
        return found;
    }

    private static void VisitStatementLists(
        SyntaxNode node,
        SyntaxNode ownerBody,
        Action<SyntaxList<StatementSyntax>> collect,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node != ownerBody && IsNestedExecutable(node))
        {
            return;
        }

        switch (node)
        {
            case BlockSyntax block:
                collect(block.Statements);
                break;
            case SwitchSectionSyntax section:
                collect(section.Statements);
                break;
        }

        foreach (var child in node.ChildNodes())
        {
            VisitStatementLists(child, ownerBody, collect, cancellationToken);
        }
    }

    private static List<StructuralDuplicateOccurrence> CollectFromList(
        SyntaxList<StatementSyntax> statements,
        SemanticModel semanticModel,
        string projectPath,
        string sourcePath,
        string ownerId,
        int ownerStart,
        CancellationToken cancellationToken)
    {
        var result = new List<StructuralDuplicateOccurrence>();
        var eligible = new bool[statements.Count];
        var tokenCounts = new int[statements.Count];
        for (var index = 0; index < statements.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statement = statements[index];
            if (ContainsNestedExecutable(statement) || HasBindingBarrier(statement, semanticModel, cancellationToken))
            {
                continue;
            }

            var tokens = GetOriginalTokens(statement);
            tokenCounts[index] = tokens.Count;
            eligible[index] = true;
        }

        for (var start = 0; start < statements.Count; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!eligible[start])
            {
                continue;
            }

            var tokenCount = 0;
            for (var end = start; end < statements.Count && eligible[end]; end++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tokenCount += tokenCounts[end];
                var statementCount = end - start + 1;
                if (statementCount < MinimumStatementCount || tokenCount < MinimumTokenCount)
                {
                    continue;
                }

                var normalized = NormalizeFragment(statements, start, statementCount, semanticModel, cancellationToken);
                if (normalized is null)
                {
                    continue;
                }

                var firstToken = GetOriginalTokens(statements[start])[0];
                var lastToken = GetOriginalTokens(statements[end]).Last();
                var span = TextSpan.FromBounds(firstToken.SpanStart, lastToken.Span.End);
                result.Add(new StructuralDuplicateOccurrence(
                    projectPath,
                    sourcePath,
                    ownerId,
                    ownerStart,
                    span.Start,
                    span.Length,
                    statementCount,
                    tokenCount,
                    normalized,
                    firstToken,
                    lastToken,
                    semanticModel.SyntaxTree.GetText(cancellationToken)));
            }
        }

        return result;
    }

    private static IReadOnlyList<StructuralDuplicateGroup> BuildGroups(
        List<StructuralDuplicateOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        var distinct = occurrences
            .GroupBy(static occurrence => (occurrence.ProjectPath, occurrence.SourcePath, occurrence.OwnerStart, occurrence.StartOffset, occurrence.SpanLength))
            .Select(static group => group.First())
            .GroupBy(static occurrence => occurrence.NormalizedForm, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.ToList();
                ordered.Sort(OccurrenceOrder);
                return new StructuralDuplicateGroup(ordered);
            })
            .Where(static group => group.Occurrences.Select(static occurrence => occurrence.OwnerKey).Distinct().Skip(1).Any())
            .ToList();

        var suppressed = new HashSet<StructuralDuplicateGroup>();
        foreach (var smaller in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var larger in distinct)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (smaller.TokenCount >= larger.TokenCount)
                {
                    continue;
                }

                if (smaller.Occurrences.All(small => larger.Occurrences.Any(big => Contains(big, small))))
                {
                    suppressed.Add(smaller);
                    break;
                }
            }
        }

        return distinct.Where(group => !suppressed.Contains(group))
            .OrderBy(static group => group.Occurrences[0], Comparer<StructuralDuplicateOccurrence>.Create(OccurrenceOrder))
            .ToArray();
    }

    private static bool Contains(StructuralDuplicateOccurrence larger, StructuralDuplicateOccurrence smaller) =>
        larger.ProjectPath == smaller.ProjectPath
        && larger.SourcePath == smaller.SourcePath
        && larger.OwnerStart == smaller.OwnerStart
        && larger.StartOffset <= smaller.StartOffset
        && larger.EndOffset >= smaller.EndOffset
        && larger.TokenCount > smaller.TokenCount;

    private static int OccurrenceOrder(StructuralDuplicateOccurrence left, StructuralDuplicateOccurrence right)
    {
        var result = StringComparer.Ordinal.Compare(left.ProjectPath, right.ProjectPath);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(left.SourcePath, right.SourcePath);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(left.OwnerId, right.OwnerId);
        if (result != 0) return result;
        result = left.StartOffset.CompareTo(right.StartOffset);
        return result != 0 ? result : left.SpanLength.CompareTo(right.SpanLength);
    }

    private static bool HasBindingBarrier(StatementSyntax statement, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var node in statement.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is IdentifierNameSyntax identifier && RequiresBinding(identifier))
            {
                var info = semanticModel.GetSymbolInfo(identifier, cancellationToken);
                if (info.Symbol is null || info.CandidateSymbols.Length > 0)
                {
                    return true;
                }

                if (info.Symbol is IPropertySymbol or IFieldSymbol or IEventSymbol or IMethodSymbol
                    && IsDynamicMemberBinding(identifier, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            if (node is GenericNameSyntax genericName && RequiresExpressionBinding(genericName))
            {
                var info = semanticModel.GetSymbolInfo(genericName, cancellationToken);
                if (info.Symbol is null || info.CandidateSymbols.Length > 0)
                {
                    return true;
                }
            }

            if (node is ExpressionSyntax expression)
            {
                var operation = semanticModel.GetOperation(expression, cancellationToken);
                if (operation is IInvalidOperation || operation?.Kind is OperationKind.DynamicInvocation
                    or OperationKind.DynamicMemberReference or OperationKind.DynamicIndexerAccess)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool RequiresBinding(IdentifierNameSyntax identifier)
    {
        if (identifier.Ancestors().TakeWhile(static node => node is not StatementSyntax)
            .Any(static node => node is NameColonSyntax or NameEqualsSyntax))
        {
            return false;
        }

        // Type-name syntax is also represented by IdentifierNameSyntax. Restrict the
        // binding check to expressions for which Roslyn produced an operation.
        if (identifier.Parent is not null && identifier.Parent is not StatementSyntax)
        {
            // GetOperation can be null for the name child of a member access while the
            // enclosing expression still carries the actual binding operation.
            if (identifier.Parent is MemberAccessExpressionSyntax member && member.Name == identifier
                || identifier.Parent is MemberBindingExpressionSyntax binding && binding.Name == identifier)
            {
                return true;
            }

            if (identifier.Parent is TypeSyntax)
            {
                return false;
            }
        }

        return identifier.Parent is not LabeledStatementSyntax
            && identifier.Parent is not GotoStatementSyntax
            && identifier.Parent is not BreakStatementSyntax
            && identifier.Parent is not ContinueStatementSyntax;
    }

    private static bool RequiresExpressionBinding(GenericNameSyntax name)
    {
        return name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name
            || name.Parent is MemberAccessExpressionSyntax member && member.Name == name
            || name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name;
    }

    private static bool IsDynamicMemberBinding(IdentifierNameSyntax identifier, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var node in identifier.AncestorsAndSelf().Take(4))
        {
            if (node is MemberAccessExpressionSyntax member)
            {
                return semanticModel.GetTypeInfo(member.Expression, cancellationToken).Type?.TypeKind == TypeKind.Dynamic;
            }

            if (node is ElementAccessExpressionSyntax element)
            {
                return semanticModel.GetTypeInfo(element.Expression, cancellationToken).Type?.TypeKind == TypeKind.Dynamic;
            }
        }

        return false;
    }

    private static string? NormalizeFragment(
        SyntaxList<StatementSyntax> statements,
        int start,
        int count,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var symbols = new Dictionary<ISymbol, (string Category, int Index, string TypeKey)>(SymbolEqualityComparer.Default);
        var nextLocal = 0;
        var nextParameter = 0;
        AppendPart(builder, "FragmentStatementCount", count.ToString(CultureInfo.InvariantCulture));
        for (var index = start; index < start + count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AppendNode(statements[index], builder, symbols, ref nextLocal, ref nextParameter, semanticModel, cancellationToken))
            {
                return null;
            }
        }

        return builder.ToString();
    }

    private static bool AppendNode(
        SyntaxNode node,
        StringBuilder builder,
        Dictionary<ISymbol, (string Category, int Index, string TypeKey)> symbols,
        ref int nextLocal,
        ref int nextParameter,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppendPart(builder, "N", node.RawKind.ToString(CultureInfo.InvariantCulture));
        foreach (var child in node.ChildNodesAndTokens())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.IsNode)
            {
                if (!AppendNode(child.AsNode()!, builder, symbols, ref nextLocal, ref nextParameter, semanticModel, cancellationToken))
                {
                    return false;
                }
            }
            else
            {
                var token = child.AsToken();
                if (token.IsMissing || token.IsKind(SyntaxKind.EndOfFileToken))
                {
                    continue;
                }

                if (TryGetLocalOrParameter(token, semanticModel, cancellationToken, out var symbol))
                {
                    if (!symbols.TryGetValue(symbol, out var placeholder))
                    {
                        var category = symbol is ILocalSymbol ? "L" : "P";
                        var type = symbol switch
                        {
                            ILocalSymbol local => local.Type,
                            IParameterSymbol parameter => parameter.Type,
                            _ => throw new InvalidOperationException("Unexpected normalized symbol kind."),
                        };
                        var typeKey = CreateTypeKey(type);
                        if (typeKey is null)
                        {
                            return false;
                        }
                        if (category == "L")
                        {
                            placeholder = (category, nextLocal++, typeKey);
                        }
                        else
                        {
                            placeholder = (category, nextParameter++, typeKey);
                        }

                        symbols.Add(symbol, placeholder);
                    }

                    AppendPart(builder, placeholder.Category, placeholder.Index.ToString(CultureInfo.InvariantCulture));
                    AppendPart(builder, "T", placeholder.TypeKey);
                }
                else
                {
                    AppendPart(builder, "K", token.RawKind.ToString(CultureInfo.InvariantCulture));
                    AppendPart(builder, "V", token.ValueText);
                }
            }
        }

        AppendPart(builder, "E", node.RawKind.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static bool TryGetLocalOrParameter(
        SyntaxToken token,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol)
    {
        if (token.Parent is IdentifierNameSyntax identifier
            && (identifier.Parent is NameColonSyntax or NameEqualsSyntax or LabeledStatementSyntax
                || identifier.Parent is GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax))
        {
            // These names can bind to method parameters or symbols, but their spelling is
            // part of the call/label syntax and is deliberately retained by the contract.
            symbol = null!;
            return false;
        }

        var declared = token.Parent switch
        {
            VariableDeclaratorSyntax declarator when declarator.Identifier == token => semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
            ParameterSyntax parameter when parameter.Identifier == token => semanticModel.GetDeclaredSymbol(parameter, cancellationToken),
            ForEachStatementSyntax forEach when forEach.Identifier == token => semanticModel.GetDeclaredSymbol(forEach, cancellationToken),
            CatchDeclarationSyntax catchDeclaration when catchDeclaration.Identifier == token => semanticModel.GetDeclaredSymbol(catchDeclaration, cancellationToken),
            SingleVariableDesignationSyntax designation when designation.Identifier == token => semanticModel.GetDeclaredSymbol(designation, cancellationToken),
            _ => null,
        };
        if (declared is ILocalSymbol or IParameterSymbol)
        {
            symbol = declared;
            return true;
        }

        var name = token.Parent as IdentifierNameSyntax;
        var bound = name is null ? null : semanticModel.GetSymbolInfo(name, cancellationToken).Symbol;
        if (bound is ILocalSymbol or IParameterSymbol)
        {
            symbol = bound;
            return true;
        }

        symbol = null!;
        return false;
    }

    private static string? CreateTypeKey(ITypeSymbol type)
    {
        var builder = new StringBuilder();
        return AppendTypeKey(type, builder) ? builder.ToString() : null;
    }

    private static bool AppendTypeKey(ITypeSymbol type, StringBuilder builder)
    {
        if (type.TypeKind == TypeKind.Error)
        {
            return false;
        }

        AppendPart(builder, "A", ((int)type.NullableAnnotation).ToString(CultureInfo.InvariantCulture));
        switch (type)
        {
            case IDynamicTypeSymbol:
                AppendPart(builder, "Dynamic", string.Empty);
                break;
            case IArrayTypeSymbol array:
                AppendPart(builder, "ArrayRank", array.Rank.ToString(CultureInfo.InvariantCulture));
                return AppendNestedTypeKey(builder, "ArrayElement", array.ElementType);
            case IPointerTypeSymbol pointer:
                return AppendNestedTypeKey(builder, "PointerTarget", pointer.PointedAtType);
            case IFunctionPointerTypeSymbol functionPointer:
                AppendPart(builder, "FunctionPointerConvention", functionPointer.Signature.CallingConvention.ToString());
                AppendPart(builder, "FunctionPointerUnmanagedConventionCount", functionPointer.Signature.UnmanagedCallingConventionTypes.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var conventionType in functionPointer.Signature.UnmanagedCallingConventionTypes)
                {
                    AppendPart(builder, "FunctionPointerUnmanagedConvention", conventionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                AppendRefKind(builder, functionPointer.Signature.RefKind);
                if (!AppendNestedTypeKey(builder, "FunctionPointerReturnType", functionPointer.Signature.ReturnType)) return false;
                AppendPart(builder, "FunctionPointerParameterCount", functionPointer.Signature.Parameters.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var parameter in functionPointer.Signature.Parameters)
                {
                    AppendRefKind(builder, parameter.RefKind);
                    if (!AppendNestedTypeKey(builder, "FunctionPointerParameterType", parameter.Type)) return false;
                }
                return true;
            case ITypeParameterSymbol parameter:
                AppendPart(builder, "TypeParameter", parameter.TypeParameterKind.ToString());
                AppendPart(builder, "TypeParameterOrdinal", parameter.Ordinal.ToString(CultureInfo.InvariantCulture));
                AppendPart(builder, "TypeParameterOwner", SymbolOwnerIdentity(parameter.ContainingSymbol));
                AppendPart(builder, "TypeParameterAssembly", parameter.ContainingAssembly?.Identity.ToString() ?? string.Empty);
                return true;
            case INamedTypeSymbol named:
                AppendPart(builder, "NamedAssembly", named.ContainingAssembly?.Identity.ToString() ?? string.Empty);
                AppendPart(builder, "NamedDefinition", named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                if (named.ContainingType is not null)
                {
                    if (!AppendNestedTypeKey(builder, "ContainingType", named.ContainingType)) return false;
                }

                AppendPart(builder, "NamedTypeArgumentCount", named.TypeArguments.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var argument in named.TypeArguments)
                {
                    if (!AppendNestedTypeKey(builder, "NamedTypeArgument", argument)) return false;
                }
                return true;
            default:
                return false;
        }

        return true;
    }

    private static bool AppendNestedTypeKey(StringBuilder builder, string tag, ITypeSymbol type)
    {
        var nested = new StringBuilder();
        if (!AppendTypeKey(type, nested))
        {
            return false;
        }

        AppendPart(builder, tag, nested.ToString());
        return true;
    }

    private static string SymbolOwnerIdentity(ISymbol symbol)
    {
        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
        if (declarationId is not null)
        {
            return declarationId;
        }

        var location = symbol.Locations.FirstOrDefault(static item => item.IsInSource);
        var sourceIdentity = location is null
            ? string.Empty
            : "@" + (location.SourceTree?.FilePath ?? string.Empty) + ":"
                + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture) + ":"
                + location.SourceSpan.Length.ToString(CultureInfo.InvariantCulture);
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + sourceIdentity;
    }

    private static void AppendRefKind(StringBuilder builder, RefKind refKind) =>
        AppendPart(builder, "RefKind", refKind.ToString());

    private static void AppendPart(StringBuilder builder, string tag, string value)
    {
        builder.Append(tag.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(tag)
            .Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }

    private static List<SyntaxToken> GetOriginalTokens(SyntaxNode node) => node.DescendantTokens(descendIntoTrivia: false)
        .Where(static token => !token.IsMissing && !token.IsKind(SyntaxKind.EndOfFileToken))
        .ToList();

    private static bool IsOwnerDeclaration(SyntaxNode node) => node is
        MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax;

    private static BlockSyntax? GetBlockBody(SyntaxNode declaration) => declaration switch
    {
        BaseMethodDeclarationSyntax method => method.Body,
        AccessorDeclarationSyntax accessor => accessor.Body,
        LocalFunctionStatementSyntax localFunction => localFunction.Body,
        _ => null,
    };

    private static bool IsSupportedMethodKind(MethodKind kind) => kind is
        MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
        or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd
        or MethodKind.EventRemove or MethodKind.LocalFunction;

    private static bool IsNestedExecutable(SyntaxNode node) => node is
        LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    private static bool ContainsNestedExecutable(StatementSyntax statement) =>
        statement.DescendantNodesAndSelf().Any(static node => IsNestedExecutable(node));

    internal sealed record StructuralDuplicateOccurrence(
        string ProjectPath,
        string SourcePath,
        string OwnerId,
        int OwnerStart,
        int StartOffset,
        int SpanLength,
        int StatementCount,
        int TokenCount,
        string NormalizedForm,
        SyntaxToken FirstToken,
        SyntaxToken LastToken,
        Microsoft.CodeAnalysis.Text.SourceText SourceText)
    {
        public int EndOffset => StartOffset + SpanLength;
        public (string ProjectPath, string SourcePath, int OwnerStart) OwnerKey => (ProjectPath, SourcePath, OwnerStart);
    }

    internal sealed class StructuralDuplicateGroup(IReadOnlyList<StructuralDuplicateOccurrence> occurrences)
    {
        internal IReadOnlyList<StructuralDuplicateOccurrence> Occurrences { get; } = occurrences;
        internal int TokenCount => Occurrences[0].TokenCount;
        internal int StatementCount => Occurrences[0].StatementCount;
    }
}
