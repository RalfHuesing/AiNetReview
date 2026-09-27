namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Configuration;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

public sealed class SolutionLoader
{
    private static readonly object MsBuildRegistrationLock = new();

    public async Task<LoadedSolution> LoadAsync(ReviewConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!File.Exists(config.ResolvedSolutionPath))
        {
            throw new AnalysisFailedException("Configured solution file is no longer available.");
        }

        var workspace = CreateWorkspace();
        var workspaceFailures = new ConcurrentBag<string>();
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                workspaceFailures.Add(args.Diagnostic.Message);
            }
        });

        try
        {
            var solution = await workspace.OpenSolutionAsync(config.ResolvedSolutionPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!workspaceFailures.IsEmpty)
            {
                throw new AnalysisFailedException("Solution could not be loaded completely.");
            }

            ValidateSourceBoundaries(solution, config);
            foreach (var project in solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
                var errors = compilation.GetDiagnostics(cancellationToken)
                    .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .ToArray();
                if (errors.Length > 0)
                {
                    throw new AnalysisFailedException($"Project '{project.Name}' has {errors.Length} compilation error(s).");
                }

                if (!workspaceFailures.IsEmpty)
                {
                    throw new AnalysisFailedException("Solution could not be loaded completely.");
                }
            }

            return new LoadedSolution(workspace, solution);
        }
        catch (OperationCanceledException)
        {
            workspace.Dispose();
            throw;
        }
        catch (AnalysisFailedException)
        {
            workspace.Dispose();
            throw;
        }
        catch (InvalidReviewInputException)
        {
            workspace.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            workspace.Dispose();
            throw new AnalysisFailedException("Solution could not be loaded or analyzed completely.", ex);
        }
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        RegisterMsBuild();
        return MSBuildWorkspace.Create(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DesignTimeBuild"] = "true",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
            ["RunAnalyzers"] = "false",
            ["RunCodeAnalysis"] = "false",
        });
    }

    private static void RegisterMsBuild()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        lock (MsBuildRegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    private static void ValidateSourceBoundaries(Solution solution, ReviewConfig config)
    {
        foreach (var document in solution.Projects
                     .Where(static project => project.Language == LanguageNames.CSharp)
                     .SelectMany(static project => project.Documents))
        {
            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                throw new AnalysisFailedException("A C# source document has no physical path.");
            }

            string sourcePath;
            try
            {
                sourcePath = ProjectPathResolver.Canonicalize(document.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new AnalysisFailedException("A C# source document path could not be resolved.", ex);
            }

            if (!ProjectPathResolver.IsWithin(config.ProjectRoot, sourcePath))
            {
                throw new AnalysisFailedException("A C# source document is outside the project root.");
            }

            if (ProjectPathResolver.IsWithin(config.ResolvedOutputDirectory, sourcePath) ||
                ProjectPathResolver.IsWithin(config.ResolvedStorageDirectory, sourcePath))
            {
                throw new InvalidReviewInputException("Output and storage directories must not contain C# source files from the solution.");
            }
        }
    }
}
