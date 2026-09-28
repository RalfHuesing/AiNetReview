namespace AiNetReview.Core.Rules.DeadCodeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>Selects explicit types and ordinary methods without known direct or recognized indirect solution use.</summary>
public sealed class DeadCodeCandidatesRule : IReviewRule
{
    private const string ExternalLibrary = "external_library";
    private const string ClosedSolution = "closed_solution";

    private static readonly RuleOptionDescriptor ApiSurfaceOption = new(
        "apiSurface",
        "Public API is protected by default; closed_solution also considers publicly visible declarations as candidates.",
        JsonSerializer.SerializeToElement(ExternalLibrary),
        static value => value.ValueKind == JsonValueKind.String
            && value.GetString() is ExternalLibrary or ClosedSolution);

    private static readonly RuleOptionDescriptor EntryPointAttributesOption = new(
        "entryPointAttributes",
        "Additional fully qualified attribute type names that mark declarations as indirect entry points.",
        JsonSerializer.SerializeToElement(Array.Empty<string>()),
        static value => value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(static item => item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString())
                && StringComparer.Ordinal.Equals(item.GetString(), item.GetString()!.Trim())
                && item.GetString()!.Contains('.', StringComparison.Ordinal)));

    public RuleDescriptor Descriptor { get; } = new(
        "dead-code-candidates",
        "Dead Code Candidates",
        1,
        "Selects explicit C# types and ordinary methods without known direct or recognized indirect use in the loaded solution for human review.",
        "A candidate has no known direct semantic reference or recognized indirect binding in production, test, generated C#, or captured markup. This is a review signal, not proof that the declaration is unused.",
        [
            "Is the declaration reached through reflection, dependency injection, framework conventions, or markup?",
            "Does code outside the analyzed solution use this declaration?",
        ],
        [ApiSurfaceOption, EntryPointAttributesOption]);

    public async Task<RuleResult> ExecuteAsync(
        ReviewContext context,
        RuleOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var apiSurface = options["apiSurface"].GetString()!;
        var referenceIndex = await SolutionReferenceIndex.CreateAsync(context, cancellationToken).ConfigureAwait(false);
        var indirectUsage = await DeadCodeIndirectUsageIndex.CreateAsync(
            context,
            options["entryPointAttributes"].EnumerateArray().Select(static item => item.GetString()!).ToArray(),
            cancellationToken).ConfigureAwait(false);
        var findings = new List<FindingDraft>();
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
            var entryPoint = compilation.GetEntryPoint(cancellationToken);
            var declarations = await CollectDeclarationsAsync(context, project, cancellationToken).ConfigureAwait(false);

            var methodsByType = new Dictionary<INamedTypeSymbol, List<Declaration>>(SymbolEqualityComparer.Default);
            foreach (var method in declarations.Methods)
            {
                var containingType = ((IMethodSymbol)method.Symbol).ContainingType;
                if (!methodsByType.TryGetValue(containingType, out var members))
                {
                    members = [];
                    methodsByType.Add(containingType, members);
                }

                members.Add(method);
            }

            foreach (var type in declarations.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                methodsByType.TryGetValue((INamedTypeSymbol)type.Symbol, out var methods);
                methods ??= [];

                var typeCoverage = referenceIndex.GetCoverage(type.Symbol);
                var typeHasExternalUse = typeCoverage.References.Any(static reference => !reference.IsSelfReference)
                    || indirectUsage.IsProtected(type.Symbol);
                var typeHasUncertainty = typeCoverage.HasUnresolvedBindings
                    || indirectUsage.HasUncertainty(type.Symbol)
                    || methods.Any(method => referenceIndex.GetCoverage(method.Symbol).HasUnresolvedBindings
                        || indirectUsage.HasUncertainty(method.Symbol));
                if (!typeHasExternalUse && !typeHasUncertainty && !IsApiProtected(type.Symbol, apiSurface))
                {
                    findings.Add(CreateFinding(type, "type-candidate"));
                    continue;
                }

                foreach (var method in methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsCompilerEntryPoint((IMethodSymbol)method.Symbol, entryPoint)
                        || IsApiProtected(method.Symbol, apiSurface)
                        || indirectUsage.IsProtected(method.Symbol))
                    {
                        continue;
                    }

                    var coverage = referenceIndex.GetCoverage(method.Symbol);
                    if (!coverage.HasUnresolvedBindings && !indirectUsage.HasUncertainty(method.Symbol)
                        && HasNoExternalUsage(coverage) && !indirectUsage.IsProtected(method.Symbol))
                    {
                        findings.Add(CreateFinding(method, "method-candidate"));
                    }
                }
            }

            foreach (var method in declarations.Methods.Where(method => !methodsByType.ContainsKey(((IMethodSymbol)method.Symbol).ContainingType)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsCompilerEntryPoint((IMethodSymbol)method.Symbol, entryPoint) || IsApiProtected(method.Symbol, apiSurface)
                    || indirectUsage.IsProtected(method.Symbol))
                {
                    continue;
                }

                var coverage = referenceIndex.GetCoverage(method.Symbol);
                if (!coverage.HasUnresolvedBindings && !indirectUsage.HasUncertainty(method.Symbol)
                    && HasNoExternalUsage(coverage))
                {
                    findings.Add(CreateFinding(method, "method-candidate"));
                }
            }
        }

        return new RuleResult(findings
            .OrderBy(static finding => finding.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.SourcePath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.StartLine)
            .ThenBy(static finding => finding.SubjectId, StringComparer.Ordinal));
    }

    private static async Task<DeclarationSet> CollectDeclarationsAsync(
        ReviewContext context,
        Project project,
        CancellationToken cancellationToken)
    {
        var types = new Dictionary<INamedTypeSymbol, Declaration>(SymbolEqualityComparer.Default);
        var methods = new Dictionary<IMethodSymbol, Declaration>(SymbolEqualityComparer.Default);
        foreach (var document in project.Documents.OrderBy(static item => item.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(PathExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)
                || await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Semantic model could not be created for document '{document.Name}'.");
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

            foreach (var declaration in root.DescendantNodes().Where(static node =>
                         node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol
                    || symbol.IsImplicitlyDeclared
                    || ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                {
                    continue;
                }

                var identifier = declaration switch
                {
                    BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier,
                    DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier,
                    _ => throw new InvalidOperationException("Unsupported explicit type declaration."),
                };
                AddDeclaration(types, symbol, declaration, identifier, document, sourceText, context, project);
            }

            foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol symbol
                    || symbol.IsImplicitlyDeclared
                    || symbol.MethodKind != MethodKind.Ordinary
                    || symbol.ContainingType.TypeKind == TypeKind.Interface
                    || symbol.OverriddenMethod is not null
                    || IsInterfaceImplementation(symbol)
                    || ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                {
                    continue;
                }

                AddDeclaration(methods, symbol, declaration, declaration.Identifier, document, sourceText, context, project);
            }
        }

        return new DeclarationSet(types.Values.ToArray(), methods.Values.ToArray());
    }

    private static void AddDeclaration<TSymbol>(
        IDictionary<TSymbol, Declaration> declarations,
        TSymbol symbol,
        SyntaxNode syntax,
        SyntaxToken identifier,
        Document document,
        SourceText sourceText,
        ReviewContext context,
        Project project)
        where TSymbol : class, ISymbol
    {
        if (declarations.ContainsKey(symbol))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(document.FilePath) || string.IsNullOrWhiteSpace(project.FilePath))
        {
            throw new AnalysisFailedException("A candidate declaration has no source or project path.");
        }

        var sourcePath = context.GetProjectRelativePath(document.FilePath);
        var projectPath = context.GetProjectRelativePath(project.FilePath);
        var line = sourceText.Lines.GetLineFromPosition(identifier.SpanStart);
        declarations.Add(symbol, new Declaration(
            symbol,
            projectPath,
            sourcePath,
            DocumentationCommentId.CreateDeclarationId(symbol)
                ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceText.Lines.GetLinePosition(syntax.SpanStart).Line + 1,
            line.LineNumber + 1,
            GetSnippet(line.ToString(), identifier.SpanStart - line.Start, identifier.Span.Length)));
    }

    private static FindingDraft CreateFinding(Declaration declaration, string discriminator) =>
        new(
            declaration.ProjectPath,
            declaration.SourcePath,
            declaration.SubjectId,
            discriminator,
            declaration.StartLine,
            "No direct semantic use or recognized indirect binding was found in the loaded solution. Review possible external uses before drawing a conclusion.",
            new Dictionary<string, double>(StringComparer.Ordinal),
            [new FindingEvidence(
                declaration.SourcePath,
                declaration.EvidenceLine,
                declaration.Symbol is INamedTypeSymbol ? "Type declaration" : "Method declaration",
                "Candidate declaration selected from production C# source.",
                declaration.Snippet)]);

    private static bool IsApiProtected(ISymbol symbol, string apiSurface) =>
        apiSurface == ExternalLibrary && IsExternallyVisible(symbol);

    private static bool HasNoExternalUsage(SolutionSymbolReferenceCoverage coverage) =>
        coverage.References.All(static reference => reference.IsSelfReference);

    private static bool IsExternallyVisible(ISymbol symbol)
    {
        if (!IsExternallyVisibleAccessibility(symbol.DeclaredAccessibility))
        {
            return false;
        }

        for (var containingType = symbol.ContainingType; containingType is not null; containingType = containingType.ContainingType)
        {
            if (!IsExternallyVisibleAccessibility(containingType.DeclaredAccessibility))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExternallyVisibleAccessibility(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    private static bool IsCompilerEntryPoint(IMethodSymbol method, IMethodSymbol? entryPoint) =>
        entryPoint is not null && SymbolEqualityComparer.Default.Equals(method, entryPoint);

    private static bool IsInterfaceImplementation(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var interfaceMethod in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(interfaceMethod), method))
                {
                    return true;
                }
            }
        }

        return method.ExplicitInterfaceImplementations.Length > 0;
    }

    private static string GetSnippet(string line, int identifierStart, int identifierLength)
    {
        const int maximumLength = 180;
        if (line.Length <= maximumLength)
        {
            return line.Trim();
        }

        var start = Math.Max(0, identifierStart - Math.Max(0, (maximumLength - identifierLength) / 2));
        start = Math.Min(start, line.Length - maximumLength);
        if (start > identifierStart)
        {
            start = identifierStart;
        }

        return line.Substring(start, Math.Min(maximumLength, line.Length - start)).Trim();
    }

    private static string? PathExtension(string? path) => path is null ? null : System.IO.Path.GetExtension(path);

    private sealed record DeclarationSet(Declaration[] Types, Declaration[] Methods);

    private sealed record Declaration(
        ISymbol Symbol,
        string ProjectPath,
        string SourcePath,
        string SubjectId,
        int StartLine,
        int EvidenceLine,
        string Snippet);
}
