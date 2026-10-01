namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>Immutable source and semantic context prepared from the solution already loaded for a review.</summary>
internal sealed class AuditSourceContext
{
    private AuditSourceContext(
        IReadOnlyList<AuditSourceProject> projects,
        IReadOnlyList<AuditSourceFile> files,
        IReadOnlyList<AuditSourceType> types,
        IReadOnlyList<AuditSourceSubject> subjects,
        IReadOnlyList<AuditSourceReference> references,
        IReadOnlyList<AuditSourceUncertainty> uncertainties)
    {
        Projects = projects;
        Files = files;
        Types = types;
        Subjects = subjects;
        References = references;
        Uncertainties = uncertainties;
    }

    public IReadOnlyList<AuditSourceProject> Projects { get; }

    public IReadOnlyList<AuditSourceFile> Files { get; }

    public IReadOnlyList<AuditSourceType> Types { get; }

    public IReadOnlyList<AuditSourceSubject> Subjects { get; }

    public IReadOnlyList<AuditSourceReference> References { get; }

    public IReadOnlyList<AuditSourceUncertainty> Uncertainties { get; }

    public static async Task<AuditSourceContext> CreateAsync(ReviewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var referenceIndex = await SolutionReferenceIndex.CreateAsync(context, cancellationToken).ConfigureAwait(false);
        var projects = new List<AuditSourceProject>();
        var files = new List<AuditSourceFile>();
        var pendingSubjects = new List<AuditSourceSubject>();
        var typeDeclarations = new Dictionary<string, MutableSourceType>(StringComparer.Ordinal);
        var projectTypes = new Dictionary<ProjectId, List<(INamedTypeSymbol Symbol, string Id)>>();
        var projectPaths = new Dictionary<ProjectId, string>();
        var fileById = new Dictionary<(ProjectId Project, DocumentId Document), AuditSourceFile>();
        var sourceTexts = new Dictionary<(ProjectId Project, DocumentId Document), SourceText>();

        foreach (var project in context.Solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw new AnalysisFailedException($"A C# project has no project file path: '{project.Name}'.");
            }

            var classification = ReviewSourceClassifier.ClassifyProject(project);
            var projectPath = context.GetProjectRelativePath(project.FilePath);
            projects.Add(new AuditSourceProject(
                project.Id,
                projectPath,
                project.Name,
                classification.Role,
                classification.Reason));
            projectPaths.Add(project.Id, projectPath);
            projectTypes.Add(project.Id, []);

            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(document.FilePath))
                {
                    throw new AnalysisFailedException($"A C# source document has no physical path: '{document.Name}'.");
                }

                var filePath = context.GetProjectRelativePath(document.FilePath);
                var generated = await ReviewSourceClassifier.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                var file = new AuditSourceFile(project.Id, document.Id, filePath, generated);
                files.Add(file);
                fileById.Add((project.Id, document.Id), file);

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Semantic model could not be read for document '{document.Name}'.");
                var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                sourceTexts.Add((project.Id, document.Id), sourceText);
                foreach (var declaration in root.DescendantNodes().Where(static node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var declaredSymbol = declaration switch
                    {
                        BaseTypeDeclarationSyntax typeDeclaration => semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken),
                        DelegateDeclarationSyntax delegateDeclaration => semanticModel.GetDeclaredSymbol(delegateDeclaration, cancellationToken),
                        _ => null,
                    };
                    if (declaredSymbol is not INamedTypeSymbol declaredType)
                    {
                        continue;
                    }

                    declaredType = declaredType.OriginalDefinition;
                    var id = GetTypeId(projectPath, declaredType);
                    var outerId = declaredType;
                    while (outerId.ContainingType is not null)
                    {
                        outerId = outerId.ContainingType;
                    }

                    var outerTypeId = GetTypeId(projectPath, outerId.OriginalDefinition);
                    if (!typeDeclarations.TryGetValue(id, out var sourceType))
                    {
                        sourceType = new MutableSourceType(
                            project.Id,
                            id,
                            outerTypeId,
                            declaredType.Name,
                            declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            ReviewSourceClassifier.IsGeneratedSymbol(declaredType),
                            declaredType);
                        typeDeclarations.Add(id, sourceType);
                        projectTypes[project.Id].Add((declaredType, id));
                    }

                    sourceType.HasNonGeneratedDeclaration |= !generated;
                    sourceType.Declarations.Add(CreateLocation(filePath, declaration.Span, sourceText));
                }

