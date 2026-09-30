namespace AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class ExecutableMemberCollector
{
    public static async Task<IReadOnlyList<ExecutableMemberMeasurement>> CollectAsync(
        ReviewContext context,
        Project project,
        CancellationToken cancellationToken)
    {
        var measurements = new List<ExecutableMemberMeasurement>();
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

            foreach (var declaration in root.DescendantNodes().Where(IsSupportedDeclaration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var body = GetExecutableBody(declaration);
                var codeLines = CodeLineMetrics.CountExecutableDeclaration(declaration);
                if (body is null || codeLines == 0)
                {
                    continue;
                }

                var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
                if (symbol is null || ReviewSourceClassifier.IsGeneratedSymbol(symbol))
                {
                    continue;
                }

                var metrics = ControlFlowMetrics.Measure(body);
                var sourcePath = context.GetProjectRelativePath(item.FilePath);
                var projectPath = context.GetProjectRelativePath(project.FilePath!);
                var startLine = sourceText.Lines.GetLineFromPosition(declaration.SpanStart).LineNumber + 1;
                var subjectId = DocumentationCommentId.CreateDeclarationId(symbol)
                    ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var evidence = CreateEvidence(sourceText, GetEvidenceToken(declaration));

                measurements.Add(new ExecutableMemberMeasurement(
                    projectPath,
                    sourcePath,
                    subjectId,
                    startLine,
                    codeLines,
                    metrics.DecisionCount,
                    metrics.DecisionConstructCount,
                    metrics.MaxDecisionNesting,
                    evidence));
            }
        }

        return measurements;
    }

    private static bool IsSupportedDeclaration(SyntaxNode node) => node is
        MethodDeclarationSyntax
        or ConstructorDeclarationSyntax
        or AccessorDeclarationSyntax
        or OperatorDeclarationSyntax
        or ConversionOperatorDeclarationSyntax
        or PropertyDeclarationSyntax
        or IndexerDeclarationSyntax;

    private static SyntaxNode? GetExecutableBody(SyntaxNode declaration) => declaration switch
    {
        MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
        ConstructorDeclarationSyntax constructor => (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody?.Expression,
        AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
        OperatorDeclarationSyntax op => (SyntaxNode?)op.Body ?? op.ExpressionBody?.Expression,
        ConversionOperatorDeclarationSyntax conversion => (SyntaxNode?)conversion.Body ?? conversion.ExpressionBody?.Expression,
        PropertyDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
        IndexerDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
        _ => null,
    };

    private static SyntaxToken GetEvidenceToken(SyntaxNode declaration) => declaration switch
    {
        MethodDeclarationSyntax method => method.Identifier,
        ConstructorDeclarationSyntax constructor => constructor.Identifier,
        AccessorDeclarationSyntax accessor => accessor.Keyword,
        OperatorDeclarationSyntax op => op.OperatorToken,
        ConversionOperatorDeclarationSyntax conversion => conversion.OperatorKeyword,
        PropertyDeclarationSyntax property => property.Identifier,
        IndexerDeclarationSyntax indexer => indexer.ThisKeyword,
        _ => throw new ArgumentException("Unsupported executable member declaration.", nameof(declaration)),
    };

    private static MemberSourceEvidence CreateEvidence(SourceText sourceText, SyntaxToken token)
    {
        var lineIndex = sourceText.Lines.GetLineFromPosition(token.SpanStart).LineNumber;
        var line = sourceText.Lines[lineIndex];
        var lineText = line.ToString();
        if (lineText.Length <= 180)
        {
            return new MemberSourceEvidence(lineIndex + 1, lineText.Trim());
        }

        const int maximumSnippetLength = 180;
        var tokenStart = token.SpanStart - line.Start;
        var tokenEnd = token.Span.End - line.Start;
        var tokenLength = tokenEnd - tokenStart;
        var snippetStart = Math.Max(0, tokenStart - Math.Max(0, (maximumSnippetLength - tokenLength) / 2));
        snippetStart = Math.Min(snippetStart, lineText.Length - maximumSnippetLength);
        if (snippetStart > tokenStart)
        {
            snippetStart = tokenStart;
        }

        var snippetEnd = Math.Min(lineText.Length, Math.Max(snippetStart + maximumSnippetLength, tokenEnd));
        if (snippetEnd - snippetStart > maximumSnippetLength)
        {
            snippetStart = snippetEnd - maximumSnippetLength;
        }

        return new MemberSourceEvidence(lineIndex + 1, lineText[snippetStart..snippetEnd].Trim());
    }
}
