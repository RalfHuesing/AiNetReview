namespace AiNetReview.Core.Configuration;

using System;
using System.IO;
using System.Linq;

/// <summary>Finds the preferred solution file directly under a project root.</summary>
public sealed class SolutionDiscovery
{
    /// <summary>
    /// Returns the selected solution file name, or <see langword="null"/> when the directory contains no solution.
    /// </summary>
    public string? Discover(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Project root '{root}' does not exist.");
        }

        var projectDirectoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        var candidates = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                FileName = Path.GetFileName(path),
                Extension = Path.GetExtension(path),
                Name = Path.GetFileNameWithoutExtension(path),
            })
            .Where(static candidate =>
                candidate.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                || candidate.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, projectDirectoryName) ? 0 : 1)
            .ThenBy(static candidate => candidate.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(static candidate => candidate.FileName, StringComparer.Ordinal)
            .ToArray();

        return candidates.FirstOrDefault()?.FileName;
    }
}
