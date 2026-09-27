namespace AiNetReview.Core.Analysis;

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

public sealed class LoadedSolution : IDisposable
{
    internal LoadedSolution(MSBuildWorkspace workspace, Solution solution)
    {
        Workspace = workspace;
        Solution = solution;
    }

    public Solution Solution { get; }

    public void Dispose() => Workspace.Dispose();

    private MSBuildWorkspace Workspace { get; }
}
