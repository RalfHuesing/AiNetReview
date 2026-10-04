namespace AiNetReview.FastTests;

using System;
using System.Collections.Generic;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>FastTests adapter that supplies the repository's filtered references and review context.</summary>
internal sealed class FastTestWorkspace : IDisposable
{
    private readonly TestWorkspaceBuilder builder;

    public FastTestWorkspace(string? rootPath = null) => builder = new TestWorkspaceBuilder(rootPath);

    public string RootPath => builder.RootPath;

    public AdhocWorkspace Workspace => builder.Workspace;

    public Solution Solution => builder.Solution;

    public string GetProjectFilePath(string projectName) => builder.GetProjectFilePath(projectName);

    public string GetDocumentFilePath(string documentName) => builder.GetDocumentFilePath(documentName);

    public ProjectId AddProject(
        string name,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null,
        bool allowUnsafe = false,
        string? projectFilePath = null,
        string? assemblyName = null,
        IEnumerable<MetadataReference>? metadataReferences = null,
        IEnumerable<ProjectReference>? projectReferences = null,
        ProjectId? projectId = null) =>
        builder.AddProject(
            name,
            metadataReferences ?? FastTestReferences.CreatePlatformReferences(),
            parseOptions,
            compilationOptions ?? new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe),
            projectFilePath,
            assemblyName,
            projectReferences,
            projectId);

    public DocumentId AddDocument(
        ProjectId projectId,
        string name,
        string source,
        string? filePath = null,
        IEnumerable<string>? folders = null) =>
        builder.AddDocument(projectId, name, source, filePath, folders);

    public void AddProjectReference(ProjectId projectId, ProjectReference projectReference) =>
        builder.AddProjectReference(projectId, projectReference);

    public ReviewContext CreateReviewContext() => new(Solution, RootPath);

    public void Dispose() => builder.Dispose();
}
