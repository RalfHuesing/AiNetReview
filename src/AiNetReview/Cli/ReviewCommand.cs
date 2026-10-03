namespace AiNetReview.Cli;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System.CommandLine;
using System.CommandLine.Invocation;

/// <summary>Adapts the review command to injectable services and streams.</summary>
public sealed class ReviewCommand
{
    private const int InvalidInputExitCode = 2;
    private const int AnalysisFailedExitCode = 3;
    private const int FailedOutputExitCode = 4;
    private const int CancelledExitCode = 130;

    public async Task<int> InvokeAsync(
        string[] args,
        IServiceProvider services,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        var root = new RootCommand("AiNetReview - Code review and architectural analysis tool for .NET solutions.");
        var review = new Command("review", "Runs review analyses on the specified solution and publishes a markdown report.");
        var reviewProjectPathArgument = new Argument<string?>("project-path")
        {
            Description = "Path to the project or solution root directory containing ainetreview.json or a .sln/.slnx file. Defaults to current directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        review.Arguments.Add(reviewProjectPathArgument);
        review.SetAction(async (parseResult, token) => await ExecuteReviewAsync(
            parseResult.GetValue(reviewProjectPathArgument),
            services,
            standardOutput,
            standardError,
            token).ConfigureAwait(false));
        root.Subcommands.Add(review);

        if (args.Length == 0)
        {
            args = ["--help"];
        }

        var parse = root.Parse(args);
        if (parse.Errors.Count > 0 || parse.UnmatchedTokens.Count > 0)
        {
            var errorMessage = parse.Errors.Count > 0
                ? string.Join(" ", parse.Errors.Select(static e => e.Message))
                : $"Unrecognized argument(s): {string.Join(", ", parse.UnmatchedTokens)}.";
            await WriteErrorAsync(standardError, "INVALID_INPUT", $"{errorMessage} Expected 'ainetreview review [project-path]'. Run 'ainetreview --help' for usage.")
                .ConfigureAwait(false);
            return InvalidInputExitCode;
        }

        var invocation = new InvocationConfiguration
        {
            Output = standardOutput,
            Error = standardError,
        };
        return await parse.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteReviewAsync(
        string? projectPath,
        IServiceProvider services,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        using var commandScope = LogContext.PushProperty("Command", "review");
        var logger = services.GetRequiredService<ILogger<ReviewCommand>>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (projectRoot, absoluteConfigPath) = ResolvePaths(projectPath);
            if (!File.Exists(absoluteConfigPath))
            {
                var solutionFileName = services.GetRequiredService<SolutionDiscovery>().Discover(projectRoot);
                if (solutionFileName is null)
                {
                    throw new InvalidReviewInputException("No .sln or .slnx file was found directly under the project directory.");
                }

                var generatedConfig = services.GetRequiredService<DefaultReviewConfigGenerator>().Generate(solutionFileName);
                try
                {
                    var created = await CreateConfigFileAsync(absoluteConfigPath, generatedConfig, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation(
                        created ? "Created default review configuration at {ConfigPath}" : "Default review configuration already exists at {ConfigPath}",
                        absoluteConfigPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidReviewInputException("Default review configuration could not be created.", exception);
                }
            }

            var config = services.GetRequiredService<ReviewConfigValidator>().Load(absoluteConfigPath);
            logger.LogInformation("Review started for {SolutionPath}", config.SolutionPath);

            LoadedSolution loaded;
            try
            {
                loaded = await services.GetRequiredService<SolutionLoader>()
                    .LoadAsync(config, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidReviewInputException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Solution analysis could not be completed");
                await WriteErrorAsync(standardError, "ANALYSIS_FAILED", "Solution could not be loaded or analyzed completely.")
                    .ConfigureAwait(false);
                return AnalysisFailedExitCode;
            }

            using (loaded)
            {
                ReviewRunResult result;
                try
                {
                    result = await services.GetRequiredService<ReviewRunner>()
                        .RunAsync(config, loaded, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Review analyses did not complete successfully");
                    await WriteErrorAsync(standardError, "ANALYSIS_FAILED", "Review analysis did not complete successfully.")
                        .ConfigureAwait(false);
                    return AnalysisFailedExitCode;
                }

                PublishedReport report;
                try
                {
                    report = await services.GetRequiredService<MarkdownReportWriter>()
                        .WriteAsync(config, result, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Markdown report could not be published");
                    await WriteErrorAsync(standardError, "REPORT_FAILED", "Markdown report could not be published.")
                        .ConfigureAwait(false);
                    return FailedOutputExitCode;
                }

                using (LogContext.PushProperty("RunId", report.RunId))
                {
                    logger.LogInformation("Review completed with {DetectedCount} finding(s)", result.DetectedCount);
                }
                var response = JsonSerializer.Serialize(new
                {
                    status = "completed",
                    runId = report.RunId,
                    indexPath = report.IndexPath,
                    counts = new { detected = result.DetectedCount },
                });
                await standardOutput.WriteLineAsync(response).ConfigureAwait(false);
                return 0;
            }
        }
        catch (InvalidReviewInputException exception)
        {
            logger.LogWarning("Invalid review input: {Reason}", exception.Message);
            await WriteErrorAsync(standardError, "INVALID_INPUT", exception.Message).ConfigureAwait(false);
            return InvalidInputExitCode;
        }
        catch (DirectoryNotFoundException exception)
        {
            logger.LogWarning(exception, "Project directory was not found");
            await WriteErrorAsync(standardError, "INVALID_INPUT", "Project directory does not exist.").ConfigureAwait(false);
            return InvalidInputExitCode;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Review cancelled by user");
            await WriteErrorAsync(standardError, "CANCELLED", "Review was cancelled.").ConfigureAwait(false);
            return CancelledExitCode;
        }
    }

    private static (string ProjectRoot, string ConfigPath) ResolvePaths(string? projectPath)
    {
        try
        {
            var projectRoot = Path.GetFullPath(projectPath ?? Environment.CurrentDirectory, Environment.CurrentDirectory);
            if (File.Exists(projectRoot))
            {
                throw new InvalidReviewInputException($"The project path '{projectPath}' is a file. Pass the directory containing the solution or project instead.");
            }

            return (projectRoot, Path.Combine(projectRoot, "ainetreview.json"));
        }
        catch (InvalidReviewInputException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidReviewInputException("Project or configuration path could not be resolved.", exception);
        }
    }

    private static async Task<bool> CreateConfigFileAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var streamCreated = false;
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            streamCreated = true;
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (!streamCreated && File.Exists(path))
        {
            return false;
        }

        return true;
    }

    private static async Task WriteErrorAsync(TextWriter writer, string code, string message)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { code, message })).ConfigureAwait(false);
    }
}
