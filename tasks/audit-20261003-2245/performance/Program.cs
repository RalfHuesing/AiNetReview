using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

var count = int.Parse(args[0]);
var body = new StringBuilder();
for (var index = 0; index < count; index++) body.Append("value += ").Append(index).AppendLine(";");
body.AppendLine("return value;");
var source = "public static class Synthetic { public static int First(int value) {\n" + body +
    "} public static int Second(int value) {\n" + body + "} }";
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../fixture-" + count));
Directory.CreateDirectory(root);
var sourcePath = Path.Combine(root, "Synthetic.cs");
File.WriteAllText(sourcePath, source);
using var workspace = new AdhocWorkspace();
var projectId = ProjectId.CreateNewId();
var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(),
    "Synthetic", "Synthetic", LanguageNames.CSharp, filePath: Path.Combine(root, "Synthetic.csproj"),
    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
    metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
solution = solution.AddDocument(DocumentId.CreateNewId(projectId), "Synthetic.cs", SourceText.From(source), filePath: sourcePath);
var compilation = await solution.GetProject(projectId)!.GetCompilationAsync();
var errors = compilation!.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(error => error.ToString())));
var context = new ReviewContext(solution, root);
var analysis = new StructuralDuplicationCandidatesAnalysis();
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
var timer = Stopwatch.StartNew();
try
{
    var result = await analysis.ExecuteAsync(context, analysis.Descriptor.ResolveOptions(), cancellation.Token);
    timer.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new { count, sourceBytes = Encoding.UTF8.GetByteCount(source),
        seconds = timer.Elapsed.TotalSeconds, allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore,
        findings = result.Findings.Count, status = "completed" }));
}
catch (OperationCanceledException)
{
    timer.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new { count, sourceBytes = Encoding.UTF8.GetByteCount(source),
        seconds = timer.Elapsed.TotalSeconds, allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore,
        status = "cancelled-at-45-seconds" }));
}
