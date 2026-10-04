namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>Builds deterministic map data from one loaded solution snapshot.</summary>
internal static class ReviewMapBuilder
{
    private const string GlobalNamespace = "<global>";
    private static readonly SymbolDisplayFormat MapTypeNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static async Task<ReviewMaps> BuildAsync(ReviewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp)
            .Select(project => CreateProjectInfo(context, project))
            .OrderBy(static project => project.ProjectPath, StringComparer.Ordinal)
            .ToArray();
        var projectById = projects.ToDictionary(static project => project.Project.Id);
        var projectByKey = projects.ToDictionary(static project => project.Key, StringComparer.Ordinal);
        var projectMaps = projects.Select(project => new ReviewMapProject(
                project.Key,
                project.ProjectPath,
                project.Role,
                project.Reason,
                Array.AsReadOnly(project.Project.ProjectReferences
                    .Select(reference => context.Solution.GetProject(reference.ProjectId))
                    .Where(target => target?.Language == LanguageNames.CSharp && target.Id != project.Project.Id)
                    .Select(target => projectById[target!.Id])
                    .OrderBy(static target => target.ProjectPath, StringComparer.Ordinal)
                    .Select(target => new ReviewMapProjectReference(target.Key, target.ProjectPath, target.Role))
                    .ToArray())))
            .ToArray();

        if (projects.Length == 0)
        {
            return new ReviewMaps(
                Array.AsReadOnly(projectMaps),
                Array.Empty<ReviewMapSourceFile>(),
                Array.Empty<ReviewMapType>(),
                Array.Empty<ReviewMapTypeEdge>());
        }

        var snapshots = new Dictionary<(ProjectId ProjectId, string Path), SourceFileSnapshot>();
        var sourceFiles = new List<ReviewMapSourceFile>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in project.Project.Documents
                         .Where(IsCSharpDocument)
                         .OrderBy(static document => document.FilePath ?? document.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                SourceText text;
                SyntaxNode root;
                SemanticModel semanticModel;
                try
                {
                    text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new AnalysisFailedException($"Source map could not read syntax for '{document.FilePath ?? document.Name}'.");
                    semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new AnalysisFailedException($"Source map could not read semantic model for '{document.FilePath ?? document.Name}'.");
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
                    throw new AnalysisFailedException($"Source map could not read '{document.FilePath ?? document.Name}'.", exception);
                }

                var relativePath = GetSourcePath(context, document, project.Project);
                var namespaceNameSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var namespaceDeclaration in root.DescendantNodesAndSelf().OfType<BaseNamespaceDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(namespaceDeclaration, cancellationToken) is not INamespaceSymbol namespaceSymbol)
                    {
                        throw new AnalysisFailedException($"Source map could not bind namespace declaration in '{relativePath}'.");
                    }

