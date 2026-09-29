namespace AiNetReview.Cli;

using System;
using System.Collections.Generic;
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

/// <summary>Adapts the single review command to injectable services and streams.</summary>
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

        var root = new RootCommand();
        var rootProjectPathArgument = new Argument<string?>("project-path")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        var rootConfigOption = new Option<string?>("--config");
        var rootCommandOption = new Option<string?>("--cmd");
        var rootOutputDirectoryOption = new Option<string?>("--output-directory");
        var rootProjectPathOption = new Option<string?>("--project-path");
        root.Arguments.Add(rootProjectPathArgument);
        root.Options.Add(rootConfigOption);
        root.Options.Add(rootCommandOption);
        root.Options.Add(rootOutputDirectoryOption);
        root.Options.Add(rootProjectPathOption);
        root.SetAction(async (parseResult, token) => await ExecuteReviewAsync(
            parseResult.GetValue(rootProjectPathOption) ?? parseResult.GetValue(rootProjectPathArgument),
            parseResult.GetValue(rootConfigOption),
            parseResult.GetValue(rootCommandOption),
            parseResult.GetValue(rootOutputDirectoryOption),
            services,
            standardOutput,
            standardError,
            token).ConfigureAwait(false));

        var review = new Command("review");
        var reviewProjectPathArgument = new Argument<string?>("project-path")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        var reviewConfigOption = new Option<string?>("--config");
        review.Arguments.Add(reviewProjectPathArgument);
        review.Options.Add(reviewConfigOption);
        review.SetAction(async (parseResult, token) => await ExecuteReviewAsync(
            parseResult.GetValue(reviewProjectPathArgument),
            parseResult.GetValue(reviewConfigOption),
            null,
            null,
            services,
            standardOutput,
            standardError,
            token).ConfigureAwait(false));
        root.Subcommands.Add(review);

        var parse = root.Parse(args);
        if (parse.Errors.Count > 0 || parse.UnmatchedTokens.Count > 0)
        {
            await WriteErrorAsync(standardError, "INVALID_INPUT", "Expected 'ainetreview [project-path] [--config <path-to-ainetreview.json>]' or 'ainetreview --cmd baseline --project-path <target-root> --config <config-file> --output-directory <absolute-path>'.")
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
        string? configPath,
        string? command,
        string? outputDirectory,
        IServiceProvider services,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (command is not null)
        {
            if (command.Equals("baseline", StringComparison.Ordinal))
            {
                return await ExecuteBaselineAsync(projectPath, configPath, outputDirectory, services, standardOutput, standardError, cancellationToken)
                    .ConfigureAwait(false);
            }

            await WriteErrorAsync(standardError, "INVALID_INPUT", "The '--cmd' option only accepts 'baseline'.").ConfigureAwait(false);
            return InvalidInputExitCode;
        }

        if (outputDirectory is not null)
        {
            await WriteErrorAsync(standardError, "INVALID_INPUT", "'--output-directory' is only supported with '--cmd baseline'.").ConfigureAwait(false);
            return InvalidInputExitCode;
        }

        using var commandScope = LogContext.PushProperty("Command", "review");
        var logger = services.GetRequiredService<ILogger<ReviewCommand>>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (projectRoot, absoluteConfigPath) = ResolvePaths(projectPath, configPath);
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
                IReadOnlyDictionary<string, string>? baselineFiles;
                try
                {
                    baselineFiles = await services.GetRequiredService<BaselineReader>()
                        .ReadAsync(config.ResolvedOutputDirectory, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Source baseline could not be read");
                    await WriteErrorAsync(standardError, "ANALYSIS_FAILED", "Source baseline could not be read.")
                        .ConfigureAwait(false);
                    return AnalysisFailedExitCode;
                }

                ReviewRunResult result;
                try
                {
                    result = await services.GetRequiredService<ReviewRunner>()
                        .RunAsync(config, loaded, cancellationToken, baselineFiles).ConfigureAwait(false);
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
                        .WriteAsync(config, result, cancellationToken, absoluteConfigPath).ConfigureAwait(false);
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

    private static async Task<int> ExecuteBaselineAsync(
        string? projectPath,
        string? configPath,
        string? outputDirectory,
        IServiceProvider services,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        using var commandScope = LogContext.PushProperty("Command", "baseline");
        var logger = services.GetRequiredService<ILogger<ReviewCommand>>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string projectRoot;
            string absoluteConfigPath;
            ReviewConfig config;
            if (outputDirectory is null)
            {
                (projectRoot, absoluteConfigPath) = ResolvePaths(projectPath, configPath);
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

                config = services.GetRequiredService<ReviewConfigValidator>().Load(absoluteConfigPath);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(configPath))
                {
                    throw new InvalidReviewInputException("'--project-path', '--config', and '--output-directory' must be supplied together for a central baseline.");
                }

                try
                {
                    projectRoot = Path.GetFullPath(projectPath, Environment.CurrentDirectory);
                    absoluteConfigPath = Path.GetFullPath(configPath, Environment.CurrentDirectory);
                    var absoluteOutputDirectory = Path.GetFullPath(outputDirectory, Environment.CurrentDirectory);
                    var configJson = await File.ReadAllTextAsync(absoluteConfigPath, cancellationToken).ConfigureAwait(false);
                    config = services.GetRequiredService<ReviewConfigValidator>()
                        .ValidateForAudit(projectRoot, configJson, absoluteOutputDirectory);
                }
                catch (InvalidReviewInputException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    throw new InvalidReviewInputException("Central baseline configuration could not be resolved or read.", exception);
                }
            }

            logger.LogInformation("Baseline started for {SolutionPath}", config.SolutionPath);

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
                logger.LogError(exception, "Solution source snapshot could not be loaded");
                await WriteErrorAsync(standardError, "ANALYSIS_FAILED", "Solution source snapshot could not be loaded completely.")
                    .ConfigureAwait(false);
                return AnalysisFailedExitCode;
            }

            using (loaded)
            {
                string baselinePath;
                try
                {
                    baselinePath = await services.GetRequiredService<BaselineWriter>()
                        .WriteAsync(config, loaded, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Source baseline could not be published");
                    await WriteErrorAsync(standardError, "BASELINE_FAILED", "Source baseline could not be published.")
                        .ConfigureAwait(false);
                    return FailedOutputExitCode;
                }

                logger.LogInformation("Baseline completed with {FileCount} source file(s)", loaded.SourceFiles.Count);
                await standardOutput.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    status = "completed",
                    baselinePath,
                    files = loaded.SourceFiles.Count,
                })).ConfigureAwait(false);
                return 0;
            }
        }
        catch (InvalidReviewInputException exception)
        {
            logger.LogWarning("Invalid baseline input: {Reason}", exception.Message);
            await WriteErrorAsync(standardError, "INVALID_INPUT", exception.Message).ConfigureAwait(false);
            return InvalidInputExitCode;
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogWarning("Project directory was not found");
            await WriteErrorAsync(standardError, "INVALID_INPUT", "Project directory does not exist.").ConfigureAwait(false);
            return InvalidInputExitCode;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Baseline cancelled by user");
            await WriteErrorAsync(standardError, "CANCELLED", "Baseline was cancelled.").ConfigureAwait(false);
            return CancelledExitCode;
        }
    }

    private static (string ProjectRoot, string ConfigPath) ResolvePaths(string? projectPath, string? configPath)
    {
        try
        {
            var absoluteConfigPath = configPath is null
                ? null
                : Path.GetFullPath(configPath, Environment.CurrentDirectory);
            var projectRoot = Path.GetFullPath(
                projectPath ?? (absoluteConfigPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(absoluteConfigPath)!),
                Environment.CurrentDirectory);
            var resolvedConfigPath = absoluteConfigPath ?? Path.Combine(projectRoot, "ainetreview.json");
            var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!Path.GetFileName(resolvedConfigPath).Equals("ainetreview.json", StringComparison.Ordinal)
                || !Path.GetFullPath(Path.GetDirectoryName(resolvedConfigPath)!).Equals(projectRoot, pathComparison))
            {
                throw new InvalidReviewInputException("Configuration path must be 'ainetreview.json' directly under the project directory.");
            }

            return (projectRoot, resolvedConfigPath);
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
