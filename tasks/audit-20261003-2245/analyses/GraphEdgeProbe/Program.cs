using System.Reflection;
using AiNetReview.Core.Analysis;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();
using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(args[0]);
var builder = typeof(ReviewContext).Assembly.GetType(
    "AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates.MissingTestEvidenceSemanticGraphBuilder",
    throwOnError: true)!;
var build = builder.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
var task = (Task)build.Invoke(null, [solution, CancellationToken.None])!;
await task;
var graph = task.GetType().GetProperty("Result")!.GetValue(task)!;
var nodes = ((System.Collections.IEnumerable)graph.GetType().GetProperty("Nodes")!.GetValue(graph)!)
    .Cast<object>().ToArray();
var edges = ((System.Collections.IEnumerable)graph.GetType().GetProperty("Edges")!.GetValue(graph)!)
    .Cast<object>().ToArray();
var nodeProjects = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
foreach (var node in nodes)
{
    nodeProjects.Add(
        (IMethodSymbol)node.GetType().GetProperty("Method")!.GetValue(node)!,
        (string)node.GetType().GetProperty("ProjectName")!.GetValue(node)!);
}
Console.WriteLine($"NODES={nodes.Length} EDGES={edges.Length}");
foreach (var group in edges.Select(edge =>
         {
             var type = edge.GetType();
             var from = type.GetProperty("From")!.GetValue(edge)!;
             var to = type.GetProperty("To")!.GetValue(edge)!;
             return $"{nodeProjects[(IMethodSymbol)from]}->{nodeProjects[(IMethodSymbol)to]}";
         }).GroupBy(value => value).OrderBy(group => group.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"COUNT {group.Key}={group.Count()}");
}
var fastRoots = nodes.Where(node =>
    (string)node.GetType().GetProperty("ProjectName")!.GetValue(node)! == "AiNetReview.FastTests"
    && ((IMethodSymbol)node.GetType().GetProperty("Method")!.GetValue(node)!).ContainingType.Name == "CodeLineMetricsTests");
foreach (var root in fastRoots.OrderBy(node => ((IMethodSymbol)node.GetType().GetProperty("Method")!.GetValue(node)!).Name, StringComparer.Ordinal))
{
    var method = (IMethodSymbol)root.GetType().GetProperty("Method")!.GetValue(root)!;
    Console.WriteLine($"ROOT {method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}");
}
var directCoreEdges = edges.Where(edge =>
{
    var type = edge.GetType();
    var from = type.GetProperty("From")!.GetValue(edge)!;
    var to = type.GetProperty("To")!.GetValue(edge)!;
    return nodeProjects[(IMethodSymbol)from] == "AiNetReview.FastTests"
        && nodeProjects[(IMethodSymbol)to] == "AiNetReview.Core"
        && ((IMethodSymbol)to).ContainingType.Name == "CodeLineMetrics";
}).ToArray();
Console.WriteLine($"FASTTESTS_TO_CODELINEMETRICS_EDGES={directCoreEdges.Length}");