                    namespaceNameSet.Add(namespaceSymbol.IsGlobalNamespace ? GlobalNamespace : namespaceSymbol.ToDisplayString());
                }

                foreach (var typeDeclaration in root.DescendantNodesAndSelf()
                             .Where(static node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                {
                    if (semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is not INamedTypeSymbol typeSymbol)
                    {
                        throw new AnalysisFailedException($"Source map could not bind type declaration in '{relativePath}'.");
                    }

                    if (typeSymbol.ContainingNamespace.IsGlobalNamespace)
                    {
                        namespaceNameSet.Add(GlobalNamespace);
                    }
                }
                var namespaceNames = namespaceNameSet.OrderBy(static name => name, StringComparer.Ordinal).ToArray();

                var snapshot = new SourceFileSnapshot(project.Project.Id, project.Key, relativePath, text);
                snapshots.Add((project.Project.Id, relativePath), snapshot);
                sourceFiles.Add(new ReviewMapSourceFile(
                    project.Key,
                    relativePath,
                    Encoding.UTF8.GetByteCount(text.ToString()),
                    text.Lines.Count,
                    Array.AsReadOnly(namespaceNames),
                    Array.Empty<string>()));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var graph = await context.GetTypeDependencyGraphAsync(cancellationToken).ConfigureAwait(false);
        var typeInfo = graph.Nodes.ToDictionary(
            node => GetTypeKey(node.ProjectId, node.Symbol),
            node => new TypeMapInfo(node, CreateTypeId(projectById[node.ProjectId].Key, node.Symbol)));
        var declarationFileTypes = new Dictionary<(ProjectId ProjectId, string Path), HashSet<string>>();
        var mapTypes = new List<ReviewMapType>(graph.Nodes.Count);
        foreach (var node in graph.Nodes.OrderBy(static node => node.StableId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = projectById[node.ProjectId];
            var info = typeInfo[GetTypeKey(node.ProjectId, node.Symbol)];
            var declarations = node.Declarations
                .OrderBy(static declaration => declaration.SourcePath, StringComparer.Ordinal)
                .ThenBy(static declaration => declaration.Span.Start)
                .Select(declaration =>
                {
                    if (!snapshots.TryGetValue((declaration.ProjectId, declaration.SourcePath), out var snapshot))
                    {
                        throw new AnalysisFailedException($"Source map could not match declaration '{node.Symbol.ToDisplayString()}' to loaded source '{declaration.SourcePath}'.");
                    }

                    var line = snapshot.Text.Lines.GetLineFromPosition(declaration.Span.Start).LineNumber + 1;
                    var key = (declaration.ProjectId, declaration.SourcePath);
                    if (!declarationFileTypes.TryGetValue(key, out var ids))
                    {
                        ids = new HashSet<string>(StringComparer.Ordinal);
                        declarationFileTypes.Add(key, ids);
                    }
                    ids.Add(info.Id);
                    return new ReviewMapTypeDeclaration(declaration.SourcePath, line);
                })
                .ToArray();
            var containingNamespace = node.Symbol.ContainingNamespace;
            var namespaceName = containingNamespace is null || containingNamespace.IsGlobalNamespace
                ? GlobalNamespace
                : containingNamespace.ToDisplayString();
            mapTypes.Add(new ReviewMapType(
                info.Id,
                project.Key,
                namespaceName,
                node.Symbol.ToDisplayString(MapTypeNameFormat),
                node.Symbol.IsRecord
                    ? node.Symbol.IsValueType ? "RecordStruct" : "Record"
                    : node.Symbol.TypeKind.ToString(),
                node.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Array.AsReadOnly(declarations)));
        }

        var completedFiles = sourceFiles.Select(file =>
        {
            var project = projectByKey[file.ProjectKey];
            var snapshot = snapshots[(project.Project.Id, file.RelativePath)];
            var typeIds = declarationFileTypes.TryGetValue((snapshot.ProjectId, file.RelativePath), out var ids)
                ? ids.Order(StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            return file with { TypeIds = Array.AsReadOnly(typeIds) };
        }).ToArray();

        var mapEdges = graph.Edges
            .Select(edge =>
            {
                var fromInfo = typeInfo[GetTypeKey(edge.From.ProjectId, edge.From.Symbol)];
                var toInfo = typeInfo[GetTypeKey(edge.To.ProjectId, edge.To.Symbol)];
                var witnesses = edge.Witnesses
                    .OrderBy(static witness => witness.ProjectPath, StringComparer.Ordinal)
                    .ThenBy(static witness => witness.SourcePath, StringComparer.Ordinal)
                    .ThenBy(static witness => witness.Span.Start)
                    .ThenBy(static witness => witness.Kind)
                    .Select(witness =>
                    {
                        if (!snapshots.TryGetValue((witness.ProjectId, witness.SourcePath), out var snapshot))
                        {
                            throw new AnalysisFailedException($"Source map could not match dependency witness to loaded source '{witness.SourcePath}'.");
                        }

                        return new ReviewMapTypeWitness(
                            snapshot.ProjectKey,
                            witness.SourcePath,
                            snapshot.Text.Lines.GetLineFromPosition(witness.Span.Start).LineNumber + 1,
                            witness.Kind.ToString());
                    })
                    .ToArray();
                return new ReviewMapTypeEdge(
                    fromInfo.Id,
                    toInfo.Id,
                    edge.From.IsTestProject,
                    Array.AsReadOnly(witnesses));
            })
            .OrderBy(static edge => edge.FromTypeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.ToTypeId, StringComparer.Ordinal)
            .ToArray();

        return new ReviewMaps(
            Array.AsReadOnly(projectMaps),
            Array.AsReadOnly(completedFiles.OrderBy(static file => file.ProjectKey, StringComparer.Ordinal)
                .ThenBy(static file => file.RelativePath, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(mapTypes.OrderBy(static type => type.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(mapEdges));
    }

    private static ProjectMapInfo CreateProjectInfo(ReviewContext context, Project project)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            throw new AnalysisFailedException($"Source maps could not be built because C# project '{project.Name}' has no project file path.");
        }

        var path = context.GetProjectRelativePath(project.FilePath);
        var classification = ReviewSourceClassifier.ClassifyProject(project);
        return new ProjectMapInfo(project, CreateProjectKey(path), path, classification.Role, classification.Reason);
    }

    private static bool IsCSharpDocument(Document document) =>
        document.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || (document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string GetSourcePath(ReviewContext context, Document document, Project project) =>
        string.IsNullOrWhiteSpace(document.FilePath)
            ? throw new AnalysisFailedException($"Source maps could not build a stable source path for '{project.Name}/{document.Name}': the loaded document has no file path.")
            : context.GetProjectRelativePath(document.FilePath);

    private static string CreateProjectKey(string projectPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(projectPath);
        var slugBuilder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            slugBuilder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        var slug = slugBuilder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            slug = "project";
        }
        if (slug.Length > 32)
        {
            slug = slug[..32];
        }

        return $"p-{slug}-{Digest(projectPath, 8)}";
    }

    private static string CreateTypeId(string projectKey, INamedTypeSymbol symbol) =>
        $"t-{Digest($"{projectKey}:{GetTypeDocumentationId(symbol)}", 16)}";

    private static string GetTypeKey(ProjectId projectId, INamedTypeSymbol symbol) =>
        $"{projectId.Id:N}:{GetTypeDocumentationId(symbol)}";

    private static string GetTypeDocumentationId(INamedTypeSymbol symbol) =>
        DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition)
        ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Digest(string value, int length) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..length];

    private sealed record ProjectMapInfo(
        Project Project,
        string Key,
        string ProjectPath,
        ProjectRole Role,
        ProjectClassificationReason Reason);

    private sealed record SourceFileSnapshot(ProjectId ProjectId, string ProjectKey, string RelativePath, SourceText Text);

    private sealed record TypeMapInfo(TypeDependencyNode Node, string Id);
}
