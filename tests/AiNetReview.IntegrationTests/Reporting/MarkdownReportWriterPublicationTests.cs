namespace AiNetReview.IntegrationTests.Reporting;

using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.ReviewAnalyses;

public sealed class MarkdownReportWriterPublicationTests
{
    [Fact]
    public async Task WriteAsync_PublishesUniqueConcurrentRunsAndPreservesEarlierRuns()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var writer = new MarkdownReportWriter();

        var first = await writer.WriteAsync(config, result);
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, first.RunId, "index.md"));
        var concurrent = await Task.WhenAll(writer.WriteAsync(config, result), writer.WriteAsync(config, result));

        Assert.Equal(3, new[] { first }.Concat(concurrent).Select(static report => report.RunId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, first.RunId, "index.md")));
        Assert.All(concurrent, report => Assert.True(File.Exists(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"))));
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_RemovesTemporaryReportWhenWritingFailsBeforePublication()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var writer = new MarkdownReportWriter(_ => ValueTask.FromException(new IOException("injected pre-publication failure")));

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(config, result));

        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory));
        Assert.Empty(Directory.EnumerateFiles(config.ResolvedOutputDirectory));
    }

    [Fact]
    public async Task WriteAsync_CentralAuditReportsLinkBackToSourcesOutsideTheirOutputDirectory()
    {
        using var temp = TestTempDirectory.Create();
        var repositoryRoot = temp.GetPath("target-repository");
        Directory.CreateDirectory(repositoryRoot);
        var sourcePath = Path.Combine(repositoryRoot, "Sample Code.cs");
        await File.WriteAllTextAsync(sourcePath, "public sealed class Sample { }");
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "Sample.slnx"), "<Solution />");
        var analysis = new PublicationAnalysis();
        var standardConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"publication-analysis\":{}}}";
        var outputDirectory = temp.GetPath("central/audit-reporting/sample-repository");
        var config = new ReviewConfigValidator(new ReviewAnalysisRegistry([analysis]))
            .ValidateForAudit(repositoryRoot, standardConfig, outputDirectory);
        var finding = new AiNetReview.Core.Findings.FindingDraft(
            "Sample.csproj",
            "Sample Code.cs",
            "T:Sample",
            "type",
            1,
            "A test finding.",
            new Dictionary<string, double> { ["count"] = 1 },
            [new AiNetReview.Core.Findings.FindingEvidence("Sample Code.cs", 1, "Source", "Source evidence.", "Sample")]);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding]))]);

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var analysisReportPath = Path.Combine(outputDirectory, report.RunId, "analyses", "publication-analysis.md");
        var analysisReport = await File.ReadAllTextAsync(analysisReportPath);
        var links = Regex.Matches(analysisReport, @"\]\((?<target>[^)]+)#L[0-9]+\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Cast<Match>()
            .ToArray();
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            var target = Uri.UnescapeDataString(link.Groups["target"].Value);
            var linkedSource = Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile
                ? uri.LocalPath
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(analysisReportPath)!, target.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(Path.GetFullPath(sourcePath), Path.GetFullPath(linkedSource));
            Assert.True(File.Exists(linkedSource));
        }

        Assert.False(Directory.Exists(Path.Combine(repositoryRoot, "reports")));
    }

    private static ReviewConfig CreateConfig(string root, PublicationAnalysis analysis)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var json = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\""
            + analysis.Descriptor.AnalysisId + "\":{}}}";
        return new ReviewConfigValidator(new ReviewAnalysisRegistry([analysis])).Validate(root, json);
    }

    private sealed class PublicationAnalysis : IReviewAnalysis
    {
        internal PublicationAnalysis() => Descriptor = new ReviewAnalysisDescriptor(
            "publication-analysis", "Publication Review analysis", 1, "A test purpose.", "A test measurement.", ["Is publication complete?"]);

        public ReviewAnalysisDescriptor Descriptor { get; }

        public Task<AiNetReview.Core.ReviewAnalyses.ReviewAnalysisResult> ExecuteAsync(
            AiNetReview.Core.Analysis.ReviewContext context,
            ReviewAnalysisOptions options,
            CancellationToken cancellationToken) => Task.FromResult(ReviewAnalysisResult.Empty);
    }
}
