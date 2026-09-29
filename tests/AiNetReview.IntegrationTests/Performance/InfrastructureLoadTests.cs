namespace AiNetReview.IntegrationTests.Performance;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

public sealed class InfrastructureLoadTests(ITestOutputHelper output)
{
    private const int ProjectCount = 6;
    private const int LinesPerProject = 30_000;
    private const int RequiredCodeLines = 180_000;

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ProductionExecutable_LoadsCompilesAndPublishesLargeSolutionWithinReleaseLimits()
    {
        Assert.True(OperatingSystem.IsWindows(), "The release performance gate must run on Windows.");
        var processorCount = Environment.ProcessorCount;
        var totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        Assert.True(processorCount >= 4, $"The release gate requires at least 4 logical CPUs; found {processorCount}.");
        Assert.True(totalMemoryBytes >= 16L * 1024 * 1024 * 1024,
            $"The release gate requires at least 16 GiB RAM; available to the process: {FormatGiB(totalMemoryBytes)} GiB.");

        using var temp = TestTempDirectory.Create("ainet-performance-");
        var projectRoot = temp.GetPath("large-solution");
        Directory.CreateDirectory(projectRoot);
        var solution = new StringBuilder("<Solution>\n");
        var sourcePaths = new List<string>(ProjectCount);
        for (var projectIndex = 0; projectIndex < ProjectCount; projectIndex++)
        {
            var projectName = $"Load{projectIndex:D2}";
            var projectDirectory = Path.Combine(projectRoot, projectName);
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, $"{projectName}.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");

            var source = new StringBuilder("namespace Generated;\npublic static class ").Append(projectName).AppendLine("\n{");
            for (var line = 0; line < LinesPerProject; line++)
            {
                source.Append("    public const int Value").Append(line.ToString("D5", CultureInfo.InvariantCulture))
                    .Append(" = ").Append(line.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            }

            source.AppendLine("}");
            var sourcePath = Path.Combine(projectDirectory, $"{projectName}.cs");
            sourcePaths.Add(sourcePath);
            await File.WriteAllTextAsync(sourcePath, source.ToString());
            solution.Append("  <Project Path=\"").Append(projectName).Append('/').Append(projectName).AppendLine(".csproj\" />");
        }

        solution.AppendLine("</Solution>");
        var solutionPath = Path.Combine(projectRoot, "AiNetReview.slnx");
        await File.WriteAllTextAsync(solutionPath, solution.ToString());
        var configPath = Path.Combine(projectRoot, "ainetreview.json");
        await File.WriteAllTextAsync(configPath,
            "{\"schemaVersion\":1,\"solution\":\"AiNetReview.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"method-control-flow-outliers\":{}}}");
        await RestoreAsync(solutionPath, projectRoot);

        using var host = IsolatedHost.Create();
        using var process = host.Start(host.CreateWorkingDirectory(), "review", Path.GetDirectoryName(configPath)!);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timer = Stopwatch.StartNew();
        await process.WaitForExitAsync();
        var peakPrivateBytes = GetPeakPrivateBytes(process);
        timer.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(stderr);
        using var response = JsonDocument.Parse(stdout);
        var runId = response.RootElement.GetProperty("runId").GetString()!;
        Assert.Equal(0, response.RootElement.GetProperty("counts").GetProperty("detected").GetInt32());
        var reportPath = Path.Combine(projectRoot, "reports", runId, "index.md");
        Assert.True(File.Exists(reportPath), "The production executable must publish the completed report.");

        var documentCount = sourcePaths.Count;
        var physicalCodeLines = await CountCodeLinesAsync(sourcePaths);
        Assert.Equal(ProjectCount, documentCount);
        Assert.True(physicalCodeLines >= RequiredCodeLines,
            $"The generated solution has {physicalCodeLines:N0} code lines; at least {RequiredCodeLines:N0} are required.");
        Assert.True(peakPrivateBytes > 0, "Windows must report a nonzero process peak commit charge.");

        output.WriteLine($"Environment: Windows {Environment.OSVersion.Version}; {processorCount} logical CPUs; {FormatGiB(totalMemoryBytes)} GiB available RAM.");
        output.WriteLine($"Load: {physicalCodeLines:N0} non-comment C# code lines; {documentCount} documents; {ProjectCount} projects.");
        output.WriteLine($"Production EXE: {timer.Elapsed.TotalSeconds:F2} s; peak private bytes: {FormatGiB(peakPrivateBytes)} GiB; exit {process.ExitCode}.");
        output.WriteLine("Command: AiNetReview.exe review <generated-project>");
        Assert.True(timer.Elapsed <= TimeSpan.FromMinutes(10), $"Load took {timer.Elapsed}; limit is 10 minutes.");
        Assert.True(peakPrivateBytes <= 6L * 1024 * 1024 * 1024,
            $"Peak private bytes were {FormatGiB(peakPrivateBytes)} GiB; limit is 6 GiB.");
    }

    private static long GetPeakPrivateBytes(Process process)
    {
        var counters = new ProcessMemoryCountersEx { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
        if (!GetProcessMemoryInfo(process.Handle, ref counters, counters.Size))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the process peak private-byte counter.");
        }

        return checked((long)counters.PeakPagefileUsage);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        internal uint Size;
        internal uint PageFaultCount;
        internal nuint PeakWorkingSetSize;
        internal nuint WorkingSetSize;
        internal nuint QuotaPeakPagedPoolUsage;
        internal nuint QuotaPagedPoolUsage;
        internal nuint QuotaPeakNonPagedPoolUsage;
        internal nuint QuotaNonPagedPoolUsage;
        internal nuint PagefileUsage;
        internal nuint PeakPagefileUsage;
        internal nuint PrivateUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx counters, uint size);

    private static async Task RestoreAsync(string solutionPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        startInfo.ArgumentList.Add("--disable-parallel");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {await stdout}{await stderr}");
    }

    private static async Task<int> CountCodeLinesAsync(IEnumerable<string> sourcePaths)
    {
        var count = 0;
        foreach (var path in sourcePaths)
        {
            foreach (var line in await File.ReadAllLinesAsync(path))
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string FormatGiB(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture);

    private sealed class IsolatedHost : IDisposable
    {
        private readonly TestTempDirectory temp;

        private IsolatedHost(TestTempDirectory temp) => this.temp = temp;

        internal static IsolatedHost Create()
        {
            var temp = TestTempDirectory.Create("ainet-performance-host-");
            try
            {
                foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetRelativePath(AppContext.BaseDirectory, source).Split(Path.DirectorySeparatorChar)[0]
                        .Equals("logs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var destination = temp.GetPath(Path.GetRelativePath(AppContext.BaseDirectory, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination);
                }

                return new IsolatedHost(temp);
            }
            catch
            {
                temp.Dispose();
                throw;
            }
        }

        private string HostDirectory => temp.DirectoryPath;

        internal string CreateWorkingDirectory() => temp.CreateSubdirectory("working-directory");

        internal Process Start(string workingDirectory, params string[] args)
        {
            var executable = Path.Combine(HostDirectory, "AiNetReview.exe");
            var startInfo = File.Exists(executable) ? new ProcessStartInfo(executable) : new ProcessStartInfo("dotnet");
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.WorkingDirectory = workingDirectory;
            if (!File.Exists(executable))
            {
                startInfo.ArgumentList.Add(Path.Combine(HostDirectory, "AiNetReview.dll"));
            }

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start AiNetReview.");
        }

        public void Dispose() => temp.Dispose();
    }
}
