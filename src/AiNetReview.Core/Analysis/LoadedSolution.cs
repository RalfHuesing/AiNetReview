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
        IReadOnlyList<MarkupDocumentSnapshot> markupDocuments)
    {
        Workspace = workspace;
        Solution = solution;
        MarkupDocuments = markupDocuments;
    }

    public Solution Solution { get; }

    /// <summary>Gets markup content captured with the loaded solution, when requested by a configured rule.</summary>
    public IReadOnlyList<MarkupDocumentSnapshot> MarkupDocuments { get; }

    public void Dispose() => Workspace.Dispose();

    private MSBuildWorkspace Workspace { get; }
}
