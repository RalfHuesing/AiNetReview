namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

public sealed class HostProcessIntegrationTests
{
    [Fact]
    public async Task ProcessInvocation_WithInvalidOrUnimplementedCommand_FailsWithExitCodeTwoAndStderrErrorJson()
    {
        using var process = StartHostProcess("invalid-command");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("INVALID_INPUT", stderr, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stdout), "Stdout should be empty on command failure.");
    }

    [Fact]
    public async Task ProcessInvocation_WithoutArguments_FailsWithExitCodeTwo()
    {
        using var process = StartHostProcess();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var stderr = await stderrTask;

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("INVALID_INPUT", stderr, StringComparison.Ordinal);
    }

    private static Process StartHostProcess(params string[] args)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "AiNetReview.exe" : "AiNetReview");
        ProcessStartInfo psi;
        if (File.Exists(exePath))
        {
            psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }
        else
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "AiNetReview.dll");
            psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(dllPath);
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        return Process.Start(psi) ?? throw new InvalidOperationException("AiNetReview-Hostprozess konnte nicht gestartet werden.");
    }
}
