namespace AiNetReview.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public sealed class HostProcessIntegrationTests
{
    [Fact]
    public async Task ProcessInvocation_WithInvalidCommand_LogsBesideHostAndKeepsStreamsFreeOfLogs()
    {
        using var host = IsolatedHost.Create();
        var workingDirectory = host.CreateWorkingDirectory();
        using var process = host.Start(workingDirectory, "invalid-command");
        var (stdout, stderr) = await ReadProcessOutputAsync(process);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("INVALID_INPUT", stderr, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stdout), "Stdout should be empty on command failure.");

        var logDirectory = Path.Combine(host.HostDirectory, "logs");
        var logPath = Assert.Single(Directory.GetFiles(logDirectory, "ainetreview-*.log"));
        var logContents = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Host started for unknown", logContents, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(workingDirectory, "logs")));
        Assert.DoesNotContain("Host started", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Host started", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessInvocation_WhenLogDirectoryCannotBeCreated_ReturnsLoggingFailureOnStderr()
    {
        using var host = IsolatedHost.Create();
        var workingDirectory = host.CreateWorkingDirectory();
        await File.WriteAllTextAsync(Path.Combine(host.HostDirectory, "logs"), "blocks directory creation");

        using var process = host.Start(workingDirectory);
        var (stdout, stderr) = await ReadProcessOutputAsync(process);

        Assert.Equal(4, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout), "Stdout should stay empty when logging initialization fails.");

        using var error = JsonDocument.Parse(stderr);
        Assert.Equal("LOGGING_FAILED", error.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("Host started", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessInvocation_WithoutArguments_FailsWithExitCodeTwo()
    {
        using var host = IsolatedHost.Create();
        using var process = host.Start(host.CreateWorkingDirectory());
        var (stdout, stderr) = await ReadProcessOutputAsync(process);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("INVALID_INPUT", stderr, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stdout), "Stdout should be empty when no command is provided.");
    }

    private static async Task<(string Stdout, string Stderr)> ReadProcessOutputAsync(Process process)
    {
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (await stdoutTask, await stderrTask);
    }

    private sealed class IsolatedHost : IDisposable
    {
        private readonly TestTempDirectory tempDirectory;

        private IsolatedHost(TestTempDirectory tempDirectory)
        {
            this.tempDirectory = tempDirectory;
        }

        internal string HostDirectory => tempDirectory.DirectoryPath;

        internal static IsolatedHost Create()
        {
            var tempDirectory = TestTempDirectory.Create("ainet-host-");
            try
            {
                foreach (var sourceFile in Directory.EnumerateFiles(
                    AppContext.BaseDirectory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(AppContext.BaseDirectory, sourceFile);
                    if (relativePath.Split(Path.DirectorySeparatorChar)[0].Equals("logs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var targetFile = tempDirectory.GetPath(relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    File.Copy(sourceFile, targetFile);
                }

                return new IsolatedHost(tempDirectory);
            }
            catch
            {
                tempDirectory.Dispose();
                throw;
            }
        }

        internal string CreateWorkingDirectory() => tempDirectory.CreateSubdirectory("working-directory");

        internal Process Start(string workingDirectory, params string[] args)
        {
            var executablePath = Path.Combine(
                HostDirectory,
                OperatingSystem.IsWindows() ? "AiNetReview.exe" : "AiNetReview");
            var startInfo = File.Exists(executablePath)
                ? new ProcessStartInfo(executablePath)
                : new ProcessStartInfo("dotnet");

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.WorkingDirectory = workingDirectory;

            if (!File.Exists(executablePath))
            {
                startInfo.ArgumentList.Add(Path.Combine(HostDirectory, "AiNetReview.dll"));
            }

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("AiNetReview host process could not be started.");
        }

        public void Dispose() => tempDirectory.Dispose();
    }
}
