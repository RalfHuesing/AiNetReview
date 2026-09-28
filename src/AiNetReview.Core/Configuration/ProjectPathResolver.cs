namespace AiNetReview.Core.Configuration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class ProjectPathResolver
{
    internal static string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            current = ResolveLink(current);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    internal static string ResolveRelative(string projectRoot, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidReviewInputException($"'{fieldName}' must be a nonempty project-relative path.");
        }

        if (Path.IsPathRooted(value) || value.StartsWith('/') || value.StartsWith('\\') || value.Contains(':'))
        {
            throw new InvalidReviewInputException($"'{fieldName}' must be project-relative.");
        }

        if (value.Contains('\\'))
        {
            throw new InvalidReviewInputException($"'{fieldName}' must use '/' path separators.");
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment == ".."))
        {
            throw new InvalidReviewInputException($"'{fieldName}' must not contain '..' path segments.");
        }

        var cleaned = segments.Where(static segment => segment != ".").ToArray();
        if (cleaned.Length == 0)
        {
            throw new InvalidReviewInputException($"'{fieldName}' must identify a path below the project root.");
        }

        var candidate = Canonicalize(Path.Combine([projectRoot, .. cleaned]));
        if (!IsWithin(projectRoot, candidate))
        {
            throw new InvalidReviewInputException($"'{fieldName}' resolves outside the project root.");
        }

        return candidate;
    }

    internal static bool IsWithin(string root, string path)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Canonicalize(root));
        var canonicalPath = Path.TrimEndingDirectorySeparator(Canonicalize(path));
        return canonicalPath.Equals(canonicalRoot, PathComparison)
            || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    internal static string ToRelativeForwardSlashes(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string ResolveLink(string path)
    {
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!entry.Exists)
        {
            return path;
        }

        try
        {
            return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException ex)
        {
            throw new InvalidReviewInputException($"Path '{path}' cannot be resolved safely.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidReviewInputException($"Path '{path}' cannot be resolved safely.", ex);
        }
    }
}
