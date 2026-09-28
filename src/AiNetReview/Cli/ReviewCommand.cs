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
        var review = new Command("review");
        var configOption = new Option<string>("--config") { Required = true };
        review.Options.Add(configOption);
        review.SetAction(async (parseResult, token) => await ExecuteReviewAsync(
            parseResult.GetValue(configOption),
            services,
            standardOutput,
            standardError,
            token).ConfigureAwait(false));
        root.Subcommands.Add(review);

        var parse = root.Parse(args);
        if (parse.Errors.Count > 0 || parse.UnmatchedTokens.Count > 0)
        {
            await WriteErrorAsync(standardError, "INVALID_INPUT", "Expected 'review --config <absolute-path-to-ainetreview.json>'.")
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
        string? configPath,
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
            var config = services.GetRequiredService<ReviewConfigValidator>().Load(configPath ?? string.Empty);
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
                    logger.LogError(exception, "Review rules did not complete successfully");
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
        catch (OperationCanceledException)
        {
            logger.LogInformation("Review cancelled by user");
            await WriteErrorAsync(standardError, "CANCELLED", "Review was cancelled.").ConfigureAwait(false);
            return CancelledExitCode;
        }
    }

    private static async Task WriteErrorAsync(TextWriter writer, string code, string message)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { code, message })).ConfigureAwait(false);
    }
}
