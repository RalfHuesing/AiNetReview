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
using Microsoft.CodeAnalysis;

public sealed class MarkdownReportWriterTests
{
    [Fact]
    public async Task WriteAsync_UsesOriginalProjectAndLineForEveryAssignedTestOccurrence()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture analysis", "default");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string productionProject = "src/Sample_project.csproj";
        const string testProject = "tests/Sample.Tests.csproj";
        const string productionPath = "src/Widget.cs";
        const string testPath = "tests/WidgetTests.cs";
        const string areaId = "area-production-widget";
        const string packageId = "package-mixed-widget";
        var symbols = new[]
        {
            new FindingSymbol(productionProject, productionPath, "M:Sample.Widget`1.Run_with_under", 3, "fragment-a"),
            new FindingSymbol(productionProject, productionPath, "M:Sample.Widget`1.Run_with_under", 6, "fragment-b"),
            new FindingSymbol(testProject, testPath, "M:Sample.Tests.WidgetTests.Run", 17, "test-a"),
            new FindingSymbol(testProject, testPath, "M:Sample.Tests.WidgetTests.Run", 22, "test-b"),
        };
        var occurrences = new[]
        {
            new ReviewFindingOccurrence(symbols[0], ProjectRole.Production),
            new ReviewFindingOccurrence(symbols[1], ProjectRole.Production),
            new ReviewFindingOccurrence(symbols[2], ProjectRole.Tests),
            new ReviewFindingOccurrence(symbols[3], ProjectRole.Tests),
        };
        var draft = new FindingDraft(productionProject, productionPath, "M:Sample.Widget`1.Run_with_under", "shared-fragment", 4,
            "A shared fragment has two production and two test owner occurrences.", new Dictionary<string, double>(), [],
            relatedSymbols: symbols, subjectSymbols: symbols);
        var finding = new ReviewFinding(analysis.Descriptor.AnalysisId, draft, [productionPath, testPath], [], [productionPath, testPath])
        {
            Occurrences = occurrences,
            SubjectOccurrences = occurrences,
        };
        var productionArea = new AuditFindingArea(areaId, productionProject, ProjectRole.Production, "Widget", "type:Widget", null,
            [new AuditSourceLocation(productionPath, default, 1, 1, 9, 1)], "source type", [], []);
        var assignments = new[]
        {
            new AuditFindingAreaAssignment(areaId, ProjectRole.Production, productionProject, productionPath, symbols[0].SymbolId, symbols[0].Line, symbols[0].OccurrenceId, "production owner"),
            new AuditFindingAreaAssignment(areaId, ProjectRole.Production, productionProject, productionPath, symbols[1].SymbolId, symbols[1].Line, symbols[1].OccurrenceId, "production owner"),
            new AuditFindingAreaAssignment(areaId, ProjectRole.Tests, testProject, testPath, symbols[2].SymbolId, symbols[2].Line, symbols[2].OccurrenceId, "one direct production type reference"),
            new AuditFindingAreaAssignment(areaId, ProjectRole.Tests, testProject, testPath, symbols[3].SymbolId, symbols[3].Line, symbols[3].OccurrenceId, "one direct production type reference"),
        };
        var packaged = new AuditPackagedFinding("finding-mixed-fragment", finding, [areaId], assignments);
        var projectId = ProjectId.CreateNewId();
        var references = Enumerable.Range(1, 130).Select(line => new AuditSourceReference(
            projectId, productionPath, "type:Widget", ProjectRole.Production, projectId, "type:Caller",
            "M:Sample.Caller.Invoke", ProjectRole.Production, SolutionSymbolReferenceKind.Direct,
            new AuditSourceLocation(productionPath, new Microsoft.CodeAnalysis.Text.TextSpan(line, 2), line, 1, line, 2)))
            .Concat(Enumerable.Range(1, 130).Select(line => new AuditSourceReference(
                projectId, productionPath, "type:OtherSource", ProjectRole.Production, projectId, "type:Caller" + line,
                "M:Sample.Caller.Invoke", ProjectRole.Production, SolutionSymbolReferenceKind.MethodGroup,
                new AuditSourceLocation(productionPath, new Microsoft.CodeAnalysis.Text.TextSpan(line + 200, 2), line + 200, 1, line + 200, 2))))
            .Append(new AuditSourceReference(projectId, productionPath, "type:OtherSource", ProjectRole.Production, projectId,
                "type:LargeTarget", "M:Other.Target." + new string('X', 17_000), ProjectRole.Production,
                SolutionSymbolReferenceKind.Direct, new AuditSourceLocation(productionPath, new Microsoft.CodeAnalysis.Text.TextSpan(500, 2), 500, 1, 500, 2)))
            .ToArray();
        var uncertainties = new[]
        {
            new AuditSourceUncertainty(projectId, productionPath, "type:Widget", ProjectRole.Production,
                "type:Unknown", "M:Sample.Unknown.Run", "ambiguous binding", new AuditSourceLocation(productionPath,
                    new Microsoft.CodeAnalysis.Text.TextSpan(140, 2), 140, 1, 140, 2)),
        };
        var package = new AuditFindingPackage(packageId, [productionArea], [packaged], [], references, uncertainties, []);
        var view = new AuditFindingPackageView(true, [productionArea], [package], [], 1, true)
        {
            ProjectPaths = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [projectId.ToString()] = productionProject,
            },
        };
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([draft]))])
        {
            Findings = [finding],
            HasCSharpSnapshotChanges = true,
            AuditPackages = new AuditFindingPackageViews(view, view with { IsChangedFiles = false }),
        };

        var published = await new MarkdownReportWriter().WriteAsync(config, result);
        var packageMarkdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, published.RunId,
            "audit-map", "changed-files", packageId + ".md"));
        AssertMarkdownReportLinksResolve(Path.Combine(config.ResolvedOutputDirectory, published.RunId));

        Assert.Contains("[src/Widget.cs:3](../../../../src/Widget.cs#L3): production owner (occurrence `fragment-a`) — project `src/Sample_project.csproj`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("[src/Widget.cs:6](../../../../src/Widget.cs#L6): production owner (occurrence `fragment-b`) — project `src/Sample_project.csproj`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("[tests/WidgetTests.cs:17](../../../../tests/WidgetTests.cs#L17): one direct production type reference (occurrence `test-a`) — project `tests/Sample.Tests.csproj`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("[tests/WidgetTests.cs:22](../../../../tests/WidgetTests.cs#L22): one direct production type reference (occurrence `test-b`) — project `tests/Sample.Tests.csproj`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("Widget.cs:3", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("Widget.cs:6", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("### finding-mixed-fragment — `fixture-analysis`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("Original finding, including rationale, metrics and evidence:", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("Direct source references: **261 locations** in **132 groups**", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("Browse all reference groups:", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("binding uncertainty origins: **1**", packageMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("at [src/Widget.cs:1:1]", packageMarkdown, StringComparison.Ordinal);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, published.RunId);
        var referencePages = Directory.GetFiles(Path.Combine(runDirectory, "audit-map", "changed-files"), packageId + "-references-*.md");
        Assert.True(referencePages.Length > 1);
        var referenceEntries = new List<string>();
        foreach (var referencePage in referencePages)
        {
            var page = await File.ReadAllTextAsync(referencePage);
            Assert.DoesNotContain("Full-audit scope:", page, StringComparison.Ordinal);
            Assert.Contains($"[Back to package]({packageId}.md)", page, StringComparison.Ordinal);
            Assert.True(page.Split("\n- Source project ", StringSplitOptions.None).Length - 1 <= AuditMapReportWriter.ReferencePageEntryLimit);
            referenceEntries.AddRange(page.Split("\n- Source project ", StringSplitOptions.RemoveEmptyEntries).Skip(1));
            if (Encoding.UTF8.GetByteCount(page) > 16 * 1024)
            {
                Assert.Contains("This complete single reference record exceeds the 16-KiB page target", page, StringComparison.Ordinal);
            }
        }
        Assert.Equal(262, referenceEntries.Count);
        var referenceText = string.Join('\n', referenceEntries);
        Assert.Contains("[src/Widget.cs:130:1]", referenceText, StringComparison.Ordinal);
        Assert.Contains("`src/Sample_project.csproj`", referenceText, StringComparison.Ordinal);
        Assert.Contains("source `type:Widget`; target project `src/Sample_project.csproj`; target `type:Caller`; member `M:Sample.Caller.Invoke`; roles Production → Production; binding Direct; span 130..132; location 130:1–130:2", referenceText, StringComparison.Ordinal);
        foreach (var line in Enumerable.Range(1, 130))
        {
            Assert.Contains($"span {line}..{line + 2}; location {line}:1–{line}:2", referenceText, StringComparison.Ordinal);
        }
        foreach (var line in Enumerable.Range(201, 130))
        {
            Assert.Contains($"target `type:Caller{line - 200}`", referenceText, StringComparison.Ordinal);
            Assert.Contains("source `type:OtherSource`", referenceText, StringComparison.Ordinal);
            Assert.Contains($"span {line}..{line + 2}; location {line}:1–{line}:2", referenceText, StringComparison.Ordinal);
        }
        var groupIndexPages = Directory.GetFiles(Path.Combine(runDirectory, "audit-map", "changed-files"), packageId + "-reference-groups-*.md");
        Assert.NotEmpty(groupIndexPages);
        var groupRows = string.Join('\n', await Task.WhenAll(groupIndexPages.Select(path => File.ReadAllTextAsync(path))))
            .Split(" source locations**; details:", StringSplitOptions.None).Length - 1;
        Assert.Equal(133, groupRows);
        Assert.Contains("This complete single reference record exceeds the 16-KiB page target",
            string.Join('\n', await Task.WhenAll(referencePages.Select(path => File.ReadAllTextAsync(path)))), StringComparison.Ordinal);

        var countLimitedPages = AuditMapReportWriter.PartitionReferenceEntries(Enumerable.Repeat("x", 129).ToArray(),
            static (_, entries) => string.Concat(entries));
        Assert.Equal([128, 1], countLimitedPages.Select(static page => page.Count));
        Assert.Equal(Enumerable.Repeat("x", 129), countLimitedPages.SelectMany(static page => page));

        var orderedRoot = temp.GetPath("ordered-map");
        var reversedRoot = temp.GetPath("reversed-map");
        await AuditMapReportWriter.WriteAuditMapAsync(orderedRoot, "fixed-run", config.ProjectRoot,
            new AuditFindingPackageViews(view, view with { IsChangedFiles = false }), "changed-files", CancellationToken.None);
        var reversedPackage = package with
        {
            DirectReferences = package.DirectReferences.Reverse().ToArray(),
            DirectUncertainties = package.DirectUncertainties.Reverse().ToArray(),
        };
        var reversedView = view with { Packages = [reversedPackage] };
        await AuditMapReportWriter.WriteAuditMapAsync(reversedRoot, "fixed-run", config.ProjectRoot,
            new AuditFindingPackageViews(reversedView, reversedView with { IsChangedFiles = false }), "changed-files", CancellationToken.None);
        var orderedFiles = Directory.GetFiles(Path.Combine(orderedRoot, "audit-map", "changed-files"), "*.md")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        var reversedFiles = Directory.GetFiles(Path.Combine(reversedRoot, "audit-map", "changed-files"), "*.md")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        Assert.Equal(orderedFiles.Select(Path.GetFileName), reversedFiles.Select(Path.GetFileName));
        Assert.Equal(await Task.WhenAll(orderedFiles.Select(path => File.ReadAllTextAsync(path))),
            await Task.WhenAll(reversedFiles.Select(path => File.ReadAllTextAsync(path))));
        var allReferencePages = Directory.GetFiles(Path.Combine(runDirectory, "audit-map", "all-findings"), packageId + "-references-*.md");
        Assert.NotEmpty(allReferencePages);
        var allReferenceContent = string.Join('\n', await Task.WhenAll(allReferencePages.Select(path => File.ReadAllTextAsync(path))));
        Assert.Contains("ambiguous binding", allReferenceContent, StringComparison.Ordinal);
        Assert.Contains("origin project/type `src/Sample_project.csproj` / `type:Widget`; role Production", allReferenceContent, StringComparison.Ordinal);
        Assert.Contains("candidate type `type:Unknown`", allReferenceContent, StringComparison.Ordinal);
        Assert.Contains("candidate symbol `M:Sample.Unknown.Run`", allReferenceContent, StringComparison.Ordinal);
        Assert.Contains("span 140..142; location 140:1–140:2", allReferenceContent, StringComparison.Ordinal);
        foreach (var referencePage in allReferencePages)
        {
            Assert.Contains("Full-audit scope:", await File.ReadAllTextAsync(referencePage), StringComparison.Ordinal);
        }
        var allGroupPages = Directory.GetFiles(Path.Combine(runDirectory, "audit-map", "all-findings"), packageId + "-reference-groups-*.md");
        Assert.NotEmpty(allGroupPages);
        foreach (var groupPage in allGroupPages)
        {
            var page = await File.ReadAllTextAsync(groupPage);
            Assert.Contains("Full-audit scope:", page, StringComparison.Ordinal);
            Assert.Contains($"[Back to package]({packageId}.md)", page, StringComparison.Ordinal);
            Assert.True(page.Split("\n- ", StringSplitOptions.None).Length - 1 <= AuditMapReportWriter.ReferencePageEntryLimit);
            if (Encoding.UTF8.GetByteCount(page) > 16 * 1024)
            {
                Assert.Contains("This complete group summary exceeds the 16-KiB page target", page, StringComparison.Ordinal);
            }
        }
        Assert.Contains("``M:Sample.Widget`1.Run_with_under``", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("`src/Sample_project.csproj`", packageMarkdown, StringComparison.Ordinal);
        var symbolNavigation = packageMarkdown.Split("## Symbol navigation\n\n", StringSplitOptions.None)[1].Split("\n## ", StringSplitOptions.None)[0];
        Assert.Contains("``M:Sample.Widget`1.Run_with_under`` — [src/Widget.cs:3]", symbolNavigation, StringComparison.Ordinal);
        Assert.Contains("src/Sample_project.csproj", symbolNavigation, StringComparison.Ordinal);
    }

    private static void AssertMarkdownReportLinksResolve(string runDirectory)
    {
        var linkPattern = new Regex(@"\]\((?<target>[^)#]+\.md)(?:#[^)]+)?\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var reportPath in Directory.EnumerateFiles(Path.Combine(runDirectory, "audit-map"), "*.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(reportPath);
            foreach (Match link in linkPattern.Matches(content))
            {
                var target = Uri.UnescapeDataString(link.Groups["target"].Value);
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, target.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(resolved), $"Markdown report link does not resolve: '{target}' from '{reportPath}'.");
            }
        }
    }

    [Fact]
    public async Task WriteAsync_LabelsFileFallbackReferencesAsOutgoingFromTheirPrimaryFileArea()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture analysis", "default");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string sourceProjectPath = "Sample/Sample.csproj";
        const string sourcePath = "src/Entry.cs";
        const string targetProjectPath = "Other/Other.csproj";
        var sourceProjectId = ProjectId.CreateNewId();
        var targetProjectId = ProjectId.CreateNewId();
        var draft = new FindingDraft(sourceProjectPath, sourcePath, "file:Entry.cs", "file", 1, "File-level signal.", new Dictionary<string, double>(), []);
        var finding = new ReviewFinding(analysis.Descriptor.AnalysisId, draft, [sourcePath], [], [sourcePath])
        {
            SubjectOccurrences = [new ReviewFindingOccurrence(new FindingSymbol(sourceProjectPath, sourcePath, "file:Entry.cs", 1), ProjectRole.Production)],
        };
        var area = new AuditFindingArea("area-entry-file", sourceProjectPath, ProjectRole.Production, "Entry.cs", null, sourcePath, [], "file fallback", [], []);
        var packaged = new AuditPackagedFinding("finding-entry-file", finding, [area.Id],
            [new AuditFindingAreaAssignment(area.Id, ProjectRole.Production, sourceProjectPath, sourcePath, "file:Entry.cs", 1, null, "file fallback")]);
        var reference = new AuditSourceReference(sourceProjectId, sourcePath, null, ProjectRole.Production,
            targetProjectId, "type:Other.Target", "M:Other.Target.Run", ProjectRole.Production,
            SolutionSymbolReferenceKind.Direct, new AuditSourceLocation(sourcePath, new Microsoft.CodeAnalysis.Text.TextSpan(0, 8), 1, 1, 1, 9));
        var package = new AuditFindingPackage("package-entry-file", [area], [packaged], [], [reference], [], []);
        var view = new AuditFindingPackageView(true, [area], [package], [], 1, true)
        {
            ProjectPaths = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [sourceProjectId.ToString()] = sourceProjectPath,
                [targetProjectId.ToString()] = targetProjectPath,
            },
        };
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([draft]))])
        {
            Findings = [finding],
            AuditPackages = new AuditFindingPackageViews(view, view with { IsChangedFiles = false }),
        };

        var published = await new MarkdownReportWriter().WriteAsync(config, result);
        var packageMarkdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, published.RunId,
            "audit-map", "changed-files", package.Id + ".md"));

        Assert.Contains("outgoing: `src/Entry.cs`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("`Sample/Sample.csproj`", packageMarkdown, StringComparison.Ordinal);
        Assert.Contains("`Other/Other.csproj`", packageMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("incoming: `src/Entry.cs`", packageMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_PublishesMarkdownAuditMapsWithDirectFindingAnchorsAndStrictChangedScope()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture analysis", "default");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var draft = new FindingDraft("Sample/Sample.csproj", "src/Sample.cs", "M:Sample.Widget.Run", "member", 4,
            "The method has a long branch path; binding attribution is uncertain.", new Dictionary<string, double> { ["decisionCount"] = 9 },
            [new FindingEvidence("src/Sample.cs", 4, "M:Sample.Widget.Run", "branch evidence", "if (ready)")]);
        var reviewFinding = new ReviewFinding(analysis.Descriptor.AnalysisId, draft, ["src/Sample.cs"], [], ["src/Sample.cs"])
        {
            SubjectOccurrences = [new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "src/Sample.cs", "M:Sample.Widget.Run", 4), ProjectRole.Production)],
        };
        var excludedDraft = new FindingDraft("Sample/Sample.csproj", "src/Other.cs", "M:Sample.Other.Run", "excluded", 12,
            "EXCLUDED_DETAIL_MUST_NOT_APPEAR_IN_CHANGED_VIEW", new Dictionary<string, double>(), []);
        var excludedFinding = new ReviewFinding(analysis.Descriptor.AnalysisId, excludedDraft, ["src/Other.cs"], [], [])
        {
            SubjectOccurrences = [new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "src/Other.cs", "M:Sample.Other.Run", 12), ProjectRole.Production)],
        };
        const string areaId = "area-type-test";
        const string contextId = "area-context-test";
        const string packageId = "package-test";
        var area = new AuditFindingArea(areaId, "Sample/Sample.csproj", ProjectRole.Production, "Widget", "type:Widget", null,
            [new AuditSourceLocation("src/Sample.cs", default, 2, 1, 8, 1)], "source type", [], []);
        var contextArea = new AuditFindingArea(contextId, "Sample/Sample.csproj", ProjectRole.Production, "Caller", null, "src/Caller.cs",
            [], "file fallback", [], [areaId]);
        var packagedFinding = new AuditPackagedFinding("finding-stable", reviewFinding, [areaId],
            [new AuditFindingAreaAssignment(areaId, ProjectRole.Production, "Sample/Sample.csproj", "src/Sample.cs", "M:Sample.Widget.Run", 4, null, "subject symbol belongs to source type")]);
        var package = new AuditFindingPackage(packageId, [area], [packagedFinding], [], [], [], []);
        var excludedArea = new AuditFindingArea("area-other-test", "Sample/Sample.csproj", ProjectRole.Production, "Other", "type:Other", null,
            [new AuditSourceLocation("src/Other.cs", default, 10, 1, 14, 1)], "source type", [], []);
        var excludedPackagedFinding = new AuditPackagedFinding("finding-excluded", excludedFinding, [excludedArea.Id],
            [new AuditFindingAreaAssignment(excludedArea.Id, ProjectRole.Production, "Sample/Sample.csproj", "src/Other.cs", "M:Sample.Other.Run", 12, null, "subject symbol belongs to source type")]);
        var excludedPackage = new AuditFindingPackage("package-excluded", [excludedArea], [excludedPackagedFinding], [], [], [], []);
        var changedView = new AuditFindingPackageView(true, [area, contextArea], [package], [], 1, true);
        var allView = new AuditFindingPackageView(false, [area, contextArea, excludedArea], [package, excludedPackage],
            [new AuditPackageContextArea(contextArea, true, packageId)], 2, true);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([draft, excludedDraft]))])
        {
            Findings = [reviewFinding, excludedFinding],
            HasCSharpSnapshotChanges = true,
            AuditPackages = new AuditFindingPackageViews(changedView, allView),
        };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var changedIndexPath = Path.Combine(runDirectory, "audit-map", "changed-files", "index.md");
        var changedPackagePath = Path.Combine(runDirectory, "audit-map", "changed-files", packageId + ".md");
        var allIndexPath = Path.Combine(runDirectory, "audit-map", "all-findings", "index.md");
        var allPackagePath = Path.Combine(runDirectory, "audit-map", "all-findings", packageId + ".md");
        Assert.True(File.Exists(changedIndexPath));
        Assert.True(File.Exists(changedPackagePath));
        Assert.True(File.Exists(allIndexPath));
        Assert.True(File.Exists(allPackagePath));
        Assert.False(File.Exists(Path.Combine(runDirectory, "audit-map", "changed-files", "package-excluded.md")));
        var changedIndex = await File.ReadAllTextAsync(changedIndexPath);
        Assert.DoesNotContain("EXCLUDED_DETAIL_MUST_NOT_APPEAR_IN_CHANGED_VIEW", changedIndex, StringComparison.Ordinal);
        var allPackageMarkdown = await File.ReadAllTextAsync(allPackagePath);
        Assert.Contains("Unique findings: **1**", changedIndex, StringComparison.Ordinal);
        Assert.Contains("[package-test](package-test.md)", changedIndex, StringComparison.Ordinal);
        Assert.Contains("Context only", await File.ReadAllTextAsync(allIndexPath), StringComparison.Ordinal);
        Assert.Contains("Unique findings: **2**", await File.ReadAllTextAsync(allIndexPath), StringComparison.Ordinal);
        Assert.Contains("Original finding, including rationale, metrics and evidence:", allPackageMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Original rationale:", allPackageMarkdown, StringComparison.Ordinal);
        Assert.Contains("### finding-stable — `fixture-analysis`", allPackageMarkdown, StringComparison.Ordinal);
        Assert.Contains("#finding-finding-stable", allPackageMarkdown, StringComparison.Ordinal);
        Assert.Contains("## File navigation", allPackageMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Symbol navigation", allPackageMarkdown, StringComparison.Ordinal);
        Assert.Contains($"Run: `{report.RunId}`", allPackageMarkdown, StringComparison.Ordinal);
        Assert.Contains("Full-audit scope: This reference view contains every current finding. Inspect it only when the user explicitly requests a full-repository audit.", allPackageMarkdown, StringComparison.Ordinal);
        var changedPackageMarkdown = await File.ReadAllTextAsync(changedPackagePath);
        Assert.DoesNotContain("Full-audit scope:", changedPackageMarkdown, StringComparison.Ordinal);
        var originalReportPath = Path.Combine(runDirectory, "production", "all-findings", "fixture-analysis.md");
        Assert.True(File.Exists(originalReportPath));
        Assert.Equal(1, (await File.ReadAllTextAsync(originalReportPath)).Split("<a id=\"finding-finding-stable\"></a>", StringSplitOptions.None).Length - 1);
        var original = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "fixture-analysis.md"));
        Assert.Contains("<a id=\"finding-finding-stable\"></a>", original, StringComparison.Ordinal);
        Assert.Contains($"Run: `{report.RunId}`; view: `changed-files`", changedIndex, StringComparison.Ordinal);
        Assert.DoesNotContain($"Run: `{report.RunId.Replace("-", "\\-", StringComparison.Ordinal)}`", changedIndex, StringComparison.Ordinal);
        var rootIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        Assert.Contains("(audit-map/changed-files/index.md)", rootIndex, StringComparison.Ordinal);
        Assert.Contains("Do not claim a full audit", allPackageMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RendersEveryStructuralOccurrenceWithFramedPathsAndExclusiveCoordinates()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new StructuralDuplicationCandidatesAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string projectPath = "Product; One/[β]`special.csproj";
        const string sourcePath = "src/file ; [x]`β.cs";
        const string owner = "M:Product.Sample.Run(System.Int32)";
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
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId,
            "production", "all-findings", "structural-duplication-candidates.md"));

        Assert.Contains("Structural duplicate: 3 occurrences in 2 executable members; 3 statements / 60 tokens; identical after local/parameter normalization.", markdown, StringComparison.Ordinal);
        Assert.Contains("Containment suppression removes a smaller fragment only when every occurrence is contained in an occurrence of a larger qualifying group; a smaller group with any additional occurrence remains reportable.", markdown, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: ", markdown, StringComparison.Ordinal);
        Assert.Contains("(1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains(projectPath, markdown, StringComparison.Ordinal);
        Assert.Contains("[src/file", markdown, StringComparison.Ordinal);
        Assert.Contains("](../../../../src/file%20%3B%20%5Bx%5D%60%CE%B2.cs)", markdown, StringComparison.Ordinal);
        Assert.Contains("``Product; One/[β]`special.csproj``", markdown, StringComparison.Ordinal);
        Assert.Contains("start `3:5`; end-exclusive `7:10`", markdown, StringComparison.Ordinal);
        Assert.Contains("start `4:1`; end-exclusive `8:2`", markdown, StringComparison.Ordinal);
        Assert.Contains("start `9:2`; end-exclusive `12:1`", markdown, StringComparison.Ordinal);
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
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "production", "all-findings", "non-ascii-identifiers.md"));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("- `T:Sample.BestätigungsService`", markdown, StringComparison.Ordinal);
        Assert.Contains("Signal: The type identifier 'BestätigungsService' contains non\\-ASCII characters (e.g. 'ä').", markdown, StringComparison.Ordinal);
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
        ]));

        var indexBytes = await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        Assert.Equal(9, Directory.GetFiles(runDirectory, "*", SearchOption.AllDirectories).Length);
        Assert.Contains("No findings are assigned in this view.", await File.ReadAllTextAsync(Path.Combine(runDirectory, "audit-map", "changed-files", "index.md")), StringComparison.Ordinal);
        Assert.Contains("No findings are assigned in this view.", await File.ReadAllTextAsync(Path.Combine(runDirectory, "audit-map", "all-findings", "index.md")), StringComparison.Ordinal);
        Assert.False(indexBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', indexBytes);
        var index = Encoding.UTF8.GetString(indexBytes);
        Assert.Contains($"# AiNetReview – {report.RunId}", index, StringComparison.Ordinal);
        Assert.Contains("- Run ID:", index, StringComparison.Ordinal);
        var repositoryLine = Assert.Single(index.Split('\n').Where(static line => line.StartsWith("- Repository:", StringComparison.Ordinal)));
        var repositoryPath = repositoryLine["- Repository: `".Length..^1].Replace("\\\\", "\\", StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(repositoryPath));
        Assert.Contains("- Solution: `Sample.slnx`", index, StringComparison.Ordinal);
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

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("No review was performed because all analyses are disabled.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("No findings were found", index, StringComparison.Ordinal);
        Assert.Equal(9, Directory.GetFiles(runDirectory, "*", SearchOption.AllDirectories).Length);
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
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var analysisReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "has-findings.md"));
        var changedReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "changed-files", "has-findings.md"));

        Assert.Contains("[production changed-files (2)](production/changed-files/index.md)", index, StringComparison.Ordinal);
        Assert.Contains("[production all-findings (2)](production/all-findings/index.md)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Empty Review analysis", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "all-findings", "index.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "changed-files", "index.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "production", "all-findings", "empty-analysis.md")));
        Assert.Contains("## Findings", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Total findings: 2 across 2 projects and 2 source files.", analysisReport, StringComparison.Ordinal);
        Assert.Contains("### Project: Other/Sample.csproj (role not represented; 1 files, 1 findings)", analysisReport, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample.cs (1 findings)", analysisReport, StringComparison.Ordinal);
        Assert.Contains("### Project: Sample/Sample.csproj (role not represented; 1 files, 1 findings)", analysisReport, StringComparison.Ordinal);
        Assert.DoesNotContain("| Project | Source file | Findings |", analysisReport, StringComparison.Ordinal);
        Assert.Contains("- `C:Sample`", analysisReport, StringComparison.Ordinal);
        Assert.Contains("- `C:Other`", analysisReport, StringComparison.Ordinal);
        Assert.Contains("A normal unbounded audit includes all three areas;", changedReport, StringComparison.Ordinal);
        Assert.Contains("[root index's Review guidance](../../index.md#review-guidance)", changedReport, StringComparison.Ordinal);
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
        var markdownPath = Path.Combine(config.ResolvedOutputDirectory, report.RunId, "production", "all-findings", "fixture-analysis.md");
        var markdownBytes = await File.ReadAllBytesAsync(markdownPath);
        var markdown = Encoding.UTF8.GetString(markdownBytes);

        Assert.Contains("## Summary\n\nTotal findings: 4 across 2 projects and 2 source files.", markdown, StringComparison.Ordinal);
        Assert.Contains("### Project: Another.csproj (role not represented; 1 files, 1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("### Project: Project\\|One/β\\`name.csproj (role not represented; 1 files, 3 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: src\\|folder/line break\\`file.cs (1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: src\\|folder/line break\\`file.cs (3 findings)", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("first\\|signal", StringComparison.Ordinal) < markdown.IndexOf("second\\|signal", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("second\\|signal", StringComparison.Ordinal) < markdown.IndexOf("last\\|signal", StringComparison.Ordinal));
        Assert.DoesNotContain("`src|folder/line", markdown, StringComparison.Ordinal);
        Assert.Equal(4, markdown.Split("- Signal:", StringSplitOptions.None).Length - 1);
        Assert.False(markdownBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', markdownBytes);
    }

    [Fact]
    public async Task WriteAsync_SortsFindingsAndEscapesContentAndSourceLinks()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("fixture-analysis", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var z = Finding("z file#1.cs", 9, "Z", "last|rationale", "second`snippet", "zeta");
        var a = Finding("a file#1.cs", 3, "A", "first | rationale", "`snippet`", "alpha");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([z, a])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "production", "all-findings", "fixture-analysis.md"));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("- `A`", StringComparison.Ordinal) < markdown.IndexOf("- `Z`", StringComparison.Ordinal));
        Assert.Contains("\\| rationale", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: a file\\#1.cs (1 findings)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("aMetric", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Metrics", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("detail", markdown, StringComparison.Ordinal);
        Assert.Contains("Effective options:", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("\"alpha\"", StringComparison.Ordinal) < markdown.IndexOf("\"scenario\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\\{", markdown, StringComparison.Ordinal);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        Assert.Contains("Review guidance", index, StringComparison.Ordinal);
        Assert.Contains("Investigate every finding in the commissioned working set", index, StringComparison.Ordinal);
        Assert.Contains("Related findings may be evaluated together.", index, StringComparison.Ordinal);
        Assert.Contains("Do not dismiss a signal solely because it is heuristic or its attribution is uncertain.", index, StringComparison.Ordinal);
        Assert.Contains("Justify each classification with concrete evidence: false positive, acceptable design, needs clarification, or actionable.", index, StringComparison.Ordinal);
        Assert.Contains("A signal alone does not require a change; changes must follow from this assessment.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("All findings may validly result in no changes.", index, StringComparison.Ordinal);
        Assert.Contains("An accurate signal can describe an acceptable design; distinguish that from a false positive.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("First remove only clear false positives", index, StringComparison.Ordinal);
        Assert.True(index.IndexOf("## Review guidance", StringComparison.Ordinal) < index.IndexOf("## Audit scope", StringComparison.Ordinal));
        Assert.Contains("Set a new baseline", index, StringComparison.Ordinal);
        Assert.Contains($" baseline '{config.ProjectRoot}'", index, StringComparison.Ordinal);
        Assert.DoesNotContain("--cmd", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_LinksEveryClusterMemberToItsSourceLocationAndAddsRootReportLinks()
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
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var markdown = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "duplicate-code-candidates.md"));
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("Total findings: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("#### File: Product/First.cs (1 findings)", markdown, StringComparison.Ordinal);
        Assert.Contains("Total findings: 1 across 1 projects and 1 source files.", markdown, StringComparison.Ordinal);
        Assert.Contains("[Product/First.cs](../../../../Product/First.cs): `M:First.Run` (line 2; production)", markdown, StringComparison.Ordinal);
        Assert.Contains("[Other/Second \\# \\{sample\\}.cs](../../../../Other/Second%20%23%20%7Bsample%7D.cs): `M:Second.Run` (line 5; production)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Project | Source file | Findings |", markdown, StringComparison.Ordinal);
        Assert.Contains("(production/changed-files/index.md)", index, StringComparison.Ordinal);
        Assert.Contains("(production/all-findings/index.md)", index, StringComparison.Ordinal);
        Assert.Contains("Agent instruction:** Do not inspect", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "all-findings", "duplicate-code-candidates.md")));
        var allFindingsIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "index.md"));
        Assert.Contains("Notice for AI agents:**", allFindingsIndex, StringComparison.Ordinal);
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
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var allFindings = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "indirection-drift-candidates.md"));
        var changedFiles = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "changed-files", "indirection-drift-candidates.md"));

        Assert.Contains("This area is reference-only; inspect or report it only when the user explicitly requests a full repository audit.", allFindings, StringComparison.Ordinal);
        Assert.Contains("A normal unbounded audit includes all three areas;", changedFiles, StringComparison.Ordinal);
        Assert.Equal(allFindings.Replace(
            "This area is reference-only; inspect or report it only when the user explicitly requests a full repository audit. See the [root index's Review guidance](../../index.md#review-guidance).",
            "A normal unbounded audit includes all three areas; see the [root index's Review guidance](../../index.md#review-guidance).",
            StringComparison.Ordinal), changedFiles);
        Assert.Contains("Total findings: 1", allFindings, StringComparison.Ordinal);
        Assert.Contains("#### File: ZApi.cs (1 findings)", allFindings, StringComparison.Ordinal);
        Assert.Contains("- Forwarding path: 2 forwarding edges across 3 types and 3 files", allFindings, StringComparison.Ordinal);
        Assert.True(allFindings.IndexOf("[ZApi.cs](../../../../ZApi.cs):1: `M:ZApi.Run(System.Int32)`", StringComparison.Ordinal)
            < allFindings.IndexOf("[BService.cs](../../../../BService.cs):1: `M:BService.Run(System.Int32)`", StringComparison.Ordinal));
        Assert.True(allFindings.IndexOf("[BService.cs](../../../../BService.cs):1: `M:BService.Run(System.Int32)`", StringComparison.Ordinal)
            < allFindings.IndexOf("[ARepository.cs](../../../../ARepository.cs):1: `M:ARepository.Run(System.Int32)`", StringComparison.Ordinal));
        Assert.DoesNotContain("Cluster:", allFindings, StringComparison.Ordinal);
        Assert.DoesNotContain("return BService.Run(value)", allFindings, StringComparison.Ordinal);
        Assert.DoesNotContain("#L1", allFindings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RendersMissingTestEvidenceCategoriesPathsLimitsAndSnapshotSelection()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new MissingTestEvidenceCandidatesAnalysis();
        var otherAnalysis = new ReportAnalysis("other-analysis", "Other Analysis", "file based selection");
        var config = CreateConfig(temp.DirectoryPath, analysis, otherAnalysis);
        const string testPathMethodId = "M:Sample.Tests.Tests`1.CallsApi(System.Collections.Generic.Dictionary{System.String,System.Int32})";
        const string testPathSource = "Sample.Tests/Test`Root{V1}.cs";
        var noPath = new FindingDraft("Sample/Sample.csproj", "Sample/NoPath.cs", "M:Sample.NoPath.Run", "no-static-test-path", 2,
            "no static test path", new Dictionary<string, double>
            {
                ["decisionCount"] = 3, ["maxDecisionNesting"] = 2, ["attributionUncertain"] = 1,
            },
            [new FindingEvidence("Sample/NoPath.cs", 2, "Declaration", "Eligible candidate", "Run")]);
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
        var otherFinding = Finding("Sample/Other.cs", 1, "M:Sample.Other.Run", "unchanged analysis finding", "Run", "case");
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([noPath, indirect])),
            new ReviewAnalysisRunResult(otherAnalysis.Descriptor.AnalysisId, new ReviewAnalysisResult([otherFinding])),
        ])
        {
            Findings = [
                new ReviewFinding(analysis.Descriptor.AnalysisId, noPath, ["Sample/NoPath.cs"], [], ["Sample/NoPath.cs"]),
                new ReviewFinding(analysis.Descriptor.AnalysisId, indirect, ["Sample/Indirect.cs", "Sample/Api.cs", "Sample.Tests/Test`Root{V1}.cs"], [], ["Sample.Tests/Tests.cs"])
                {
                    Occurrences = [
                        new ReviewFindingOccurrence(new FindingSymbol("Sample.Tests/Sample.Tests.csproj", testPathSource, testPathMethodId, 3), ProjectRole.Tests),
                        new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "Sample/Api.cs", "M:Sample.Api.Run", 8), ProjectRole.Production),
                        new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", 4), ProjectRole.Production),
                    ],
                    SubjectOccurrences = [new ReviewFindingOccurrence(new FindingSymbol("Sample/Sample.csproj", "Sample/Indirect.cs", "M:Sample.Indirect.Run", 4), ProjectRole.Production)],
                },
                new ReviewFinding(otherAnalysis.Descriptor.AnalysisId, otherFinding, ["Sample/Other.cs"], [], []),
            ],
            ProjectClassifications = [
                new ProjectClassification("Sample/Sample.csproj", ProjectRole.Production, ProjectClassificationReason.NoTestMarker),
                new ProjectClassification("Sample.Tests/Sample.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix),
            ],
            HasCSharpSnapshotChanges = false,
        };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var allFindings = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "missing-test-evidence-candidates.md"));
        var changedIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "changed-files", "index.md"));
        var rootIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("no static test path; 3 decisions, nesting 2; attribution uncertain", allFindings, StringComparison.Ordinal);
        Assert.Contains("indirect test path only; 5 decisions, nesting 3", allFindings, StringComparison.Ordinal);
        Assert.Contains("The `attribution uncertain` marker means the static test association may be incomplete", allFindings, StringComparison.Ordinal);
        Assert.Contains("It does not assess test assertion quality.", allFindings, StringComparison.Ordinal);
        Assert.Contains("A production function meets the nontrivial gate when `decisionCount >= minDecisionCount OR maxDecisionNesting >= minDecisionNesting`.", allFindings, StringComparison.Ordinal);
        Assert.Contains("an indirect-path-only function must also meet `decisionCount >= minIndirectDecisionCount OR maxDecisionNesting >= minIndirectDecisionNesting`", allFindings, StringComparison.Ordinal);
        Assert.Contains("A reachable global uncertainty input can mark every function", allFindings, StringComparison.Ordinal);
        Assert.Contains("neither means a test is missing nor that the marked function itself has an unresolved binding", allFindings, StringComparison.Ordinal);
        Assert.Contains("each switch section or switch-expression arm once", allFindings, StringComparison.Ordinal);
        Assert.Contains("`&&`, `||`, and `??` do not add decisions", allFindings, StringComparison.Ordinal);
        Assert.Contains("Shortest resolved test path:", allFindings, StringComparison.Ordinal);
        var shortestPathLine = Assert.Single(allFindings.Split('\n').Where(static line => line.StartsWith("  - Shortest resolved test path:", StringComparison.Ordinal)));
        Assert.Equal($"  - Shortest resolved test path: ``{testPathMethodId}`` (``{testPathSource}:3``; tests) -> `M:Sample.GeneratedIntermediate.Run` (`Sample/Generated/Worker.g.cs:2`; production) -> `M:Sample.Api.Run` (`Sample/Api.cs:8`; production) -> `M:Sample.Indirect.Run` (`Sample/Indirect.cs:4`; production)", shortestPathLine);
        Assert.Contains("Reflection, dependency injection, external test projects, dynamic dispatch, branch execution, and custom test discovery", allFindings, StringComparison.Ordinal);
        Assert.Contains("No findings in this view.", changedIndex, StringComparison.Ordinal);
        Assert.Contains("shows every current finding when any C# path was added, changed, or deleted", rootIndex, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "all-findings", "index.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "production", "changed-files", "missing-test-evidence-candidates.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "all-findings", "other-analysis.md")));

        var changedSnapshotReport = await new MarkdownReportWriter().WriteAsync(config, result with { HasCSharpSnapshotChanges = true });
        var changedSnapshotDirectory = Path.Combine(config.ResolvedOutputDirectory, changedSnapshotReport.RunId);
        var selected = await File.ReadAllTextAsync(Path.Combine(changedSnapshotDirectory, "production", "changed-files", "missing-test-evidence-candidates.md"));
        var selectedIndex = await File.ReadAllTextAsync(Path.Combine(changedSnapshotDirectory, "production", "changed-files", "index.md"));
        Assert.Contains("no static test path", selected, StringComparison.Ordinal);
        Assert.Contains("indirect test path only", selected, StringComparison.Ordinal);
        Assert.Contains("Changed-files selection is snapshot-wide because changes to test roots or the call graph can alter associations in unchanged production files.", selected, StringComparison.Ordinal);
        Assert.Contains("Without a baseline, every current source file is treated as new.", selected, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/NoPath.cs (1 findings; source new or changed)", selected, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Indirect.cs (1 findings; source unchanged; included snapshot-wide)", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Analysis", selectedIndex, StringComparison.Ordinal);

        var withoutBaselineFindings = result.Findings.Select(static finding => finding with { ChangedSourcePaths = finding.SourcePaths }).ToArray();
        var withoutBaselineReport = await new MarkdownReportWriter().WriteAsync(config, result with
        {
            Findings = withoutBaselineFindings,
            HasCSharpSnapshotChanges = null,
        });
        var withoutBaselineDirectory = Path.Combine(config.ResolvedOutputDirectory, withoutBaselineReport.RunId);
        var withoutBaseline = await File.ReadAllTextAsync(Path.Combine(withoutBaselineDirectory, "production", "changed-files", "missing-test-evidence-candidates.md"));
        Assert.Contains("#### File: Sample/NoPath.cs (1 findings; source new or changed)", withoutBaseline, StringComparison.Ordinal);
        Assert.Contains("#### File: Sample/Indirect.cs (1 findings; source new or changed)", withoutBaseline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_UsesTheCentralAuditBaselineScriptWhenSupplied()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new ReportAnalysis("central-analysis", "Central", "active");
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var scriptPath = Path.Combine(temp.DirectoryPath, "scripts", "test-audit.ps1");
        var context = new BaselineCommandContext(scriptPath, "sample-target");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty),
        ]), baselineCommandContext: context);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));

        Assert.Contains($"& '{scriptPath}' -Target 'sample-target' -BaselineOnly", index, StringComparison.Ordinal);
        Assert.DoesNotContain($" baseline '{config.ProjectRoot}'", index, StringComparison.Ordinal);
        Assert.Contains("No findings were found.", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SeparatesChangedViewAndLinksRelatedFindingsAcrossAnalyses()
    {
        using var temp = TestTempDirectory.Create();
        var changedAnalysis = new ReportAnalysis("alpha-analysis", "Alpha", "active");
        var changedRelatedAnalysis = new ReportAnalysis("beta-analysis", "Beta", "active");
        var unchangedRelatedAnalysis = new ReportAnalysis("gamma-analysis", "Gamma", "active");
        var config = CreateConfig(temp.DirectoryPath, changedAnalysis, changedRelatedAnalysis, unchangedRelatedAnalysis);
        var symbol = new FindingSymbol("Sample/Sample.csproj", "Sample.cs", "M:Sample.Run", 3);
        var changedDraft = new FindingDraft("Sample/Sample.csproj", "Sample.cs", "M:Sample.Run", "complexity", 3, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Sample.cs", 3, "Source", "detail", "Run")], [symbol]);
        var unchangedDraft = new FindingDraft("Sample/Sample.csproj", "Other.cs", "M:Sample.Run", "dead-code", 8, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Other.cs", 8, "Source", "detail", "Run")], [symbol]);
        var outsideDraft = new FindingDraft("Sample/Sample.csproj", "Outside.cs", "M:Sample.Run", "outside", 11, "signal",
            new Dictionary<string, double>(), [new FindingEvidence("Outside.cs", 11, "Source", "detail", "Run")], [symbol]);
        var changedFinding = new ReviewFinding("alpha-analysis", changedDraft, ["Sample.cs"],
            [
                new ReviewFindingReference("beta-analysis", unchangedDraft.ProjectPath, unchangedDraft.SourcePath, unchangedDraft.SubjectId,
                    unchangedDraft.Discriminator, "Sample.cs", "M:Sample.Run", 3),
                new ReviewFindingReference("gamma-analysis", outsideDraft.ProjectPath, outsideDraft.SourcePath, outsideDraft.SubjectId,
                    outsideDraft.Discriminator, "Sample.cs", "M:Sample.Run", 3),
            ], ["Sample.cs"]);
        var changedRelatedFinding = new ReviewFinding("beta-analysis", unchangedDraft, ["Other.cs"], [], ["Sample.cs"]);
        var outsideFinding = new ReviewFinding("gamma-analysis", outsideDraft, ["Outside.cs"], [], []);
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult("alpha-analysis", new ReviewAnalysisResult([changedDraft])),
            new ReviewAnalysisRunResult("beta-analysis", new ReviewAnalysisResult([unchangedDraft])),
            new ReviewAnalysisRunResult("gamma-analysis", new ReviewAnalysisResult([outsideDraft])),
        ]) { Findings = [changedFinding, changedRelatedFinding, outsideFinding] };

        var report = await new MarkdownReportWriter().WriteAsync(config, result,
            configurationPath: Path.Combine(temp.DirectoryPath, "target project", "ainetreview.json"));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var changedIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "changed-files", "index.md"));
        var completeIndex = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "index.md"));
        var changedReportPath = Path.Combine(runDirectory, "production", "changed-files", "alpha-analysis.md");
        var changedReport = await File.ReadAllTextAsync(changedReportPath);
        var completeReportPath = Path.Combine(runDirectory, "production", "all-findings", "beta-analysis.md");

        Assert.Contains("Alpha (1)", changedIndex, StringComparison.Ordinal);
        Assert.Contains("Beta (1)", changedIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("Gamma", changedIndex, StringComparison.Ordinal);
        Assert.Contains("Alpha (1)", completeIndex, StringComparison.Ordinal);
        Assert.Contains("Beta (1)", completeIndex, StringComparison.Ordinal);
        Assert.Contains("Gamma (1)", completeIndex, StringComparison.Ordinal);
        Assert.True(File.Exists(completeReportPath));
        Assert.Contains("M:Sample.Run", changedReport, StringComparison.Ordinal);
        Assert.Contains("[beta-analysis (production/changed-files)]", changedReport, StringComparison.Ordinal);
        Assert.Contains("[gamma-analysis (production/all-findings)]", changedReport, StringComparison.Ordinal);
        var completeReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "all-findings", "alpha-analysis.md"));
        Assert.Contains("[beta-analysis (production/all-findings)]", completeReport, StringComparison.Ordinal);
        Assert.Contains("[gamma-analysis (production/all-findings)]", completeReport, StringComparison.Ordinal);
        Assert.DoesNotContain("all-findings reference", completeReport, StringComparison.Ordinal);
        Assert.Contains("`M:Sample.Run` (line 3)", changedReport, StringComparison.Ordinal);
        var completeBeforeEdit = await File.ReadAllTextAsync(completeReportPath);
        await File.WriteAllTextAsync(changedReportPath, changedReport.Replace("M:Sample.Run", "handled", StringComparison.Ordinal));
        Assert.Equal(completeBeforeEdit, await File.ReadAllTextAsync(completeReportPath));

        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        Assert.Contains($" baseline '{config.ProjectRoot}'", index, StringComparison.Ordinal);
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
        var prodReview = new ReviewFinding(sizeAnalysis.Descriptor.AnalysisId, productionFinding, [production.SourcePath, test.SourcePath], [], [production.SourcePath])
        {
            Occurrences = [new ReviewFindingOccurrence(production, ProjectRole.Production)],
            SubjectOccurrences = [new ReviewFindingOccurrence(production, ProjectRole.Production)],
        };
        var testReview = new ReviewFinding(flowAnalysis.Descriptor.AnalysisId, testFinding, [test.SourcePath], [], [test.SourcePath])
        {
            Occurrences = [new ReviewFindingOccurrence(test, ProjectRole.Tests)],
            SubjectOccurrences = [new ReviewFindingOccurrence(test, ProjectRole.Tests)],
        };
        var mixedReview = new ReviewFinding(duplicateAnalysis.Descriptor.AnalysisId, mixedFinding, [production.SourcePath], [
            new ReviewFindingReference(flowAnalysis.Descriptor.AnalysisId, test.ProjectPath, test.SourcePath, test.SymbolId, "test-case", test.SourcePath, test.SymbolId, test.Line),
        ], [production.SourcePath])
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
            Findings = [prodReview, testReview, mixedReview],
        };

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        foreach (var area in new[] { "production", "tests", "mixed" })
        foreach (var view in new[] { "changed-files", "all-findings" })
        {
            Assert.True(File.Exists(Path.Combine(runDirectory, area, view, "index.md")));
        }

        Assert.True(File.Exists(Path.Combine(runDirectory, "production", "changed-files", "code-size-candidates.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "tests", "changed-files", "flow-analysis.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "mixed", "changed-files", "duplicate-analysis.md")));
        Assert.False(File.Exists(Path.Combine(runDirectory, "production", "changed-files", "duplicate-analysis.md")));
        Assert.Contains("| production | 1 | 1 |", index, StringComparison.Ordinal);
        Assert.Contains("| tests | 1 | 1 |", index, StringComparison.Ordinal);
        Assert.Contains("| mixed | 1 | 1 |", index, StringComparison.Ordinal);
        Assert.Contains("2 unique representative project/file pairs", index, StringComparison.Ordinal);
        Assert.Contains("ProjectNameSuffix", index, StringComparison.Ordinal);
        Assert.Contains("ProjectFileNameSuffix", index, StringComparison.Ordinal);
        Assert.Contains("Test option provenance", index, StringComparison.Ordinal);
        Assert.Contains("percentile inherited", index, StringComparison.Ordinal);
        Assert.Contains("tests/Unknown.csproj", index, StringComparison.Ordinal);
        Assert.Contains("Unresolved provider type", index, StringComparison.Ordinal);
        var productionReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "production", "changed-files", "code-size-candidates.md"));
        Assert.Contains("Finding origin: production", productionReport, StringComparison.Ordinal);
        var mixedReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "mixed", "changed-files", "duplicate-analysis.md"));
        Assert.Contains("Finding origin: production + tests", mixedReport, StringComparison.Ordinal);
        Assert.Contains("M:Product.Run` (line 4; production)", mixedReport, StringComparison.Ordinal);
        Assert.Contains("M:ProductTests.Run` (line 8; tests)", mixedReport, StringComparison.Ordinal);
        Assert.Contains("[flow-analysis (tests/changed-files)]", mixedReport, StringComparison.Ordinal);
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
