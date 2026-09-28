namespace AiNetReview;

using System.Text.Json;
using AiNetReview.Bootstrap;
using AiNetReview.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

public static class Program
{
    private const int ExitCodeLoggingFailed = 4;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            HostLogging.Initialize(GetCommandCategory(args));
        }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync(JsonSerializer.Serialize(new
            {
                code = "LOGGING_FAILED",
                message = "Host logging could not be initialized.",
            }));
            return ExitCodeLoggingFailed;
        }

        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var services = new ServiceCollection();
            services.AddAiNetReviewServices();
            services.AddAiNetReviewRules();
            services.AddLogging(logging => logging.AddSerilog(Log.Logger, dispose: false));
            await using var provider = services.BuildServiceProvider();
            return await new ReviewCommand().InvokeAsync(
                args,
                provider,
                Console.Out,
                Console.Error,
                cancellationSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Host failed before completing the command");
            await Console.Error.WriteLineAsync(JsonSerializer.Serialize(new
            {
                code = "ANALYSIS_FAILED",
                message = "Review command could not be completed.",
            }));
            return 3;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            await HostLogging.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    private static string GetCommandCategory(string[] args) => args.FirstOrDefault() switch
    {
        "review" => "review",
        null => "none",
        _ => "unknown",
    };
}
