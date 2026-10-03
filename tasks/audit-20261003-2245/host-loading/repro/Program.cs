using System.Runtime.Loader;
using System.Reflection;

var hostAssembly = Path.GetFullPath(args[0]);
var alc = new AssemblyLoadContext("host-under-test", isCollectible: false);
var resolver = new AssemblyDependencyResolver(hostAssembly);
alc.Resolving += (_, name) => {
    var dependencyPath = resolver.ResolveAssemblyToPath(name);
    return dependencyPath is null ? null : alc.LoadFromAssemblyPath(dependencyPath);
};
var assembly = alc.LoadFromAssemblyPath(hostAssembly);
var reviewCommand = assembly.GetType("AiNetReview.Cli.ReviewCommand", throwOnError: true)!;
var root = Path.Combine(AppContext.BaseDirectory, "demo-project");
Directory.CreateDirectory(root);
await File.WriteAllTextAsync(Path.Combine(root, "Demo.slnx"), "<Solution />");
var configPath = Path.Combine(root, "ainetreview.json");
if (File.Exists(configPath)) File.Delete(configPath);

var method = reviewCommand.GetMethod("CreateConfigFileAsync", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Private bootstrap method was not found.");
using var canceled = new CancellationTokenSource();
await canceled.CancelAsync();
var task = (Task)(method.Invoke(null, new object[] { configPath, "{\"schemaVersion\":1}", canceled.Token })
    ?? throw new InvalidOperationException("Bootstrap did not return a task."));
try { await task; Console.WriteLine("Unexpected: bootstrap completed."); }
catch (OperationCanceledException) { Console.WriteLine("Bootstrap result: OperationCanceledException"); }

Console.WriteLine($"Config remains: {File.Exists(configPath)}; bytes: {new FileInfo(configPath).Length}");
var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
start.ArgumentList.Add(hostAssembly);
start.ArgumentList.Add("review");
start.ArgumentList.Add(root);
using var child = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start host.");
var stdout = await child.StandardOutput.ReadToEndAsync();
var stderr = await child.StandardError.ReadToEndAsync();
await child.WaitForExitAsync();
Console.WriteLine($"Retry exit: {child.ExitCode}");
Console.WriteLine($"Retry stdout: {stdout.Trim()}");
Console.WriteLine($"Retry stderr: {stderr.Trim()}");
Console.WriteLine($"Config bytes after retry: {new FileInfo(configPath).Length}");
