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
        using var error = JsonDocument.Parse(stderr);
        Assert.Equal("INVALID_INPUT", error.RootElement.GetProperty("code").GetString());
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
        using var error = JsonDocument.Parse(stderr);
        Assert.Equal("INVALID_INPUT", error.RootElement.GetProperty("code").GetString());
        Assert.True(string.IsNullOrWhiteSpace(stdout), "Stdout should be empty when no command is provided.");
    }

    [Fact]
    public async Task ProcessInvocation_WithValidTemplateNoOpConfigPublishesOneCompleteEmptyReport()
    {
        using var host = IsolatedHost.Create();
        var projectRoot = await CreateProjectAsync(host.HostDirectory, "public sealed class Sample { }");
        var configPath = await CreateConfigAsync(projectRoot);

        var workingDirectory = host.CreateWorkingDirectory();
        using var process = host.Start(workingDirectory, "review", "--config", configPath);
        var (stdout, stderr) = await ReadProcessOutputAsync(process);

        Assert.Equal(0, process.ExitCode);
        Assert.Empty(stderr);
        using var response = JsonDocument.Parse(Assert.Single(stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var runId = response.RootElement.GetProperty("runId").GetString();
        Assert.Matches("^[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}$", runId);
        var indexPath = response.RootElement.GetProperty("indexPath").GetString();
        Assert.StartsWith("reports/", indexPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot, indexPath!.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(projectRoot, "reports", runId!, "rules", "template-noop.md")));

        var logPath = Assert.Single(Directory.GetFiles(Path.Combine(host.HostDirectory, "logs"), "ainetreview-*.log"));
        var logContents = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Host started for review", logContents, StringComparison.Ordinal);
        Assert.Contains(runId!, logContents, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(workingDirectory, "logs")));
    }

    [Fact]
    public async Task ProcessInvocation_WhenAnalysisFails_ReturnsJsonErrorAndPublishesNoRun()
    {
        using var host = IsolatedHost.Create();
        var projectRoot = await CreateProjectAsync(host.HostDirectory, "public sealed class Sample { public void Broken( { } }");
        var configPath = await CreateConfigAsync(projectRoot);

        using var process = host.Start(host.CreateWorkingDirectory(), "review", "--config", configPath);
        var (stdout, stderr) = await ReadProcessOutputAsync(process);

        Assert.Equal(3, process.ExitCode);
        Assert.Empty(stdout);
        using var error = JsonDocument.Parse(Assert.Single(stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("ANALYSIS_FAILED", error.RootElement.GetProperty("code").GetString());
        Assert.Empty(Directory.GetDirectories(Path.Combine(projectRoot, "reports")));
    }

    [Fact]
    public async Task ProcessInvocation_ConcurrentProcessesShareLogFileAndWriteTheirStartupEvents()
    {
        using var host = IsolatedHost.Create();
        var workingDirectory = host.CreateWorkingDirectory();
        var processes = Enumerable.Range(0, 4).Select(_ => host.Start(workingDirectory, "invalid-command")).ToArray();
        try
        {
            var outputs = await Task.WhenAll(processes.Select(ReadProcessOutputAsync));
            Assert.All(processes, process => Assert.Equal(2, process.ExitCode));
            Assert.All(outputs, output => Assert.Equal("INVALID_INPUT", JsonDocument.Parse(output.Stderr).RootElement.GetProperty("code").GetString()));
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        var logPath = Assert.Single(Directory.GetFiles(Path.Combine(host.HostDirectory, "logs"), "ainetreview-*.log"));
        var startupCount = (await File.ReadAllTextAsync(logPath)).Split("Host started for unknown", StringSplitOptions.None).Length - 1;
        Assert.Equal(processes.Length, startupCount);
    }

    [Fact]
    public async Task ProcessInvocation_RotatesAnOversizedDailyLog()
    {
        using var host = IsolatedHost.Create();
        var logDirectory = Path.Combine(host.HostDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        var activeLogPath = Path.Combine(logDirectory, $"ainetreview-{DateTime.Now:yyyyMMdd}.log");
        await using (var stream = new FileStream(activeLogPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.SetLength(10 * 1024 * 1024 + 1);
        }

        using var process = host.Start(host.CreateWorkingDirectory(), "invalid-command");
        var (_, stderr) = await ReadProcessOutputAsync(process);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("INVALID_INPUT", stderr, StringComparison.Ordinal);
        Assert.True(Directory.GetFiles(logDirectory, "ainetreview-*.log").Length >= 2);
    }

    private static async Task<string> CreateProjectAsync(string testRoot, string source)
    {
        var root = Path.Combine(testRoot, "review-project");
        var project = Path.Combine(root, "Sample");
        Directory.CreateDirectory(project);
        var projectFile = Path.Combine(project, "Sample.csproj");
        await File.WriteAllTextAsync(projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(project, "Class1.cs"), source);
        await RestoreProjectAsync(projectFile, project);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"), "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        return root;
    }

    private static async Task<string> CreateConfigAsync(string projectRoot)
    {
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"template-noop\":{}}}");
        return configPath;
    }

    private static async Task RestoreProjectAsync(string projectFile, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {await stdout}{await stderr}");
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
