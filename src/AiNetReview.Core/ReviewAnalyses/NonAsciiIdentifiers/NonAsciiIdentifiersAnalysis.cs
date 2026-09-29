namespace AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>Reports identifiers that contain non-ASCII characters in production C# code.</summary>
public sealed class NonAsciiIdentifiersAnalysis : IReviewAnalysis
{
    private static readonly IReadOnlyDictionary<string, double> EmptyMetrics =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "non-ascii-identifiers",
        title: "Non-ASCII Identifiers",
        behaviorVersion: 1,
        purpose: "Flags identifiers that contain non-ASCII characters in production C# code.",
        measurement: "Inspects identifier tokens in namespaces, types (classes, records, structs, interfaces, enums), type members (methods, properties, fields, enum members), parameters, and local functions/variables. Allowed characters are a-z, A-Z, 0-9, and underscore (namespaces additionally allow dot). Verbatim identifiers with '@' prefix ignore the '@'. Test projects and generated code are skipped.",
        reviewQuestions:
        [
            "Does this identifier contain non-ASCII characters that should be replaced with ASCII characters?",
            "Would transliterating umlauts/accents or using English identifiers improve consistency and LLM tokenization?",
        ],
        options: []);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp)
            .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(static project => project.Name, StringComparer.Ordinal);
        var findings = new List<FindingDraft>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReviewSourceClassifier.IsTestProject(project) || string.IsNullOrWhiteSpace(project.FilePath))
            {
                continue;
            }

            var projectFindings = await AnalyzeProjectAsync(context, project, cancellationToken).ConfigureAwait(false);
            findings.AddRange(projectFindings);
        }

        return new ReviewAnalysisResult(findings);
    }

    private static async Task<List<FindingDraft>> AnalyzeProjectAsync(
        ReviewContext context,
        Project project,
        CancellationToken cancellationToken)
    {
        var findings = new List<FindingDraft>();
        var projectPath = context.GetProjectRelativePath(project.FilePath!);

        foreach (var document in project.Documents.OrderBy(static item => item.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(document.FilePath)
                || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null)
            {
                continue;
            }

            var sourcePath = context.GetProjectRelativePath(document.FilePath);
            var walker = new AsciiIdentifierWalker(projectPath, sourcePath, sourceText, semanticModel, cancellationToken);
            walker.Visit(root);
            findings.AddRange(walker.Findings);
        }

        return findings;
    }

    private sealed class AsciiIdentifierWalker(
        string projectPath,
        string sourcePath,
        SourceText sourceText,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) : CSharpSyntaxWalker
    {
        private readonly List<FindingDraft> findings = [];
        private readonly HashSet<string> seenFindingKeys = new(StringComparer.Ordinal);

        public IReadOnlyList<FindingDraft> Findings => findings;

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckNamespace(node.Name);
            base.VisitNamespaceDeclaration(node);
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckNamespace(node.Name);
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckTypeDeclaration(node, node.Identifier))
            {
                base.VisitClassDeclaration(node);
            }
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckTypeDeclaration(node, node.Identifier))
            {
                base.VisitRecordDeclaration(node);
            }
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckTypeDeclaration(node, node.Identifier))
            {
                base.VisitStructDeclaration(node);
            }
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckTypeDeclaration(node, node.Identifier))
            {
                base.VisitInterfaceDeclaration(node);
            }
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckTypeDeclaration(node, node.Identifier))
            {
                base.VisitEnumDeclaration(node);
            }
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return;
            }

            CheckIdentifier(node.Identifier, symbol, "enum-member", "Enum member declaration");
            base.VisitEnumMemberDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return;
            }

            CheckIdentifier(node.Identifier, symbol, "method", "Method declaration");
            base.VisitMethodDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return;
            }

            CheckIdentifier(node.Identifier, symbol, "property", "Property declaration");
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return;
            }

            if (node.Parent is VariableDeclarationSyntax varDecl && varDecl.Parent is EventFieldDeclarationSyntax)
            {
                base.VisitVariableDeclarator(node);
                return;
            }

            var isField = node.Parent is VariableDeclarationSyntax declaration && declaration.Parent is FieldDeclarationSyntax;
            var discriminator = isField ? "field" : "variable";
            var label = isField ? "Field declaration" : "Variable declaration";

            CheckIdentifier(node.Identifier, symbol, discriminator, label);
            base.VisitVariableDeclarator(node);
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return;
            }

            CheckIdentifier(node.Identifier, symbol, "parameter", "Parameter declaration");
            base.VisitParameter(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return;
            }

            CheckIdentifier(node.Identifier, symbol, "local-function", "Local function declaration");
            base.VisitLocalFunctionStatement(node);
        }

        private bool CheckTypeDeclaration(BaseTypeDeclarationSyntax node, SyntaxToken identifier)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null && ReviewSourceClassifier.IsGeneratedSymbol(symbol))
            {
                return false;
            }

            CheckIdentifier(identifier, symbol, "type", "Type declaration");
            return true;
        }

        private void CheckNamespace(NameSyntax nameSyntax)
        {
            var fullname = nameSyntax.ToString().Trim();
            if (string.IsNullOrEmpty(fullname))
            {
                return;
            }

            if (TryGetFirstNonAsciiNamespace(fullname, out var nonAscii))
            {
                var token = nameSyntax.GetFirstToken();
                var linePosition = sourceText.Lines.GetLinePosition(token.SpanStart);
                var line = linePosition.Line + 1;
                var subjectId = $"N:{fullname}";
                var discriminator = "namespace";
                var rationale = $"The namespace identifier '{fullname}' contains non-ASCII characters (e.g. '{nonAscii}'). Identifiers should use only ASCII characters (a-z, A-Z, 0-9, _) and dots for namespaces.";
                var evidence = CreateEvidence(token, line, "Namespace declaration", $"Namespace declaration '{fullname}' with non-ASCII character '{nonAscii}'.");

                AddFinding(subjectId, discriminator, line, rationale, evidence);
            }
        }

        private void CheckIdentifier(SyntaxToken identifier, ISymbol? symbol, string discriminator, string label)
        {
            var name = identifier.Text;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (TryGetFirstNonAscii(name, out var nonAscii))
            {
                var linePosition = sourceText.Lines.GetLinePosition(identifier.SpanStart);
                var line = linePosition.Line + 1;
                var subjectId = GetSubjectId(symbol, identifier, discriminator, line);
                var rationale = $"The {discriminator} identifier '{name}' contains non-ASCII characters (e.g. '{nonAscii}'). Identifiers should use only ASCII characters (a-z, A-Z, 0-9, _).";
                var evidence = CreateEvidence(identifier, line, label, $"Declaration '{name}' with non-ASCII character '{nonAscii}'.");

                AddFinding(subjectId, discriminator, line, rationale, evidence);
            }
        }

        private void AddFinding(string subjectId, string discriminator, int line, string rationale, FindingEvidence evidence)
        {
            var effectiveDiscriminator = discriminator;
            var key = $"{projectPath}\0{sourcePath}\0{subjectId}\0{effectiveDiscriminator}";
            if (!seenFindingKeys.Add(key))
            {
                effectiveDiscriminator = $"{discriminator}:{line}";
                key = $"{projectPath}\0{sourcePath}\0{subjectId}\0{effectiveDiscriminator}";
                seenFindingKeys.Add(key);
            }

            findings.Add(new FindingDraft(
                projectPath,
                sourcePath,
                subjectId,
                effectiveDiscriminator,
                line,
                rationale,
                EmptyMetrics,
                [evidence]));
        }

        private static string GetSubjectId(ISymbol? symbol, SyntaxToken identifier, string discriminator, int line)
        {
            if (symbol is not null)
            {
                if (symbol is INamespaceSymbol ns)
                {
                    return $"N:{ns.ToDisplayString()}";
                }

                if (DocumentationCommentId.CreateDeclarationId(symbol) is { Length: > 0 } docId)
                {
                    return docId;
                }

                if (symbol is IParameterSymbol parameter)
                {
                    var containerId = GetContainerId(parameter.ContainingSymbol);
                    return $"{containerId}:parameter:{identifier.Text}";
                }

                if (symbol is ILocalSymbol local)
                {
                    var containerId = GetContainerId(local.ContainingSymbol);
                    return $"{containerId}:variable:{identifier.Text}:{line}";
                }

                if (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction)
                {
                    var containerId = GetContainerId(localFunction.ContainingSymbol);
                    return $"{containerId}:local-function:{identifier.Text}:{line}";
                }

                return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return $"{discriminator}:{identifier.Text}:{line}";
        }

        private static string GetContainerId(ISymbol? container)
        {
            if (container is null)
            {
                return "global";
            }

            return DocumentationCommentId.CreateDeclarationId(container)
                ?? container.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private FindingEvidence CreateEvidence(SyntaxToken token, int line, string label, string detail)
        {
            var lineText = sourceText.Lines[line - 1].ToString();
            var lineStart = sourceText.Lines[line - 1].Start;
            var tokenStartInLine = token.SpanStart - lineStart;
            var snippet = GetSnippet(lineText, tokenStartInLine, token.Span.Length);
            return new FindingEvidence(sourcePath, line, label, detail, snippet);
        }

        private static string GetSnippet(string lineText, int tokenStart, int tokenLength)
        {
            const int maximumLength = 180;
            if (lineText.Length <= maximumLength)
            {
                return lineText.Trim();
            }

            var start = Math.Max(0, tokenStart - Math.Max(0, (maximumLength - tokenLength) / 2));
            start = Math.Min(start, lineText.Length - maximumLength);
            if (start > tokenStart)
            {
                start = tokenStart;
            }

            return lineText.Substring(start, Math.Min(maximumLength, lineText.Length - start)).Trim();
        }

        private static bool TryGetFirstNonAscii(string text, out string nonAsciiSequence)
        {
            var name = text.StartsWith('@') ? text[1..] : text;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!IsAsciiIdentifierChar(c))
                {
                    if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                    {
                        nonAsciiSequence = name.Substring(i, 2);
                    }
                    else
                    {
                        nonAsciiSequence = c.ToString();
                    }

                    return true;
                }
            }

            nonAsciiSequence = string.Empty;
            return false;
        }

        private static bool TryGetFirstNonAsciiNamespace(string fullname, out string nonAsciiSequence)
        {
            var segments = fullname.Split('.');
            foreach (var segment in segments)
            {
                if (TryGetFirstNonAscii(segment, out nonAsciiSequence))
                {
                    return true;
                }
            }

            nonAsciiSequence = string.Empty;
            return false;
        }

        private static bool IsAsciiIdentifierChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
    }
}
