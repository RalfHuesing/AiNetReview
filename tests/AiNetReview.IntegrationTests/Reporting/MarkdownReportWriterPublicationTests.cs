namespace AiNetReview.IntegrationTests.Reporting;

using System.IO;
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
