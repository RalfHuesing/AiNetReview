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
using AiNetReview.Core.Rules;

public sealed class MarkdownReportWriterPublicationTests
{
    [Fact]
    public async Task WriteAsync_PublishesUniqueConcurrentRunsAndPreservesEarlierRuns()
    {
        using var temp = TestTempDirectory.Create();
        var rule = new PublicationRule();
        var config = CreateConfig(temp.DirectoryPath, rule);
        var result = new ReviewRunResult([new RuleRunResult(rule.Descriptor.RuleId, RuleResult.Empty)]);
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
        var rule = new PublicationRule();
        var config = CreateConfig(temp.DirectoryPath, rule);
        var result = new ReviewRunResult([new RuleRunResult(rule.Descriptor.RuleId, RuleResult.Empty)]);
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
        var rule = new PublicationRule();
        var standardConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"publication-rule\":{}}}";
        var outputDirectory = temp.GetPath("central/audit-reporting/sample-repository");
        var config = new ReviewConfigValidator(new RuleRegistry([rule]))
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
        var result = new ReviewRunResult([new RuleRunResult(rule.Descriptor.RuleId, new RuleResult([finding]))]);

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var ruleReportPath = Path.Combine(outputDirectory, report.RunId, "rules", "publication-rule.md");
        var ruleReport = await File.ReadAllTextAsync(ruleReportPath);
        var links = Regex.Matches(ruleReport, @"\]\((?<target>[^)]+)#L[0-9]+\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Cast<Match>()
            .ToArray();
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            var target = Uri.UnescapeDataString(link.Groups["target"].Value);
            var linkedSource = Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile
                ? uri.LocalPath
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ruleReportPath)!, target.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(Path.GetFullPath(sourcePath), Path.GetFullPath(linkedSource));
            Assert.True(File.Exists(linkedSource));
        }

        Assert.False(Directory.Exists(Path.Combine(repositoryRoot, "reports")));
    }

    private static ReviewConfig CreateConfig(string root, PublicationRule rule)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var json = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\""
            + rule.Descriptor.RuleId + "\":{}}}";
        return new ReviewConfigValidator(new RuleRegistry([rule])).Validate(root, json);
    }

    private sealed class PublicationRule : IReviewRule
    {
        internal PublicationRule() => Descriptor = new RuleDescriptor(
            "publication-rule", "Publication Rule", 1, "A test purpose.", "A test measurement.", ["Is publication complete?"]);

        public RuleDescriptor Descriptor { get; }

        public Task<AiNetReview.Core.Rules.RuleResult> ExecuteAsync(
            AiNetReview.Core.Analysis.ReviewContext context,
            RuleOptions options,
            CancellationToken cancellationToken) => Task.FromResult(RuleResult.Empty);
    }
}
