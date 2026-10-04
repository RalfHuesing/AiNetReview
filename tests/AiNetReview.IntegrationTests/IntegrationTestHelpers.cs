namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using Microsoft.Extensions.DependencyInjection;

internal static class IntegrationTestHelpers
{
    public static async Task RestoreAsync(
        string targetPath,
        string workingDirectory,
        bool disableParallel = false,
        bool disableNodeReuse = false)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        if (disableParallel)
        {
            startInfo.ArgumentList.Add("--disable-parallel");
        }

        if (disableNodeReuse)
        {
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet restore failed with exit code {process.ExitCode}:{Environment.NewLine}{output}{error}");
        }
    }

    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
