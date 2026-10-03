namespace AiNetReview.Core.Analysis;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

/// <summary>Resolves source identities and locations for findings built from the shared type dependency graph.</summary>
internal static class TypeDependencyFindingEvidence
{
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string GetProjectPath(ReviewContext context, ProjectId projectId)
    {
        var project = context.Solution.GetProject(projectId)
            ?? throw new AnalysisFailedException("Type dependency project is unavailable.");
        return context.GetProjectRelativePath(project.FilePath
            ?? throw new AnalysisFailedException($"Type dependency project '{project.Name}' has no project path."));
    }

    public static string GetStableSymbolId(INamedTypeSymbol symbol) =>
        DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition)
        ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static async Task<(int Line, string Snippet)> GetLineAndSnippetAsync(
        ReviewContext context,
        ProjectId projectId,
        string sourcePath,
        int position,
        CancellationToken cancellationToken)
    {
        var document = FindDocument(context, projectId, sourcePath);
        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var line = source.Lines.GetLineFromPosition(position);
        return (line.LineNumber + 1, line.ToString().Trim());
    }

    public static async Task<int> GetLineAsync(
        ReviewContext context,
        ProjectId projectId,
        string sourcePath,
        int position,
        CancellationToken cancellationToken) =>
        (await GetLineAndSnippetAsync(context, projectId, sourcePath, position, cancellationToken).ConfigureAwait(false)).Line;

    private static Document FindDocument(ReviewContext context, ProjectId projectId, string sourcePath)
    {
        var project = context.Solution.GetProject(projectId)
            ?? throw new AnalysisFailedException($"Type dependency source project is unavailable for '{sourcePath}'.");
        return project.Documents.FirstOrDefault(document =>
        {
            if (string.IsNullOrWhiteSpace(document.FilePath)) return false;
            try { return PathComparer.Equals(context.GetProjectRelativePath(document.FilePath), sourcePath); }
            catch (AnalysisFailedException) { return false; }
        }) ?? throw new AnalysisFailedException($"Type dependency source document is unavailable for '{sourcePath}' in project '{project.Name}'.");
    }
}
