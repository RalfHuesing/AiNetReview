namespace AiNetReview;

using System;
using System.IO;
using Serilog;

internal static class HostLogging
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private const string LogFileName = "ainetreview-.log";

    internal static void Initialize(string command)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        var currentLogPath = Path.Combine(
            logDirectory,
            $"ainetreview-{DateTime.Now:yyyyMMdd}.log");
        using (new FileStream(
            currentLogPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete))
        {
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, LogFileName),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Host started for {Command}", command);
    }

    internal static ValueTask CloseAndFlushAsync() => Log.CloseAndFlushAsync();
}