                foreach (var node in root.DescendantNodesAndSelf().Where(static node => node is BaseTypeDeclarationSyntax
                    or DelegateDeclarationSyntax or BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax
                    or EventFieldDeclarationSyntax or FieldDeclarationSyntax or EnumMemberDeclarationSyntax
                    or LocalFunctionStatementSyntax or VariableDeclaratorSyntax))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var symbol = node is VariableDeclaratorSyntax variable
                        ? semanticModel.GetDeclaredSymbol(variable, cancellationToken)
                        : semanticModel.GetDeclaredSymbol(node, cancellationToken);
                    if (symbol is null || DocumentationCommentId.CreateDeclarationId(symbol) is not { Length: > 0 } declarationId)
                    {
                        continue;
                    }

                    var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
                    if (containingType is null)
                    {
                        continue;
                    }

                    var outermost = GetOutermostType(containingType);
                    pendingSubjects.Add(new AuditSourceSubject(project.Id, filePath, declarationId, GetTypeId(projectPath, outermost)));
                }
            }
        }

        var sourceTypeIds = typeDeclarations.Keys.ToHashSet(StringComparer.Ordinal);
        var subjects = pendingSubjects.Where(subject => sourceTypeIds.Contains(subject.OutermostTypeId)
                && !typeDeclarations[subject.OutermostTypeId].IsGenerated)
            .DistinctBy(static subject => (subject.ProjectId, subject.SourcePath, subject.SymbolId))
            .OrderBy(subject => projectPaths[subject.ProjectId], StringComparer.Ordinal)
            .ThenBy(static subject => subject.SourcePath, StringComparer.Ordinal)
            .ThenBy(static subject => subject.SymbolId, StringComparer.Ordinal).ToArray();

        var sourceTypesBySymbol = typeDeclarations.Values.Where(static item => !item.IsGenerated)
            .GroupBy(static item => GetSymbolId(item.Symbol), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var projectsById = projects.ToDictionary(static item => item.ProjectId);
        var references = new Dictionary<string, AuditSourceReference>(StringComparer.Ordinal);
        var referenceUncertainties = new List<AuditSourceUncertainty>();
        foreach (var declaredTypes in projectTypes.Values)
        {
            foreach (var (typeSymbol, _) in declaredTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCoverage(typeSymbol);
                foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    AddCoverage(method);
                }
            }

            void AddCoverage(ISymbol symbol)
            {
                var coverage = referenceIndex.GetCoverage(symbol);
                foreach (var reference in coverage.References)
                {
                    if (reference.IsGeneratedCode
                        || reference.EnclosingSymbol is { } enclosingSymbol && ReviewSourceClassifier.IsGeneratedSymbol(enclosingSymbol))
                    {
                        continue;
                    }

                    var sourceTypeId = reference.EnclosingType is null
                        ? null
                        : GetTypeId(projectPaths[reference.ProjectId], GetOutermostType(reference.EnclosingType));
                    if (sourceTypeId is not null && !sourceTypeIds.Contains(sourceTypeId))
                    {
                        sourceTypeId = null;
                    }
                    else if (sourceTypeId is not null && typeDeclarations[sourceTypeId].IsGenerated)
                    {
                        continue;
                    }

                    if (!fileById.TryGetValue((reference.ProjectId, reference.DocumentId), out var sourceFile)
                        || sourceFile.IsGenerated)
                    {
                        continue;
                    }

                    var targetType = reference.Symbol switch
                    {
                        IMethodSymbol method => method.ContainingType,
                        INamedTypeSymbol namedType => namedType,
                        _ => null,
                    };
                    if (targetType is null)
                    {
                        continue;
                    }

                    targetType = GetOutermostType(targetType.OriginalDefinition);
                    var targetMatches = FindTargetTypeIds(targetType, sourceTypesBySymbol);
                    if (targetMatches.Length > 1)
                    {
                        referenceUncertainties.Add(new AuditSourceUncertainty(
                            reference.ProjectId,
                            sourceFile.Path,
                            sourceTypeId,
                            projectsById[reference.ProjectId].Role,
                            null,
                            GetSymbolId(reference.Symbol),
                            "TargetProjectAmbiguous",
                            CreateLocation(sourceFile.Path, reference.SourceSpan, sourceTexts[(reference.ProjectId, reference.DocumentId)])));
                        continue;
                    }

                    if (targetMatches.Length == 0)
                    {
                        continue;
                    }

                    var targetId = targetMatches[0];
                    var target = typeDeclarations[targetId];
                    if (target.IsGenerated || ReviewSourceClassifier.IsGeneratedSymbol(reference.Symbol))
                    {
                        continue;
                    }

                    var sourceRole = projectsById[reference.ProjectId].Role;
                    var targetRole = projectsById[target.ProjectId].Role;
                    var memberId = reference.Symbol is IMethodSymbol
                        ? GetSymbolId(reference.Symbol)
                        : null;
                    var mapped = new AuditSourceReference(
                        reference.ProjectId,
                        sourceFile.Path,
                        sourceTypeId,
                        sourceRole,
                        target.ProjectId,
                        targetId,
                        memberId,
                        targetRole,
                        reference.Kind,
                        CreateLocation(sourceFile.Path, reference.SourceSpan, sourceTexts[(reference.ProjectId, reference.DocumentId)]));
                    var referenceKey = ReferenceKey(mapped);
                    if (!references.TryGetValue(referenceKey, out var previous)
                        || CompareReferencePreference(mapped, previous) > 0)
                    {
                        references[referenceKey] = mapped;
                    }
                }
            }
        }

        var uncertainties = new List<AuditSourceUncertainty>();
        foreach (var uncertainty in referenceIndex.GetUncertainties())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (uncertainty.IsGeneratedCode
                || uncertainty.EnclosingSymbol is { } enclosingSymbol && ReviewSourceClassifier.IsGeneratedSymbol(enclosingSymbol)
                || !fileById.TryGetValue((uncertainty.ProjectId, uncertainty.DocumentId), out var sourceFile)
                || sourceFile.IsGenerated)
            {
                continue;
            }

            string? originTypeId = null;
            if (uncertainty.EnclosingType is not null)
            {
                var originType = GetOutermostType(uncertainty.EnclosingType);
                originTypeId = GetTypeId(projectPaths[uncertainty.ProjectId], originType);
                if (!sourceTypeIds.Contains(originTypeId))
                {
                    originTypeId = null;
                }
                else if (typeDeclarations[originTypeId].IsGenerated)
                {
                    continue;
                }
            }

            var candidate = uncertainty.CandidateSymbol switch
            {
                IMethodSymbol method => method.ContainingType,
                INamedTypeSymbol namedType => namedType,
                _ => null,
            };
            var candidateMatches = candidate is null
                ? []
                : FindTargetTypeIds(GetOutermostType(candidate.OriginalDefinition), sourceTypesBySymbol);
            var candidateTypeId = candidateMatches.Length == 1 ? candidateMatches[0] : null;
            uncertainties.Add(new AuditSourceUncertainty(
                uncertainty.ProjectId,
                sourceFile.Path,
                originTypeId,
                projectsById[uncertainty.ProjectId].Role,
                candidateTypeId,
                GetSymbolId(uncertainty.CandidateSymbol),
                "UnresolvedBinding",
                CreateLocation(sourceFile.Path, uncertainty.SourceSpan, sourceTexts[(uncertainty.ProjectId, uncertainty.DocumentId)])));
        }
        uncertainties.AddRange(referenceUncertainties);

        cancellationToken.ThrowIfCancellationRequested();
        return new AuditSourceContext(
            Array.AsReadOnly(projects.OrderBy(static item => item.ProjectPath, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(files.OrderBy(item => projectPaths[item.ProjectId], StringComparer.Ordinal).ThenBy(static item => item.Path, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(typeDeclarations.Values.Select(static item => item.Freeze())
                .OrderBy(item => projectPaths[item.ProjectId], StringComparer.Ordinal)
                .ThenBy(static item => item.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(static item => item.Id, StringComparer.Ordinal)
                .ToArray()),
            Array.AsReadOnly(subjects),
            Array.AsReadOnly(references.Values.OrderBy(item => projectPaths[item.SourceProjectId], StringComparer.Ordinal)
                .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.StartLine)
                .ThenBy(static item => item.Location.StartColumn)
                .ThenBy(static item => item.Location.Span.Start)
                .ThenBy(static item => item.TargetTypeId, StringComparer.Ordinal)
                .ThenBy(static item => item.TargetMemberId, StringComparer.Ordinal)
                .ToArray()),
            Array.AsReadOnly(uncertainties.OrderBy(item => projectPaths[item.SourceProjectId], StringComparer.Ordinal)
                .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.StartLine)
                .ThenBy(static item => item.Location.StartColumn)
                .ThenBy(static item => item.Location.Span.Start)
                .ThenBy(static item => item.CandidateSymbolId, StringComparer.Ordinal)
                .ToArray()));
    }

    private static AuditSourceLocation CreateLocation(string path, TextSpan span, SourceText sourceText)
    {
        var start = sourceText.Lines.GetLinePosition(span.Start);
        var end = sourceText.Lines.GetLinePosition(span.End);
        return new AuditSourceLocation(path, span, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1);
    }

    private static string GetTypeId(string projectPath, INamedTypeSymbol symbol) => $"{projectPath}|{GetSymbolId(symbol)}";

    private static string GetSymbolId(ISymbol symbol)
    {
        var normalized = symbol is IAliasSymbol alias ? alias.Target : symbol;
        if (normalized is IMethodSymbol { ReducedFrom: { } reduced })
        {
            normalized = reduced;
        }

        normalized = normalized.OriginalDefinition;
        return $"{normalized.ContainingAssembly?.Identity.GetDisplayName()}|{DocumentationCommentId.CreateDeclarationId(normalized) ?? normalized.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
    }

    private static INamedTypeSymbol GetOutermostType(INamedTypeSymbol symbol)
    {
        while (symbol.ContainingType is not null)
        {
            symbol = symbol.ContainingType;
        }

        return symbol.OriginalDefinition;
    }

    private static string[] FindTargetTypeIds(INamedTypeSymbol target, IReadOnlyDictionary<string, MutableSourceType[]> sourceTypesBySymbol)
    {
        return sourceTypesBySymbol.TryGetValue(GetSymbolId(target), out var matches)
            ? matches
            .Select(static item => item.Id)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()
            : [];
    }

    private static string ReferenceKey(AuditSourceReference reference) =>
        $"{reference.SourceProjectId.Id:N}|{reference.SourcePath}|{reference.Location.Span.Start}:{reference.Location.Span.End}|{reference.TargetProjectId.Id:N}|{reference.TargetTypeId}";

    private static int CompareReferencePreference(AuditSourceReference candidate, AuditSourceReference current)
    {
        if ((candidate.TargetMemberId is null) != (current.TargetMemberId is null))
        {
            return candidate.TargetMemberId is null ? -1 : 1;
        }

        var memberComparison = string.Compare(candidate.TargetMemberId, current.TargetMemberId, StringComparison.Ordinal);
        if (memberComparison != 0)
        {
            return memberComparison;
        }

        var candidateRank = candidate.Kind == SolutionSymbolReferenceKind.ContainingType ? 0 : 1;
        var currentRank = current.Kind == SolutionSymbolReferenceKind.ContainingType ? 0 : 1;
        return candidateRank != currentRank ? candidateRank.CompareTo(currentRank) : candidate.Kind.CompareTo(current.Kind);
    }

    private sealed class MutableSourceType(
        ProjectId projectId,
        string id,
        string outerTypeId,
        string name,
        string fullyQualifiedName,
        bool symbolIsGenerated,
        INamedTypeSymbol symbol)
    {
        public ProjectId ProjectId { get; } = projectId;

        public string Id { get; } = id;

        public string OuterTypeId { get; } = outerTypeId;

        public string Name { get; } = name;

        public string FullyQualifiedName { get; } = fullyQualifiedName;

        public bool IsGenerated => symbolIsGenerated || !HasNonGeneratedDeclaration;

        public bool HasNonGeneratedDeclaration { get; set; }

        public INamedTypeSymbol Symbol { get; } = symbol;

        public List<AuditSourceLocation> Declarations { get; } = [];

        public AuditSourceType Freeze() => new(
            ProjectId,
            Id,
            OuterTypeId,
            Name,
            FullyQualifiedName,
            IsGenerated,
            Array.AsReadOnly(Declarations.Distinct().OrderBy(static location => location.Path, StringComparer.Ordinal)
                .ThenBy(static location => location.StartLine).ThenBy(static location => location.StartColumn).ToArray()));
    }
}

internal sealed record AuditSourceProject(
    ProjectId ProjectId,
    string ProjectPath,
    string ProjectName,
    ProjectRole Role,
    ProjectClassificationReason ClassificationReason);

internal sealed record AuditSourceFile(ProjectId ProjectId, DocumentId DocumentId, string Path, bool IsGenerated);

internal sealed record AuditSourceType(
    ProjectId ProjectId,
    string Id,
    string OuterTypeId,
    string Name,
    string FullyQualifiedName,
    bool IsGenerated,
    IReadOnlyList<AuditSourceLocation> Declarations);

internal sealed record AuditSourceSubject(ProjectId ProjectId, string SourcePath, string SymbolId, string OutermostTypeId);

internal sealed record AuditSourceLocation(string Path, TextSpan Span, int StartLine, int StartColumn, int EndLine, int EndColumn);

internal sealed record AuditSourceReference(
    ProjectId SourceProjectId,
    string SourcePath,
    string? SourceTypeId,
    ProjectRole SourceRole,
    ProjectId TargetProjectId,
    string TargetTypeId,
    string? TargetMemberId,
    ProjectRole TargetRole,
    SolutionSymbolReferenceKind Kind,
    AuditSourceLocation Location);

internal sealed record AuditSourceUncertainty(
    ProjectId SourceProjectId,
    string SourcePath,
    string? OriginTypeId,
    ProjectRole SourceRole,
    string? CandidateTypeId,
    string CandidateSymbolId,
    string Reason,
    AuditSourceLocation Location);
