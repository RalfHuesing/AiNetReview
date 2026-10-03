namespace AiNetReview.FastTests.Reporting;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;
using AiNetReview.Core.ReviewAnalyses.TypeDependencyCycleCandidates;
using Microsoft.CodeAnalysis;

public sealed class MarkdownReportWriterTests
{
    [Fact]
    public async Task WriteAsync_PublishesAllCycleFindingsInTheSingleCompleteAudit()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new TypeDependencyCycleCandidatesAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string project = "Sample/Sample.csproj";
        var symbols = new[]
        {
            new FindingSymbol(project, "A.cs", "T:Sample.A", 1),
            new FindingSymbol(project, "B.cs", "T:Sample.B", 1),
            new FindingSymbol(project, "C.cs", "T:Sample.C", 1),
        };
        var finding = new FindingDraft(project, "A.cs", "T:Sample.A", "strongly-connected-production-type-group", 1,
            "3 production types in 3 distinct declaration files form a mutually dependent group (3 directed dependencies). Example: A -> B -> C -> A. Review whether the dependencies are intentional.",
            new Dictionary<string, double> { ["typeCount"] = 3, ["declarationFileCount"] = 3, ["projectCount"] = 1, ["internalEdgeCount"] = 3 },
            [
                new FindingEvidence("A.cs", 1, "T:Sample.A", "Declaration of participating production type.", "class A"),
                new FindingEvidence("B.cs", 1, "T:Sample.B", "Declaration of participating production type.", "class B"),
                new FindingEvidence("C.cs", 1, "T:Sample.C", "Declaration of participating production type.", "class C"),
                new FindingEvidence("A.cs", 1, "T:Sample.A -> Sample/Sample.csproj::T:Sample.B", "MemberUse dependency in project Sample/Sample.csproj.", "class A"),
            ], symbols, symbols);
        var reviewFinding = new ReviewFinding(analysis.Descriptor.AnalysisId, finding, ["A.cs", "B.cs", "C.cs"], [])
        {
            Occurrences = symbols.Select(static symbol => new ReviewFindingOccurrence(symbol, ProjectRole.Production)).ToArray(),
            SubjectOccurrences = symbols.Select(static symbol => new ReviewFindingOccurrence(symbol, ProjectRole.Production)).ToArray(),
        };
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding]))])
        {
            Findings = [reviewFinding],
            ProjectClassifications = [new ProjectClassification(project, ProjectRole.Production, ProjectClassificationReason.NoTestMarker)],
        };

        var published = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, published.RunId);
        var all = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "type-dependency-cycle-candidates.md"));
        var map = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "audit", "index.md"));
        Assert.Contains("at least three distinct canonical declaration source files", all, StringComparison.Ordinal);
        Assert.Contains("Sample.A", all, StringComparison.Ordinal);
        Assert.Contains("Sample.B", all, StringComparison.Ordinal);
        Assert.Contains("MemberUse dependency", all, StringComparison.Ordinal);
        Assert.Contains("A.cs", map, StringComparison.Ordinal);
        Assert.Contains("Findings: **1**", map, StringComparison.Ordinal);
        Assert.False(Directory.EnumerateDirectories(runDirectory, "changed-files", SearchOption.AllDirectories).Any());
        Assert.False(Directory.EnumerateDirectories(runDirectory, "all-findings", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task WriteAsync_PublishesSingleCompleteAuditAtCanonicalPaths()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture analysis", "default");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var finding = Finding("Sample.cs", 2, "C:Sample", "A concise signal", "class Sample", "type-candidate");
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]);

        var published = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, published.RunId);
        Assert.False(Directory.EnumerateDirectories(runDirectory, "changed-files", SearchOption.AllDirectories).Any());
        foreach (var path in Directory.EnumerateFiles(runDirectory, "*.md", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("changed-files", content, StringComparison.OrdinalIgnoreCase);
        }

        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var map = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "audit", "index.md"));
        var area = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "index.md"));
        var report = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "fixture-analysis.md"));
        Assert.Contains("all findings in the three areas", index, StringComparison.Ordinal);
        Assert.Contains("Findings: **1**", map, StringComparison.Ordinal);
        Assert.Contains("fixture-analysis.md", area, StringComparison.Ordinal);
        Assert.Contains("A concise signal", report, StringComparison.Ordinal);
        Assert.Equal(6, Directory.GetFiles(runDirectory, "index.md", SearchOption.AllDirectories).Length);
        Assert.Contains("maps/audit/index.md", index, StringComparison.Ordinal);
        Assert.Contains("maps/index.md", index, StringComparison.Ordinal);
        var mapsIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "index.md"));
        Assert.Contains("not supplied", mapsIndex, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(runDirectory, "maps", "projects.md")));
        var projectsMap = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "projects.md"));
        Assert.Contains("not supplied", projectsMap, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_PublishesCompactMapsAndCanonicalFindingDetails()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture analysis", "default");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var findings = new List<ReviewFinding>();
        for (var index = 0; index < 40; index++)
        {
            var sourcePath = $"src/Area{index % 4}/Signal{index}.cs";
            var secondaryPath = index == 0 ? "tests/Shared/OtherOccurrence.cs" : sourcePath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(temp.DirectoryPath, sourcePath))!);
            await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, sourcePath), string.Join('\n', Enumerable.Repeat("// source line", 50)));
            if (index == 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(temp.DirectoryPath, secondaryPath))!);
                await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, secondaryPath), string.Join('\n', Enumerable.Repeat("// source line", 50)));
            }

            var projectPath = $"src/Area{index % 4}/Area{index % 4}.csproj";
            var primarySymbol = new FindingSymbol(projectPath, sourcePath, $"M:Area.Signal{index}.Run", index + 1);
            var subjectSymbols = index == 0
                ? new[] { primarySymbol, new FindingSymbol("tests/Shared.Tests.csproj", secondaryPath, "M:Shared.OtherOccurrence.Run", 17) }
                : new[] { primarySymbol };
            var draft = new FindingDraft(projectPath, sourcePath, primarySymbol.SymbolId, $"signal-{index}", index + 1,
                $"Original rationale for signal {index}.", new Dictionary<string, double> { ["count"] = index + 1 }, [],
                relatedSymbols: subjectSymbols, subjectSymbols: subjectSymbols);
            var occurrences = subjectSymbols.Select((symbol, occurrenceIndex) =>
                new ReviewFindingOccurrence(symbol, index == 0 && occurrenceIndex == 1 ? ProjectRole.Tests : ProjectRole.Production)).ToArray();
            findings.Add(new ReviewFinding(analysis.Descriptor.AnalysisId, draft,
                subjectSymbols.Select(static symbol => symbol.SourcePath).ToArray(), [])
            {
                Occurrences = occurrences,
                SubjectOccurrences = index == 39 ? [] : occurrences,
            });
        }

        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId,
            new ReviewAnalysisResult(findings.Select(static finding => finding.Finding).ToArray()))])
        {
            Findings = findings,
        };

        var published = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, published.RunId);
        var auditMapFiles = Directory.GetFiles(Path.Combine(runDirectory, "maps", "audit"), "*.md", SearchOption.AllDirectories);
        Assert.Single(auditMapFiles);

        var allMap = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "audit", "index.md"));
        Assert.Equal(40, Regex.Matches(allMap, "finding-[a-f0-9]{24}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Count);
        Assert.Contains("src/Area0/Signal0.cs", allMap, StringComparison.Ordinal);
        Assert.Contains("../../production/fixture-analysis.md", allMap, StringComparison.Ordinal);
        Assert.DoesNotContain("tests/Shared/OtherOccurrence.cs:17", allMap, StringComparison.Ordinal);
        Assert.DoesNotContain("Original rationale for signal 0", allMap, StringComparison.Ordinal);
        Assert.Contains("finding-d11ec9a27bbc3347f9bd09b3", allMap, StringComparison.Ordinal);
        var mixedRoute = Assert.Single(allMap.Split('\n').Where(line => line.Contains("finding-d11ec9a27bbc3347f9bd09b3", StringComparison.Ordinal)));
        Assert.Contains("../../mixed/fixture-analysis.md", mixedRoute, StringComparison.Ordinal);
        var detailReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "mixed", "fixture-analysis.md"));
        Assert.Contains("tests/Shared/OtherOccurrence.cs:17", detailReport, StringComparison.Ordinal);
        Assert.Contains("Tests", detailReport, StringComparison.Ordinal);
        Assert.DoesNotContain("<a id=", detailReport, StringComparison.Ordinal);
        AssertAuditMapRoutesResolve(runDirectory, allMap);

        var orderedMapRoot = temp.GetPath("ordered-map");
        var reversedMapRoot = temp.GetPath("reversed-map");
        await AuditMapReportWriter.WriteAuditMapAsync(orderedMapRoot, "fixed-run", findings, CancellationToken.None);
        var reversedFindings = findings.AsEnumerable().Reverse().Select(finding => finding with
        {
            SubjectOccurrences = finding.SubjectOccurrences.Reverse().ToArray(),
            SourcePaths = finding.SourcePaths.Reverse().ToArray(),
        }).ToArray();
        await AuditMapReportWriter.WriteAuditMapAsync(reversedMapRoot, "fixed-run", reversedFindings, CancellationToken.None);
        Assert.Equal(
            await File.ReadAllTextAsync(Path.Combine(orderedMapRoot, "maps", "audit", "index.md")),
            await File.ReadAllTextAsync(Path.Combine(reversedMapRoot, "maps", "audit", "index.md")));
    }

    private static void AssertAuditMapRoutesResolve(string runDirectory, string map)
    {
        var routePattern = new Regex(@"^- `(?<path>\.\./[^`]+\.md)`: (?<ids>finding-[a-f0-9]{24}(?:, finding-[a-f0-9]{24})*)$", RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        var routes = routePattern.Matches(map);
        Assert.NotEmpty(routes);
        foreach (Match route in routes)
        {
            var reportPath = Path.GetFullPath(Path.Combine(runDirectory, "maps", "audit",
                route.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(File.Exists(reportPath), $"Audit-map route does not resolve: '{route.Groups["path"].Value}'.");
            foreach (var id in route.Groups["ids"].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.Contains(id, File.ReadAllText(reportPath), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_RendersEveryStructuralOccurrenceWithFramedPathsAndExclusiveCoordinates()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string projectPath = "Product; One/[β]`special.csproj";
        const string sourcePath = "src/file ; [x]`β.cs";
        const string owner = "M:AiNetReview.FastTests.Analysis.TypeDependencyGraphBuilderTests.Fixture~return";
        var finding = new FindingDraft(
            projectPath,
            sourcePath,
            owner,
            "structural-duplicate:12:80",
            3,
            "This group contains repeated fragments.",
            new Dictionary<string, double>
            {
                ["memberCount"] = 3,
                ["executableCount"] = 2,
                ["statementCount"] = 3,
                ["tokenCount"] = 60,
            },
            [
                new FindingEvidence(sourcePath, 3, owner,
                    $"project={JsonSerializer.Serialize(projectPath)};start=3:5;end=7:10", "int x = value;"),
                new FindingEvidence(sourcePath, 4, owner,
                    $"project={JsonSerializer.Serialize(projectPath)};start=4:1;end=8:2", "int x = value;"),
                new FindingEvidence(sourcePath, 9, "M:Product.Other.Run(System.Int32)",
                    $"project={JsonSerializer.Serialize(projectPath)};start=9:2;end=12:1", "int y = input;"),
            ],
            [new FindingSymbol(projectPath, sourcePath, owner, 3),
                new FindingSymbol(projectPath, sourcePath, "M:Product.Other.Run(System.Int32)", 9)]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]) { });
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId,
            "production", "structural-duplication-candidates.md"));

        Assert.Contains("Structural duplicate: 3 occurrences in 2 executable members; 3 statements / 60 tokens; identical after local/parameter normalization.", markdown, StringComparison.Ordinal);
        Assert.Contains("Containment suppression removes a smaller fragment only when every occurrence is contained in an occurrence of a larger qualifying group; a smaller group with any additional occurrence remains reportable.", markdown, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: ", markdown, StringComparison.Ordinal);
        Assert.Contains("(1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains(projectPath, markdown, StringComparison.Ordinal);
        Assert.Contains("src/file", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[src/file", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<a id=", markdown, StringComparison.Ordinal);
        Assert.Contains("``Product; One/[β]`special.csproj``", markdown, StringComparison.Ordinal);
        Assert.Contains("3:5", markdown, StringComparison.Ordinal);
        Assert.Contains("7:10", markdown, StringComparison.Ordinal);
        Assert.Contains("4:1", markdown, StringComparison.Ordinal);
        Assert.Contains("8:2", markdown, StringComparison.Ordinal);
        Assert.Contains("9:2", markdown, StringComparison.Ordinal);
        Assert.Contains("12:1", markdown, StringComparison.Ordinal);
        Assert.Contains("TypeDependencyGraphBuilderTests.Fixture", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("~return", markdown, StringComparison.Ordinal);
        Assert.Equal(1, markdown.Split("### Project: ", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("#### File: ", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("Statement and control\\-flow shape", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("Is this repeated structure intentional", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task WriteAsync_FormatsNonAsciiIdentifiersSignalAndReport()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new NonAsciiIdentifiersAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var finding = new FindingDraft(
            "Sample/Sample.csproj",
            "Sample.cs",
            "T:Sample.BestätigungsService",
            "type",
            10,
            "The type identifier 'BestätigungsService' contains non-ASCII characters (e.g. 'ä').",
            new Dictionary<string, double>(),
            [new FindingEvidence("Sample.cs", 10, "Type declaration", "detail", "BestätigungsService")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "production", "non-ascii-identifiers.md"));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("BestätigungsService", markdown, StringComparison.Ordinal);
        Assert.Contains("Signal: The type identifier 'BestätigungsService' contains non\\-ASCII characters (e.g. 'ä').", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Effective options:", markdown, StringComparison.Ordinal);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        var analysisRow = Assert.Single(index.Split('\n').Where(static line => line.Contains("`non-ascii-identifiers`", StringComparison.Ordinal)));
        Assert.EndsWith("| — |", analysisRow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_ReportsEmptyActiveAnalysesWithoutCreatingAnalysisFiles()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("alpha-analysis", "Alpha | Review analysis", "value|with `markdown`");
        var secondAnalysis = new ReportAnalysis("zeta-analysis", "Zeta Review analysis", "last");
        var config = CreateConfig(temp.DirectoryPath, analysis, secondAnalysis);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(secondAnalysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
        ]) { });

        var indexBytes = await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        Assert.Equal(7, Directory.GetFiles(runDirectory, "*", SearchOption.AllDirectories).Length);
        Assert.Contains("No findings in this audit.", await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "audit", "index.md")), StringComparison.Ordinal);
        Assert.False(indexBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', indexBytes);
        var index = Encoding.UTF8.GetString(indexBytes);
        Assert.Contains($"# AiNetReview – {report.RunId}", index, StringComparison.Ordinal);
        var repositoryLine = Assert.Single(index.Split('\n').Where(static line => line.StartsWith("Repository:", StringComparison.Ordinal)));
        var repositoryPath = repositoryLine["Repository: `".Length..repositoryLine.IndexOf("`; solution:", StringComparison.Ordinal)].Replace("\\\\", "\\", StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(repositoryPath));
        Assert.Contains("solution: `Sample.slnx`", repositoryLine, StringComparison.Ordinal);
        Assert.Contains("No findings were found.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Started", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Detected", index, StringComparison.Ordinal);
        Assert.Equal(4, Directory.GetDirectories(Path.Combine(config.ResolvedOutputDirectory, report.RunId)).Length);
        Assert.Equal("reports/" + report.RunId + "/index.md", report.IndexPath);
    }

    [Fact]
    public async Task WriteAsync_DistinguishesWhenNoAnalysesAreActive()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("inactive-analysis", "Inactive Review analysis", "unused");
        var config = CreateConfig(temp.DirectoryPath, false, analysis);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([]) { });
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("No review was performed because all analyses are disabled.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("No findings were found", index, StringComparison.Ordinal);
        Assert.Equal(7, Directory.GetFiles(runDirectory, "*", SearchOption.AllDirectories).Length);
        Assert.Contains("not supplied", await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "index.md")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_LinksOnlyAnalysesWithFindingsAndWritesOneListItemPerFinding()
    {
        using var temp = TestTempDirectory.Create();
        var withFindings = new ReportAnalysis("has-findings", "Has Findings", "active");
        var withoutFindings = new ReportAnalysis("empty-analysis", "Empty Review analysis", "active");
        var config = CreateConfig(temp.DirectoryPath, withFindings, withoutFindings);
        var finding = Finding("Sample.cs", 2, "C:Sample", "A concise signal", "class Sample", "type-candidate");
        var samePathInAnotherProject = new FindingDraft(
            "Other/Sample.csproj", "Sample.cs", "C:Other", "type-candidate", 3, "A concise signal",
            new Dictionary<string, double>(),
            [new FindingEvidence("Sample.cs", 3, "Type declaration", "A candidate declaration.", "class Other")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(withFindings.Descriptor.AnalysisId, new ReviewAnalysisResult([finding, samePathInAnotherProject])),
            new ReviewAnalysisRunResult(withoutFindings.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
        ]) { });
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var analysisReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "has-findings.md"));
        Assert.Contains("production/index.md", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Empty Review analysis", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "index.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "production", "empty-analysis.md")));
        Assert.Contains("## Findings", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2 across 2 projects and 2 source files.", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Other/Sample.csproj", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Sample/Sample.csproj", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Sample.cs", analysisReport, StringComparison.Ordinal);
        Assert.DoesNotContain("| Project | Source file | Findings |", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Sample", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Other", analysisReport, StringComparison.Ordinal);
        Assert.Contains("finding-", analysisReport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_GroupsFindingsDeterministicallyAndEscapesMarkdownTableAndHeadings()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string projectPath = "Project|One/β`name.csproj";
        const string sourcePath = "src|folder/line\nbreak`file.cs";
        var first = new FindingDraft(projectPath, sourcePath, "M:Zed.Run", "z", 4, "last|signal",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 4, "Member", "detail", "Run")]);
        var second = new FindingDraft(projectPath, sourcePath, "M:Alpha.Run", "a", 4, "first|signal",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 4, "Member", "detail", "Run")]);
        var sameSubject = new FindingDraft(projectPath, sourcePath, "M:Alpha.Run", "b", 4, "second|signal",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 4, "Member", "detail", "Run")]);
        var otherProject = new FindingDraft("Another.csproj", sourcePath, "M:Only.Run", "only", 1, "single",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, 1, "Member", "detail", "Run")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([first, otherProject, sameSubject, second])),
        ]));
        var markdownPath = Path.Combine(config.ResolvedOutputDirectory, report.RunId, "production", "fixture-analysis.md");
        var markdownBytes = await File.ReadAllBytesAsync(markdownPath);
        var markdown = Encoding.UTF8.GetString(markdownBytes);

        Assert.Contains("## Summary\n\nTotal findings: 4 across 2 projects and 2 source files.", markdown, StringComparison.Ordinal);
        Assert.Contains("Another.csproj", markdown, StringComparison.Ordinal);
        Assert.Contains("Project|One/β", markdown, StringComparison.Ordinal);
        Assert.Contains("line break", markdown, StringComparison.Ordinal);
        Assert.Contains("file.cs", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("first\\|signal", StringComparison.Ordinal) < markdown.IndexOf("second\\|signal", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("second\\|signal", StringComparison.Ordinal) < markdown.IndexOf("last\\|signal", StringComparison.Ordinal));
        Assert.Equal(4, markdown.Split("- Signal:", StringSplitOptions.None).Length - 1);
        Assert.False(markdownBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', markdownBytes);
    }

    [Fact]
    public async Task WriteAsync_SortsFindingsAndEscapesSignalContentWithoutDuplicatingSourceLinks()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var z = Finding("z file#1.cs", 9, "Z", "last|rationale", "second`snippet", "zeta");
        var a = Finding("a file#1.cs", 3, "A", "first | rationale", "`snippet`", "alpha");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([z, a])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "production", "fixture-analysis.md"));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("Subject: `A`", StringComparison.Ordinal) < markdown.IndexOf("Subject: `Z`", StringComparison.Ordinal));
        Assert.Contains("\\| rationale", markdown, StringComparison.Ordinal);
        Assert.Contains("a file#1.cs", markdown.Replace("\\#", "#", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("[a file", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Source:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Effective options: `{}`", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("\"alpha\"", StringComparison.Ordinal) < markdown.IndexOf("\"scenario\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\\{", markdown, StringComparison.Ordinal);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        Assert.Contains("Review guidance", index, StringComparison.Ordinal);
        Assert.Contains("false positive", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evidence", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Audit scope", index, StringComparison.Ordinal);
        Assert.DoesNotContain("baseline", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--cmd", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_PreservesEveryClusterMemberLocationWithoutSourceLinks()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("duplicate-code-candidates", "Duplicate code", "active");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "Product"));
        Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "Other"));
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "Product", "First.cs"), "class First { }");
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "Other", "Second # {sample}.cs"), "class Second { }");
        var first = new FindingSymbol("Sample/Sample.csproj", "Product/First.cs", "M:First.Run", 2);
        var second = new FindingSymbol("Other/Other.csproj", "Other/Second # {sample}.cs", "M:Second.Run", 5);
        var finding = new FindingDraft("Sample/Sample.csproj", "Product/First.cs", "M:First.Run", "duplicate-cluster", 2, "similar methods",
            new Dictionary<string, double>(), [new FindingEvidence("Product/First.cs", 2, "Member", "member source", "Run")], [first, second]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]) { });
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var markdown = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "duplicate-code-candidates.md"));
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("Total findings: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("Product/First.cs", markdown, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", markdown, StringComparison.Ordinal);
        Assert.Contains("L2", markdown, StringComparison.Ordinal);
        Assert.Contains("Second", markdown, StringComparison.Ordinal);
        Assert.Contains(":5", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("](../../../../", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<a id=", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Project | Source file | Findings |", markdown, StringComparison.Ordinal);
        Assert.Contains("production/index.md", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "duplicate-code-candidates.md")));
    }

    [Fact]
    public async Task WriteAsync_RendersForwardingPathInEvidenceOrderWithStructuralMetrics()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new IndirectionDriftCandidatesAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var finding = new FindingDraft(
            "Sample/Sample.csproj",
            "ZApi.cs",
            "M:ZApi.Run(System.Int32)",
            "transparent-forwarding-path",
            1,
            "Current statically declared path.",
            new Dictionary<string, double>
            {
                ["forwardingEdgeCount"] = 2,
                ["distinctTypeCount"] = 3,
                ["distinctFileCount"] = 3,
            },
            [
                new FindingEvidence("ZApi.cs", 1, "M:ZApi.Run(System.Int32)", "Forwards to service.", "return BService.Run(value);"),
                new FindingEvidence("BService.cs", 1, "M:BService.Run(System.Int32)", "Forwards to repository.", "return ARepository.Run(value);"),
                new FindingEvidence("ARepository.cs", 1, "M:ARepository.Run(System.Int32)", "Ends the path.", "return value;"),
            ],
            [
                new FindingSymbol("Sample/Sample.csproj", "ARepository.cs", "M:ARepository.Run(System.Int32)", 1),
                new FindingSymbol("Sample/Sample.csproj", "BService.cs", "M:BService.Run(System.Int32)", 1),
                new FindingSymbol("Sample/Sample.csproj", "ZApi.cs", "M:ZApi.Run(System.Int32)", 1),
            ]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ]) { });
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var detail = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "indirection-drift-candidates.md"));
        var areaIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "index.md"));

        Assert.DoesNotContain("Full-audit reference", detail, StringComparison.Ordinal);
        Assert.Contains("indirection-drift-candidates.md", areaIndex, StringComparison.Ordinal);
        foreach (var path in new[] { "ZApi.cs", "BService.cs", "ARepository.cs" })
        {
            Assert.Contains(path, detail, StringComparison.Ordinal);
        }
        Assert.Contains("Total findings: 1", detail, StringComparison.Ordinal);
        Assert.Contains("ZApi.cs", detail, StringComparison.Ordinal);
        Assert.Contains("- Forwarding path: 2 forwarding edges across 3 types and 3 files", detail, StringComparison.Ordinal);
        Assert.Contains("  - Forwarding path:", detail, StringComparison.Ordinal);
        Assert.Contains("    - `L1` `ZApi.Run`", detail, StringComparison.Ordinal);
        Assert.True(detail.IndexOf("L1", StringComparison.Ordinal)
            < detail.IndexOf("BService.cs:1", StringComparison.Ordinal));
        Assert.True(detail.IndexOf("BService.cs:1", StringComparison.Ordinal)
            < detail.IndexOf("ARepository.cs:1", StringComparison.Ordinal));
        Assert.DoesNotContain("Cluster:", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("#L1", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RendersCompleteMissingTestEvidenceAndCrossAnalysisContext()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new MissingTestEvidenceCandidatesAnalysis();
        var otherAnalysis = new ReportAnalysis("other-analysis", "Other Analysis", "default");
        var config = CreateConfig(temp.DirectoryPath, analysis, otherAnalysis);
        const string testPathMethodId = "M:Sample.Tests.Tests`1.CallsApi~return";
        const string testPathSource = "Sample.Tests/Test`Root{V1}.cs";
        var noPath = new FindingDraft("Sample/Sample.csproj", "Sample/NoPath.cs", "M:Sample.NoPath.Run", "no-static-test-path", 2,
            "no static test path", new Dictionary<string, double>
            {
                ["decisionCount"] = 3, ["maxDecisionNesting"] = 2, ["attributionUncertain"] = 1,
            }, [new FindingEvidence("Sample/NoPath.cs", 2, "Declaration", "Eligible candidate", "Run")]);
        var indirect = new FindingDraft("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", "indirect-test-path-only", 4,
            "indirect test path only", new Dictionary<string, double>
            {
                ["decisionCount"] = 5, ["maxDecisionNesting"] = 3, ["attributionUncertain"] = 0,
            },
            [
                new FindingEvidence("Sample/Indirect.cs", 4, "M:Sample.Indirect.Run", "Eligible candidate", "Run"),
                new FindingEvidence(testPathSource, 3, testPathMethodId, "Test root", "CallsApi"),
                new FindingEvidence("Sample/Generated/Worker.g.cs", 2, "M:Sample.GeneratedIntermediate.Run", "Generated intermediate", "Run"),
                new FindingEvidence("Sample/Api.cs", 8, "M:Sample.Api.Run", "Intermediate", "Run"),
                new FindingEvidence("Sample/Indirect.cs", 4, "M:Sample.Indirect.Run", "Candidate", "Run"),
            ],
            [new FindingSymbol("Sample.Tests/Sample.Tests.csproj", testPathSource, testPathMethodId, 3),
                new FindingSymbol("Sample/Sample.csproj", "Sample/Api.cs", "M:Sample.Api.Run", 8),
                new FindingSymbol("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", 4)]);
        var otherFinding = Finding("Sample/Other.cs", 1, "M:Sample.Other.Run", "other analysis finding", "Run", "case");
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([noPath, indirect])),
            new ReviewAnalysisRunResult(otherAnalysis.Descriptor.AnalysisId, new ReviewAnalysisResult([otherFinding])),
        ])
        {
            Findings = [
                new ReviewFinding(analysis.Descriptor.AnalysisId, noPath, ["Sample/NoPath.cs"], []),
                new ReviewFinding(analysis.Descriptor.AnalysisId, indirect, ["Sample/Indirect.cs", "Sample/Api.cs", testPathSource], [])
                {
                    Occurrences = [
                        new ReviewFindingOccurrence(new FindingSymbol("Sample.Tests/Sample.Tests.csproj", testPathSource, testPathMethodId, 3), ProjectRole.Tests),
                        new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "Sample/Api.cs", "M:Sample.Api.Run", 8), ProjectRole.Production),
                        new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", 4), ProjectRole.Production),
                    ],
                    SubjectOccurrences = [new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", 4), ProjectRole.Production)],
                },
                new ReviewFinding(otherAnalysis.Descriptor.AnalysisId, otherFinding, ["Sample/Other.cs"], []),
            ],
            ProjectClassifications = [
                new ProjectClassification("Sample/Sample.csproj", ProjectRole.Production, ProjectClassificationReason.NoTestMarker),
                new ProjectClassification("Sample.Tests/Sample.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix),
            ],
        };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        Assert.DoesNotContain(Directory.EnumerateFiles(runDirectory, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(runDirectory, path).Replace('\\', '/')),
            path => path.Contains("all-findings", StringComparison.Ordinal) || path.Contains("changed-files", StringComparison.Ordinal));
        var detail = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "missing-test-evidence-candidates.md"));
        var rootIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var areaIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "index.md"));
        var auditMap = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "audit", "index.md"));

        var noPathSignal = Assert.Single(detail.Split('\n').Where(static line => line.Contains("no static test path", StringComparison.Ordinal)));
        var indirectSignal = Assert.Single(detail.Split('\n').Where(static line => line.Contains("indirect test path only", StringComparison.Ordinal)));
        Assert.Contains("attribution uncertain", noPathSignal, StringComparison.Ordinal);
        Assert.DoesNotContain("attribution uncertain", indirectSignal, StringComparison.Ordinal);
        Assert.Contains("A production function meets the nontrivial gate when `decisionCount >= minDecisionCount OR maxDecisionNesting >= minDecisionNesting`.", detail, StringComparison.Ordinal);
        Assert.Contains("an indirect-path-only function must also meet `decisionCount >= minIndirectDecisionCount OR maxDecisionNesting >= minIndirectDecisionNesting`", detail, StringComparison.Ordinal);
        Assert.Contains("Shortest resolved test path:", detail, StringComparison.Ordinal);
        Assert.Contains("Tests`1.CallsApi", detail, StringComparison.Ordinal);
        Assert.Contains("Sample/Generated/Worker.g.cs:2", detail, StringComparison.Ordinal);
        Assert.Contains("Reflection, dependency injection, external test projects, dynamic dispatch, branch execution, and custom test discovery", detail, StringComparison.Ordinal);
        Assert.Contains("Findings: **3**", auditMap, StringComparison.Ordinal);
        Assert.Contains("production/index.md", rootIndex, StringComparison.Ordinal);
        Assert.Contains("other-analysis.md", areaIndex, StringComparison.Ordinal);
        Assert.Contains("missing-test-evidence-candidates.md", areaIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("changed-files", rootIndex, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.EnumerateDirectories(runDirectory, "all-findings", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task WriteAsync_LinksRelatedFindingsAcrossAnalysesInTheCompleteAudit()
    {
        using var temp = TestTempDirectory.Create();
        var alpha = new ReportAnalysis("alpha-analysis", "Alpha", "active");
        var beta = new ReportAnalysis("beta-analysis", "Beta", "active");
        var gamma = new ReportAnalysis("gamma-analysis", "Gamma", "active");
        var config = CreateConfig(temp.DirectoryPath, alpha, beta, gamma);
        var symbol = new FindingSymbol("Sample/Sample.csproj", "Sample.cs", "M:Sample.Run", 3);
        var alphaDraft = new FindingDraft("Sample/Sample.csproj", "Sample.cs", "M:Sample.Run", "complexity", 3, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Sample.cs", 3, "Source", "detail", "Run")], [symbol]);
        var betaDraft = new FindingDraft("Sample/Sample.csproj", "Other.cs", "M:Sample.Run", "dead-code", 8, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Other.cs", 8, "Source", "detail", "Run")], [symbol]);
        var gammaDraft = new FindingDraft("Sample/Sample.csproj", "Outside.cs", "M:Sample.Run", "outside", 11, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Outside.cs", 11, "Source", "detail", "Run")], [symbol]);
        var alphaFinding = new ReviewFinding("alpha-analysis", alphaDraft, ["Sample.cs"], [
            new ReviewFindingReference("beta-analysis", betaDraft.ProjectPath, betaDraft.SourcePath, betaDraft.SubjectId,
                betaDraft.Discriminator, "Sample.cs", "M:Sample.Run", 3),
            new ReviewFindingReference("gamma-analysis", gammaDraft.ProjectPath, gammaDraft.SourcePath, gammaDraft.SubjectId,
                gammaDraft.Discriminator, "Sample.cs", "M:Sample.Run", 3),
        ]);
        var betaFinding = new ReviewFinding("beta-analysis", betaDraft, ["Other.cs"], []);
        var gammaFinding = new ReviewFinding("gamma-analysis", gammaDraft, ["Outside.cs"], []);
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult("alpha-analysis", new ReviewAnalysisResult([alphaDraft])),
            new ReviewAnalysisRunResult("beta-analysis", new ReviewAnalysisResult([betaDraft])),
            new ReviewAnalysisRunResult("gamma-analysis", new ReviewAnalysisResult([gammaDraft])),
        ]) { Findings = [alphaFinding, betaFinding, gammaFinding] };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var alphaReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "alpha-analysis.md"));
        var betaReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "beta-analysis.md"));
        var gammaReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "gamma-analysis.md"));
        var auditMap = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "audit", "index.md"));

        Assert.Contains("beta-analysis.md", alphaReport, StringComparison.Ordinal);
        Assert.Contains("gamma-analysis.md", alphaReport, StringComparison.Ordinal);
        Assert.DoesNotContain("all-findings reference", alphaReport, StringComparison.Ordinal);
        Assert.Contains("finding-", betaReport, StringComparison.Ordinal);
        Assert.Contains("finding-", gammaReport, StringComparison.Ordinal);
        Assert.Contains("../../production/alpha-analysis.md", auditMap, StringComparison.Ordinal);
        Assert.Contains("../../production/beta-analysis.md", auditMap, StringComparison.Ordinal);
        Assert.Contains("../../production/gamma-analysis.md", auditMap, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_PublishesThreeCompleteAreasWithScopeProvenanceAndCrossAreaRelatedLinks()
    {
        using var temp = TestTempDirectory.Create();
        var sizeAnalysis = new AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates.CodeSizeCandidatesAnalysis();
        var flowAnalysis = new ReportAnalysis("flow-analysis", "Flow", "active");
        var duplicateAnalysis = new ReportAnalysis("duplicate-analysis", "Duplicate", "active");
        var config = CreateConfig(temp.DirectoryPath, sizeAnalysis, flowAnalysis, duplicateAnalysis);
        var production = new FindingSymbol("src/Product.csproj", "src/Product.cs", "M:Product.Run", 4);
        var test = new FindingSymbol("tests/Product.Tests.csproj", "tests/ProductTests.cs", "M:ProductTests.Run", 8);
        var productionFinding = new FindingDraft(production.ProjectPath, production.SourcePath, production.SymbolId, "large-member", 4,
            "large production method", new Dictionary<string, double>(), [new FindingEvidence(test.SourcePath, 8, "Test evidence", "context", "Run")], [production]);
        var testFinding = new FindingDraft(test.ProjectPath, test.SourcePath, test.SymbolId, "test-case", 8,
            "test-only signal", new Dictionary<string, double>(), [new FindingEvidence(test.SourcePath, 8, "Test", "detail", "Run")], [test]);
        var mixedFinding = new FindingDraft(production.ProjectPath, production.SourcePath, production.SymbolId, "mixed-cluster", 4,
            "cross-role cluster", new Dictionary<string, double>(), [new FindingEvidence(production.SourcePath, 4, "Member", "detail", "Run")], [production, test]);
        var prodReview = new ReviewFinding(sizeAnalysis.Descriptor.AnalysisId, productionFinding, [production.SourcePath, test.SourcePath], [])
        {
            Occurrences = [new ReviewFindingOccurrence(production, ProjectRole.Production)],
            SubjectOccurrences = [new ReviewFindingOccurrence(production, ProjectRole.Production)],
        };
        var testReview = new ReviewFinding(flowAnalysis.Descriptor.AnalysisId, testFinding, [test.SourcePath], [])
        {
            Occurrences = [new ReviewFindingOccurrence(test, ProjectRole.Tests)],
            SubjectOccurrences = [new ReviewFindingOccurrence(test, ProjectRole.Tests)],
        };
        var mixedReview = new ReviewFinding(duplicateAnalysis.Descriptor.AnalysisId, mixedFinding, [production.SourcePath], [
            new ReviewFindingReference(flowAnalysis.Descriptor.AnalysisId, test.ProjectPath, test.SourcePath, test.SymbolId, "test-case", test.SourcePath, test.SymbolId, test.Line),
        ])
        {
            Occurrences = [new ReviewFindingOccurrence(production, ProjectRole.Production), new ReviewFindingOccurrence(test, ProjectRole.Tests)],
            SubjectOccurrences = [new ReviewFindingOccurrence(production, ProjectRole.Production), new ReviewFindingOccurrence(test, ProjectRole.Tests)],
        };
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult(sizeAnalysis.Descriptor.AnalysisId, new ReviewAnalysisResult([productionFinding])),
            new ReviewAnalysisRunResult(flowAnalysis.Descriptor.AnalysisId, new ReviewAnalysisResult([testFinding])
            {
                ScopeExclusions = [new ReviewAnalysisScopeExclusion("tests/Unknown.csproj", "Unresolved provider type Data.Cases.")],
            }),
            new ReviewAnalysisRunResult(duplicateAnalysis.Descriptor.AnalysisId, new ReviewAnalysisResult([mixedFinding])),
        ])
        {
            ProjectClassifications = [
                new ProjectClassification("src/Product.csproj", ProjectRole.Production, ProjectClassificationReason.NoTestMarker),
                new ProjectClassification("tests/Product.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix),
                new ProjectClassification("tests/Empty.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectFileNameSuffix),
            ],
            Maps = new ReviewMaps(
                [
                    new ReviewMapProject("p-product", "src/Product.csproj", ProjectRole.Production, ProjectClassificationReason.NoTestMarker,
                        [new ReviewMapProjectReference("p-product-tests", "tests/Product.Tests.csproj", ProjectRole.Tests)]),
                    new ReviewMapProject("p-product-tests", "tests/Product.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix, []),
                    new ReviewMapProject("p-empty-tests", "tests/Empty.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectFileNameSuffix, []),
                ],
                [],
                [],
                []),
            Findings = [prodReview, testReview, mixedReview],
        };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        foreach (var area in new[] { "production", "tests", "mixed" })
        {
            Assert.True(File.Exists(Path.Combine(runDirectory, area, "index.md")));
        }

        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "code-size-candidates.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "tests", "flow-analysis.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "mixed", "duplicate-analysis.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "production", "duplicate-analysis.md")));
        Assert.Contains("| production | 1 |", index, StringComparison.Ordinal);
        Assert.Contains("| tests | 1 |", index, StringComparison.Ordinal);
        Assert.Contains("| mixed | 1 |", index, StringComparison.Ordinal);
        Assert.Contains("tests/Unknown.csproj", index, StringComparison.Ordinal);
        Assert.Contains("Unresolved provider type", index, StringComparison.Ordinal);
        var projectsMap = await File.ReadAllTextAsync(Path.Combine(runDirectory, "maps", "projects.md"));
        Assert.Contains("## `src/Product.csproj`", projectsMap, StringComparison.Ordinal);
        Assert.Contains("role: production; classification: `NoTestMarker`", projectsMap, StringComparison.Ordinal);
        Assert.Contains("## `tests/Product.Tests.csproj`", projectsMap, StringComparison.Ordinal);
        Assert.Contains("role: tests; classification: `ProjectNameSuffix`", projectsMap, StringComparison.Ordinal);
        Assert.Contains("## `tests/Empty.Tests.csproj`", projectsMap, StringComparison.Ordinal);
        Assert.Contains("role: tests; classification: `ProjectFileNameSuffix`", projectsMap, StringComparison.Ordinal);
        Assert.Contains("`tests/Product.Tests.csproj` (tests)", projectsMap, StringComparison.Ordinal);
        Assert.Contains("Files: 0; types: 0.", projectsMap, StringComparison.Ordinal);
        var productionReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "code-size-candidates.md"));
        Assert.Contains("Effective options:", productionReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Finding origin:", productionReport, StringComparison.Ordinal);
        var mixedReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "mixed", "duplicate-analysis.md"));
        Assert.DoesNotContain("Finding origin:", mixedReport, StringComparison.Ordinal);
        Assert.Contains("L4", mixedReport, StringComparison.Ordinal);
        Assert.Contains("tests/ProductTests.cs:8", mixedReport, StringComparison.Ordinal);
        Assert.Contains("production", mixedReport, StringComparison.Ordinal);
        Assert.Contains("tests", mixedReport, StringComparison.Ordinal);
        Assert.Contains("flow-analysis.md", mixedReport, StringComparison.Ordinal);
    }

    private static ReviewConfig CreateConfig(string root, params IReviewAnalysis[] analyses) => CreateConfig(root, true, analyses);

    private static ReviewConfig CreateConfig(string root, bool enabled, params IReviewAnalysis[] analyses)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var registry = new ReviewAnalysisRegistry(analyses);
        var entries = string.Join(',', analyses.Select(analysis => "\"" + analysis.Descriptor.AnalysisId + "\":{" + (enabled ? string.Empty : "\"enabled\":false") + "}"));
        var config = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{" + entries + "}}";
        return new ReviewConfigValidator(registry).Validate(root, config);
    }

    private static FindingDraft Finding(string path, int line, string subject, string rationale, string snippet, string discriminator) => new(
        "Sample/Sample.csproj", path, subject, discriminator, line, rationale,
        new Dictionary<string, double> { ["zMetric"] = 2, ["aMetric"] = 1 },
        [
            new FindingEvidence(path, line, "B | label", "detail `text`", snippet),
            new FindingEvidence(path, line, "A | label", "earlier evidence", snippet),
        ]);

    private sealed class ReportAnalysis : IReviewAnalysis
    {
        internal ReportAnalysis(string id, string title, string optionValue)
        {
            Descriptor = new ReviewAnalysisDescriptor(id, title, 1, "A test purpose.", "A test measurement.", ["Review this result?"],
                [
                    ReviewAnalysisOptionDescriptor.String("scenario", "Scenario", optionValue),
                    ReviewAnalysisOptionDescriptor.String("zeta", "Last option", "z"),
                    ReviewAnalysisOptionDescriptor.String("alpha", "First option", "a"),
                ]);
        }

        public ReviewAnalysisDescriptor Descriptor { get; }

        public Task<ReviewAnalysisResult> ExecuteAsync(AiNetReview.Core.Analysis.ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(ReviewAnalysisResult.Empty);
    }
}
