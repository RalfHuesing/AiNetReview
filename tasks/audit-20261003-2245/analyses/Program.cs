using System.Reflection;
using AiNetReview.Core.Analysis;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
MSBuildLocator.RegisterDefaults();
using var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(args[0]);
var builder = typeof(ReviewContext).Assembly.GetType("AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates.MissingTestEvidenceSemanticGraphBuilder", true)!;
var build = builder.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
var task = (Task)build.Invoke(null, [solution, CancellationToken.None])!; await task;
var graph = task.GetType().GetProperty("Result")!.GetValue(task)!;
var nodes = (System.Collections.IEnumerable)graph.GetType().GetProperty("Nodes")!.GetValue(graph)!;
var codeNodes = new List<IMethodSymbol>(); var allNodes = new List<IMethodSymbol>();
foreach(var node in nodes) { var m=(IMethodSymbol)node!.GetType().GetProperty("Method")!.GetValue(node)!; allNodes.Add(m); if(m.ContainingType.Name=="CodeLineMetrics") codeNodes.Add(m); }
foreach(var projectName in new[]{"AiNetReview.FastTests","AiNetReview.IntegrationTests"}) {
 var project=solution.Projects.Single(p=>p.Name==projectName); var compilation=await project.GetCompilationAsync();
 foreach(var doc in project.Documents.Where(d=>d.Name.Contains(projectName=="AiNetReview.FastTests"?"CodeLineMetricsTests":"ReviewRunnerTests",StringComparison.Ordinal))) {
  var model=await doc.GetSemanticModelAsync(); var root=await doc.GetSyntaxRootAsync();
  foreach(var invocation in root!.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
   var method=model!.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
   if(method?.ContainingType.Name=="CodeLineMetrics" || method?.Name=="RunAsync" && method.ContainingType.Name=="ReviewRunner") {
    var original=method.OriginalDefinition;
    var node=(original.ContainingType.Name=="CodeLineMetrics"?codeNodes:allNodes).FirstOrDefault(n=>n.Name==original.Name && n.ContainingType.Name==original.ContainingType.Name && n.Parameters.Length==original.Parameters.Length);
    Console.WriteLine($"PROJECT {projectName} CALL {original.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}");
    Console.WriteLine($"  call assembly={original.ContainingAssembly?.Identity}; locations={string.Join(";",original.Locations.Select(l=>l.Kind+":"+(l.SourceTree?.FilePath??l.ToString())))}");
    if(node is not null) { Console.WriteLine($"  NODE eq={SymbolEqualityComparer.Default.Equals(original,node)} typeEq={SymbolEqualityComparer.Default.Equals(original.ContainingType,node.ContainingType)} doc={DocumentationCommentId.CreateDeclarationId(original)} / {DocumentationCommentId.CreateDeclarationId(node)} assembly={node.ContainingAssembly?.Identity}; locations={string.Join(";",node.Locations.Select(l=>l.Kind+":"+(l.SourceTree?.FilePath??l.ToString())))}"); for(var i=0;i<Math.Min(original.Parameters.Length,node.Parameters.Length);i++) Console.WriteLine($"  PARAM[{i}] eq={SymbolEqualityComparer.Default.Equals(original.Parameters[i].Type,node.Parameters[i].Type)} callTypeAsm={original.Parameters[i].Type.ContainingAssembly?.Identity} nodeTypeAsm={node.Parameters[i].Type.ContainingAssembly?.Identity}"); }
   }
  }
 }
}
