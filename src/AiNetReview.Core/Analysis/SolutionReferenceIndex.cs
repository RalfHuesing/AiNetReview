namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

/// <summary>Indexes semantically bound type and method references from one loaded solution snapshot.</summary>
public sealed class SolutionReferenceIndex
{
    private readonly Dictionary<string, List<SolutionSymbolReference>> references = new(StringComparer.Ordinal);
    private readonly HashSet<string> uncertainSymbols = new(StringComparer.Ordinal);

    private SolutionReferenceIndex()
    {
    }

    /// <summary>Builds a complete reference index across every C# project and generated C# document.</summary>
    public static async Task<SolutionReferenceIndex> CreateAsync(
        ReviewContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var index = new SolutionReferenceIndex();
        var projectCount = 0;

        foreach (var project in context.Solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectCount++;
            if (!project.SupportsCompilation)
            {
                throw new AnalysisFailedException($"Semantic references could not be enumerated for project '{project.Name}'.");
            }

            Compilation compilation;
            Document[] generatedDocuments;
            try
            {
                compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
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
            catch (Exception ex)
            {
                throw new AnalysisFailedException($"Semantic references could not be enumerated for project '{project.Name}'.", ex);
            }

            var projectIsTest = ReviewSourceClassifier.IsTestProject(project);
            var documents = project.Documents.Concat(generatedDocuments).ToArray();
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                        ?? compilation.GetSemanticModel(root.SyntaxTree, ignoreAccessibility: true);
                    var generated = generatedDocuments.Any(candidate => candidate.Id == document.Id)
                        || await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                    var projectRole = projectIsTest ? SolutionReferenceProjectRole.Test : SolutionReferenceProjectRole.Production;
                    foreach (var name in root.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        index.IndexReference(semanticModel, name, document, project.Id, project.Name, projectRole, generated, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (AnalysisFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new AnalysisFailedException($"Semantic references could not be enumerated for document '{document.Name}'.", ex);
                }
            }
        }

        if (projectCount == 0)
        {
            throw new AnalysisFailedException("Solution has no C# projects for semantic reference analysis.");
        }

        return index;
    }

    /// <summary>Gets known references and symbol-local binding uncertainty for a type or method.</summary>
    public SolutionSymbolReferenceCoverage GetCoverage(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var definition = GetKey(symbol);
        IReadOnlyList<SolutionSymbolReference> entries = references.TryGetValue(definition, out var found)
            ? Array.AsReadOnly(found
                .OrderBy(static reference => reference.SourceFilePath ?? reference.DocumentName, StringComparer.Ordinal)
                .ThenBy(static reference => reference.SourceSpan.Start)
                .ThenBy(static reference => reference.ProjectRole)
                .ThenBy(static reference => reference.ProjectName, StringComparer.Ordinal)
                .ThenBy(static reference => reference.Kind)
                .ToArray())
            : Array.Empty<SolutionSymbolReference>();
        return new SolutionSymbolReferenceCoverage(entries, uncertainSymbols.Contains(definition));
    }

    private void IndexReference(
        SemanticModel semanticModel,
        SimpleNameSyntax name,
        Document document,
        ProjectId projectId,
        string projectName,
        SolutionReferenceProjectRole projectRole,
        bool isGenerated,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(name, cancellationToken);
        var symbol = symbolInfo.Symbol;
        if (symbol is null && semanticModel.GetOperation(name, cancellationToken) is IMethodReferenceOperation methodReference)
        {
            symbol = methodReference.Method;
        }

        if (symbol is null)
        {
            foreach (var candidate in symbolInfo.CandidateSymbols)
            {
                var candidateDefinition = Normalize(candidate);
                if (candidateDefinition is INamedTypeSymbol or IMethodSymbol)
                {
                    uncertainSymbols.Add(GetKey(candidateDefinition));
                }
            }

            return;
        }

        symbol = Normalize(symbol);
        if (symbol is not (INamedTypeSymbol or IMethodSymbol))
        {
            return;
        }

        var enclosingSymbol = semanticModel.GetEnclosingSymbol(name.SpanStart, cancellationToken);
        var enclosingType = enclosingSymbol as INamedTypeSymbol ?? enclosingSymbol?.ContainingType;
        var referenceKind = symbol is IMethodSymbol && !IsInvocationTarget(name)
            ? SolutionSymbolReferenceKind.MethodGroup
            : SolutionSymbolReferenceKind.Direct;
        var reference = new SolutionSymbolReference(
            symbol,
            referenceKind,
            enclosingSymbol,
            enclosingType,
            projectId,
            projectName,
            document.Id,
            document.Name,
            document.FilePath,
            name.Span,
            projectRole,
            isGenerated,
            IsSelfReference(symbol, enclosingSymbol, enclosingType));
        Add(symbol, reference);

        var containingType = symbol switch
        {
            IMethodSymbol method => method.ContainingType,
            INamedTypeSymbol { ContainingType: { } type } => type,
            _ => null,
        };
        if (containingType is not null)
        {
            var declaringType = (INamedTypeSymbol)Normalize(containingType);
            Add(declaringType, reference with
            {
                Symbol = declaringType,
                Kind = SolutionSymbolReferenceKind.ContainingType,
                IsSelfReference = IsSelfReference(declaringType, enclosingSymbol, enclosingType),
            });
        }
    }

    private void Add(ISymbol symbol, SolutionSymbolReference reference)
    {
        var key = GetKey(symbol);
        if (!references.TryGetValue(key, out var entries))
        {
            entries = [];
            references.Add(key, entries);
        }

        entries.Add(reference);
    }

    private static string GetKey(ISymbol symbol)
    {
        symbol = Normalize(symbol);
        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol)
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return $"{symbol.ContainingAssembly?.Identity.GetDisplayName()}|{declarationId}";
    }

    private static ISymbol Normalize(ISymbol symbol)
    {
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        if (symbol is IMethodSymbol { ReducedFrom: { } reduced })
        {
            symbol = reduced;
        }

        return symbol.OriginalDefinition;
    }

    private static bool IsSelfReference(ISymbol referencedSymbol, ISymbol? enclosingSymbol, INamedTypeSymbol? enclosingType)
    {
        if (referencedSymbol is INamedTypeSymbol)
        {
            return enclosingType is not null && GetKey(referencedSymbol) == GetKey(enclosingType);
        }

        return referencedSymbol is IMethodSymbol
            && enclosingSymbol is IMethodSymbol
            && GetKey(referencedSymbol) == GetKey(enclosingSymbol);
    }

    private static bool IsInvocationTarget(SimpleNameSyntax name) =>
        name.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression.Span.Contains(name.Span));
}

public enum SolutionReferenceProjectRole
{
    Production,
    Test,
}

public enum SolutionSymbolReferenceKind
{
    Direct,
    MethodGroup,
    ContainingType,
}

/// <summary>One semantically bound reference and its source and containment provenance.</summary>
public sealed record SolutionSymbolReference(
    ISymbol Symbol,
    SolutionSymbolReferenceKind Kind,
    ISymbol? EnclosingSymbol,
    INamedTypeSymbol? EnclosingType,
    ProjectId ProjectId,
    string ProjectName,
    DocumentId DocumentId,
    string DocumentName,
    string? SourceFilePath,
    TextSpan SourceSpan,
    SolutionReferenceProjectRole ProjectRole,
    bool IsGeneratedCode,
    bool IsSelfReference);

/// <summary>Known references plus uncertainty local to one symbol.</summary>
public sealed class SolutionSymbolReferenceCoverage
{
    internal SolutionSymbolReferenceCoverage(IReadOnlyList<SolutionSymbolReference> references, bool hasUnresolvedBindings)
    {
        References = references;
        HasUnresolvedBindings = hasUnresolvedBindings;
    }

    public IReadOnlyList<SolutionSymbolReference> References { get; }

    public bool HasUnresolvedBindings { get; }
}
