namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class TypeSizeCollector
{
    public static async Task<IReadOnlyList<TypeSizeMeasurement>> CollectAsync(
        ReviewContext context,
        Project project,
        CancellationToken cancellationToken)
    {
        var partsBySymbol = new Dictionary<INamedTypeSymbol, List<TypeSizePart>>(SymbolEqualityComparer.Default);
        foreach (var item in await CodeSizeDocuments.GetUniqueNonGeneratedByPhysicalPathAsync(project, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = item.Document;

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null)
            {
                continue;
            }

            var sourcePath = context.GetProjectRelativePath(item.FilePath);
            foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Where(IsClassDeclaration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
                if (symbol is null || ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                {
                    continue;
                }

                if (!partsBySymbol.TryGetValue(symbol, out var parts))
                {
                    parts = [];
                    partsBySymbol.Add(symbol, parts);
                }

                var nameToken = declaration switch
                {
                    ClassDeclarationSyntax classDeclaration => classDeclaration.Identifier,
                    RecordDeclarationSyntax recordDeclaration => recordDeclaration.Identifier,
                    _ => throw new InvalidOperationException("Unsupported class declaration syntax.")
                };
                parts.Add(new TypeSizePart(
                    sourcePath,
                    sourceText.Lines.GetLineFromPosition(declaration.SpanStart).LineNumber + 1,
                    nameToken,
                    sourceText,
                    CodeLineMetrics.CountOwnTypePart(declaration)));
            }
        }

        return partsBySymbol.Select(pair =>
        {
            var parts = pair.Value
                .OrderBy(static part => part.SourcePath, StringComparer.Ordinal)
                .ThenBy(static part => part.StartLine)
                .ToArray();
            var subjectId = DocumentationCommentId.CreateDeclarationId(pair.Key)
                ?? pair.Key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new TypeSizeMeasurement(
                context.GetProjectRelativePath(project.FilePath!),
                subjectId,
                parts.Sum(static part => part.CodeLines),
                parts);
        })
        .OrderBy(static measurement => measurement.Parts[0].SourcePath, StringComparer.Ordinal)
        .ThenBy(static measurement => measurement.Parts[0].StartLine)
        .ThenBy(static measurement => measurement.SubjectId, StringComparer.Ordinal)
        .ToArray();
    }

    private static bool IsClassDeclaration(BaseTypeDeclarationSyntax declaration) => declaration switch
    {
        ClassDeclarationSyntax => true,
        RecordDeclarationSyntax record => !record.ClassOrStructKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructKeyword),
        _ => false,
    };
}

internal sealed record TypeSizeMeasurement(
    string ProjectPath,
    string SubjectId,
    int CodeLines,
    IReadOnlyList<TypeSizePart> Parts);

internal sealed record TypeSizePart(
    string SourcePath,
    int StartLine,
    SyntaxToken NameToken,
    SourceText SourceText,
    int CodeLines)
{
    public FindingEvidence ToEvidence() => new(
        SourcePath,
        SourceText.Lines.GetLineFromPosition(NameToken.SpanStart).LineNumber + 1,
        "Class declaration part",
        "This loaded source line identifies one part contributing to the measured type.",
        SourceText.Lines.GetLineFromPosition(NameToken.SpanStart).ToString().Trim(),
        OmitWhenRedundantWithSubject: true);
}
