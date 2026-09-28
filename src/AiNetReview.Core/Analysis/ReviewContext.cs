namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        CSharpDocuments = Array.AsReadOnly(solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp)
            .SelectMany(static project => project.Documents)
            .ToArray());
    }

    public Solution Solution { get; }

    public string ProjectRoot { get; }

    /// <summary>Gets the C# documents available in the loaded solution snapshot.</summary>
    public IReadOnlyList<Document> CSharpDocuments { get; }

    /// <summary>Gets a checked project-root-relative path using forward slashes.</summary>
    internal string GetProjectRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var relative = Path.GetRelativePath(ProjectRoot, Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison))
        {
            throw new AnalysisFailedException("A loaded source path is outside the project root.");
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
