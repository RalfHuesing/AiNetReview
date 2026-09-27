namespace AiNetReview.Core.Analysis;

using System;
using System.IO;
using Microsoft.CodeAnalysis;

public sealed class ReviewContext
{
    public ReviewContext(Solution solution, string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!Path.IsPathFullyQualified(projectRoot))
        {
            throw new ArgumentException("Project root must be an absolute path.", nameof(projectRoot));
        }

        Solution = solution;
        ProjectRoot = Path.GetFullPath(projectRoot);
    }

    public Solution Solution { get; }

    public string ProjectRoot { get; }
}
