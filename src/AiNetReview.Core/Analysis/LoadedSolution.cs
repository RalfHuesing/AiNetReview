namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

public sealed class LoadedSolution : IDisposable
{
    internal LoadedSolution(
        MSBuildWorkspace workspace,
        Solution solution,
        IReadOnlyList<MarkupDocumentSnapshot> markupDocuments,
        IReadOnlyList<SourceFileSnapshot> sourceFiles)
    {
        Workspace = workspace;
        Solution = solution;
        MarkupDocuments = markupDocuments;
        SourceFiles = sourceFiles;
    }

    public Solution Solution { get; }

    /// <summary>Gets markup content captured with the loaded solution, when requested by a configured analysis.</summary>
    public IReadOnlyList<MarkupDocumentSnapshot> MarkupDocuments { get; }

    /// <summary>Gets every source file in this configured analysis snapshot, including files without findings.</summary>
    public IReadOnlyList<SourceFileSnapshot> SourceFiles { get; }

    public void Dispose() => Workspace.Dispose();

    private MSBuildWorkspace Workspace { get; }
}
