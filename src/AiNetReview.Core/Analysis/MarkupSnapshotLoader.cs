namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Configuration;
using Microsoft.CodeAnalysis;

internal static class MarkupSnapshotLoader
{
    private const int MaximumFiles = 2_000;
    private const int MaximumFileBytes = 1024 * 1024;
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".codex",
        ".git",
        "bin",
        "generated",
        "node_modules",
        "obj",
        "temp",
    };

    internal static async Task<IReadOnlyList<MarkupDocumentSnapshot>> CaptureAsync(
        IReadOnlyCollection<Project> projects,
        string configuredProjectRoot,
        CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(PathComparer);
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = project.FilePath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' has no physical path for markup discovery.");
            }

            var projectRoot = Canonicalize(projectPath, "A project path could not be resolved for markup discovery.");
            var directory = Path.GetDirectoryName(projectRoot);
            if (directory is null || !Directory.Exists(directory))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' root could not be discovered safely.");
            }

            if (!IsWithin(configuredProjectRoot, directory, "A project root could not be checked safely."))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' is outside the configured project root.");
            }

            var buildOutputDirectories = GetBuildOutputDirectories(project);

            foreach (var document in project.Documents.Concat(project.AdditionalDocuments))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsMarkup(document.Name))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(document.FilePath))
                {
                    throw new AnalysisFailedException($"Markup document in project '{project.Name}' has no physical path.");
                }

                ValidateAdditionalDocumentPath(document.FilePath, directory, configuredProjectRoot);
            }

            foreach (var path in EnumerateMarkupFiles(directory, buildOutputDirectories, cancellationToken))
            {
                AddCandidate(candidates, path, directory, configuredProjectRoot, buildOutputDirectories);
                if (candidates.Count > MaximumFiles)
                {
                    throw new AnalysisFailedException("Markup snapshot exceeds the 2,000 file limit.");
                }
            }
        }

        if (candidates.Count > MaximumFiles)
        {
            throw new AnalysisFailedException("Markup snapshot exceeds the 2,000 file limit.");
        }

        var snapshots = new List<MarkupDocumentSnapshot>(candidates.Count);
        foreach (var path in candidates.OrderBy(static path => path, PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
            snapshots.Add(new MarkupDocumentSnapshot(path, content));
        }

        return Array.AsReadOnly(snapshots.ToArray());
    }

    private static IEnumerable<string> EnumerateMarkupFiles(
        string projectRoot,
        IReadOnlyCollection<string> buildOutputDirectories,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(projectRoot);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PathComparer.Equals(current, projectRoot) && ContainsNestedProject(current))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsMarkup(file))
                {
                    continue;
                }

                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(child);
                if (SkippedDirectoryNames.Contains(name) || IsWithinAnyDirectory(buildOutputDirectories, child))
                {
                    continue;
                }

                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool ContainsNestedProject(string directory) => Directory.EnumerateFiles(directory)
        .Any(static path => Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj");

    private static void AddCandidate(
        ISet<string> candidates,
        string path,
        string projectRoot,
        string configuredProjectRoot,
        IReadOnlyCollection<string> buildOutputDirectories)
    {
        if (HasReparsePointPathComponent(path, projectRoot))
        {
            return;
        }

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AnalysisFailedException("A markup file could not be inspected safely.", exception);
        }

        var canonicalPath = Canonicalize(path, "A markup file path could not be resolved safely.");
        if (!IsWithin(projectRoot, canonicalPath, "A markup file path could not be checked safely.")
            || !IsWithin(configuredProjectRoot, canonicalPath, "A markup file path could not be checked safely."))
        {
            throw new AnalysisFailedException("A markup file is outside its analyzed project root.");
        }

        var relativePath = Path.GetRelativePath(projectRoot, canonicalPath);
        if (relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(SkippedDirectoryNames.Contains))
        {
            return;
        }

        if (IsWithinNestedProject(projectRoot, canonicalPath)
            || IsWithinAnyDirectory(buildOutputDirectories, canonicalPath))
        {
            return;
        }

        candidates.Add(canonicalPath);
        if (candidates.Count > MaximumFiles)
        {
            throw new AnalysisFailedException("Markup snapshot exceeds the 2,000 file limit.");
        }
    }

    private static void ValidateAdditionalDocumentPath(string path, string projectRoot, string configuredProjectRoot)
    {
        if (HasReparsePointPathComponent(path, projectRoot))
        {
            return;
        }

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AnalysisFailedException("A markup file could not be inspected safely.", exception);
        }

        var canonicalPath = Canonicalize(path, "A markup file path could not be resolved safely.");
        if (!IsWithin(projectRoot, canonicalPath, "A markup file path could not be checked safely.")
            || !IsWithin(configuredProjectRoot, canonicalPath, "A markup file path could not be checked safely."))
        {
            throw new AnalysisFailedException("A markup file is outside its analyzed project root.");
        }
    }

    private static IReadOnlyCollection<string> GetBuildOutputDirectories(Project project)
    {
        var directories = new HashSet<string>(PathComparer);
        AddOutputDirectory(project.OutputFilePath);
        AddOutputDirectory(project.OutputRefFilePath);

        var generatedEditorConfigName = $"{Path.GetFileNameWithoutExtension(project.FilePath)}.GeneratedMSBuildEditorConfig.editorconfig";
        foreach (var document in project.AnalyzerConfigDocuments)
        {
            if (document.FilePath is not null
                && Path.GetFileName(document.FilePath).Equals(generatedEditorConfigName, StringComparison.OrdinalIgnoreCase))
            {
                AddOutputDirectory(document.FilePath);
            }
        }

        return directories;

        void AddOutputDirectory(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                if (directory is not null)
                {
                    directories.Add(Path.TrimEndingDirectorySeparator(directory));
                }
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                throw new AnalysisFailedException("A project build output directory could not be resolved safely.", exception);
            }
        }
    }

    private static bool HasReparsePointPathComponent(string path, string projectRoot)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AnalysisFailedException("A markup file path could not be inspected safely.", exception);
        }

        if (!IsLexicallyWithin(projectRoot, fullPath))
        {
            return false;
        }

        var current = fullPath;
        while (IsLexicallyWithin(projectRoot, current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                throw new AnalysisFailedException("A markup path component could not be inspected safely.", exception);
            }

            if (PathComparer.Equals(current, projectRoot))
            {
                break;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new AnalysisFailedException("A markup path component could not be resolved safely.");
        }

        return false;
    }

    private static bool IsWithinNestedProject(string projectRoot, string path)
    {
        var current = Path.GetDirectoryName(path);
        while (current is not null && !PathComparer.Equals(current, projectRoot))
        {
            if (!IsLexicallyWithin(projectRoot, current))
            {
                return false;
            }

            if (ContainsNestedProject(current))
            {
                return true;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static bool IsWithinAnyDirectory(IEnumerable<string> directories, string path) =>
        directories.Any(directory => IsLexicallyWithin(directory, path));

    private static bool IsLexicallyWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return PathComparer.Equals(normalizedRoot, normalizedPath)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (stream.Length > MaximumFileBytes)
            {
                throw new AnalysisFailedException("Markup snapshot contains a file larger than 1 MiB.");
            }

            using var content = new MemoryStream((int)stream.Length);
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (content.Length + read > MaximumFileBytes)
                {
                    throw new AnalysisFailedException("Markup snapshot contains a file larger than 1 MiB.");
                }

                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            content.Position = 0;
            using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AnalysisFailedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AnalysisFailedException("A markup file could not be read completely.", exception);
        }
    }

    private static string Canonicalize(string path, string message)
    {
        try
        {
            return ProjectPathResolver.Canonicalize(path);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception) || exception is InvalidReviewInputException)
        {
            throw new AnalysisFailedException(message, exception);
        }
    }

    private static bool IsWithin(string root, string path, string message)
    {
        try
        {
            return ProjectPathResolver.IsWithin(root, path);
        }
        catch (InvalidReviewInputException exception)
        {
            throw new AnalysisFailedException(message, exception);
        }
    }

    private static bool IsMarkup(string path) => Path.GetExtension(path).ToLowerInvariant() is ".razor" or ".xaml" or ".js";

    private static bool IsFileSystemFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or SecurityException;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
