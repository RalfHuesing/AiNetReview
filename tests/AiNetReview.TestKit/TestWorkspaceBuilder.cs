namespace AiNetReview.TestKit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

/// <summary>Builds a lightweight Roslyn solution without creating source files on disk.</summary>
public sealed class TestWorkspaceBuilder : IDisposable
{
    private readonly AdhocWorkspace workspace = new();

    public TestWorkspaceBuilder(string? rootPath = null)
    {
        RootPath = Path.GetFullPath(rootPath ?? Path.Combine(
            Path.GetTempPath(), "AiNetReview-TestWorkspace", Guid.NewGuid().ToString("N")));
    }

    /// <summary>Gets the synthetic absolute root used for project and document paths.</summary>
    public string RootPath { get; }

    /// <summary>Gets the Roslyn workspace containing the projects built so far.</summary>
    public AdhocWorkspace Workspace => workspace;

    /// <summary>Gets the current solution snapshot.</summary>
    public Solution Solution => workspace.CurrentSolution;

    public ProjectId AddProject(
        string name,
        IEnumerable<MetadataReference> metadataReferences,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null,
        string? projectFilePath = null,
        string? assemblyName = null,
        IEnumerable<ProjectReference>? projectReferences = null,
        ProjectId? projectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(metadataReferences);

        var id = projectId ?? ProjectId.CreateNewId(name);
        var info = ProjectInfo.Create(
            id,
            VersionStamp.Create(),
            name,
            assemblyName ?? name,
            LanguageNames.CSharp,
            filePath: projectFilePath ?? GetProjectFilePath(name),
            compilationOptions: compilationOptions ?? new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: parseOptions ?? new CSharpParseOptions(LanguageVersion.Preview),
            projectReferences: projectReferences?.ToArray(),
            metadataReferences: metadataReferences.ToArray());

        if (!workspace.TryApplyChanges(workspace.CurrentSolution.AddProject(info)))
        {
            throw new InvalidOperationException($"Could not add Roslyn test project '{name}'.");
        }

        return id;
    }

    public DocumentId AddDocument(
        ProjectId projectId,
        string name,
        string source,
        string? filePath = null,
        IEnumerable<string>? folders = null)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);

        var id = DocumentId.CreateNewId(projectId, name);
        var sourceText = SourceText.From(source);
        var documentInfo = DocumentInfo.Create(
            id,
            name,
            folders: folders?.ToArray(),
            filePath: filePath ?? GetDocumentFilePath(name),
            loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Create())));

        if (!workspace.TryApplyChanges(workspace.CurrentSolution.AddDocument(documentInfo)))
        {
            throw new InvalidOperationException($"Could not add Roslyn test document '{name}'.");
        }

        return id;
    }

    public void AddProjectReference(ProjectId projectId, ProjectId referencedProjectId)
        => AddProjectReference(projectId, new ProjectReference(referencedProjectId));

    public void AddProjectReference(ProjectId projectId, ProjectReference projectReference)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(projectReference);
        if (!workspace.TryApplyChanges(workspace.CurrentSolution.AddProjectReference(
                projectId, projectReference)))
        {
            throw new InvalidOperationException($"Could not add project reference to '{projectId}'.");
        }
    }

    public string GetProjectFilePath(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        return Path.Combine(RootPath, projectName + ".csproj");
    }

    public string GetDocumentFilePath(string documentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        return Path.Combine(RootPath, documentName);
    }

    public void Dispose() => workspace.Dispose();
}
