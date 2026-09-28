namespace AiNetReview.FastTests.Reporting;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.Rules;

public sealed class MarkdownReportWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesEmptyRuleReportWithSortedOptionsAndUtf8Lf()
    {
        using var temp = TestTempDirectory.Create();
        var rule = new ReportRule("alpha-rule", "Alpha | Rule", "value|with `markdown`");
        var secondRule = new ReportRule("zeta-rule", "Zeta Rule", "last");
        var config = CreateConfig(temp.DirectoryPath, rule, secondRule);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new RuleRunResult(secondRule.Descriptor.RuleId, RuleResult.Empty),
            new RuleRunResult(rule.Descriptor.RuleId, RuleResult.Empty),
        ]));

        var indexBytes = await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(config.ResolvedOutputDirectory, report.RunId), "*", SearchOption.AllDirectories).Length);
        Assert.False(indexBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain((byte)'\r', indexBytes);
        var index = Encoding.UTF8.GetString(indexBytes);
        Assert.Contains($"# AiNetReview – {report.RunId}", index, StringComparison.Ordinal);
        Assert.Contains("| Detected | 0 |", index, StringComparison.Ordinal);
        Assert.Contains("Alpha \\| Rule", index, StringComparison.Ordinal);
        Assert.Contains("value", index, StringComparison.Ordinal);
        Assert.Contains("markdown", index, StringComparison.Ordinal);
        Assert.True(index.IndexOf("Alpha \\| Rule", StringComparison.Ordinal) < index.IndexOf("Zeta Rule", StringComparison.Ordinal));
        Assert.Contains("[Markdown report](rules/alpha-rule.md)", index, StringComparison.Ordinal);
        Assert.True(index.IndexOf("\"alpha\"", StringComparison.Ordinal) < index.IndexOf("\"scenario\"", StringComparison.Ordinal));
        var ruleReport = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "rules", "alpha-rule.md"));
        Assert.Contains("| Detected | 0 |", ruleReport, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "rules", "zeta-rule.md")));
        Assert.Equal("reports/" + report.RunId + "/index.md", report.IndexPath);
    }

    [Fact]
    public async Task WriteAsync_SortsFindingsAndEscapesContentAndSourceLinks()
    {
        using var temp = TestTempDirectory.Create();
        var rule = new ReportRule("fixture-rule", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, rule);
        var z = Finding("z file#1.cs", 9, "Z", "last|rationale", "second`snippet", "zeta");
        var a = Finding("a file#1.cs", 3, "A", "first | rationale", "first `snippet`", "alpha");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new RuleRunResult(rule.Descriptor.RuleId, new RuleResult([z, a])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "rules", "fixture-rule.md"));

        Assert.True(markdown.IndexOf("### Evidence", StringComparison.Ordinal) >= 0);
        Assert.True(markdown.IndexOf("A — alpha", StringComparison.Ordinal) < markdown.IndexOf("Z — zeta", StringComparison.Ordinal));
        Assert.Contains("../../../a%20file%231.cs#L3", markdown, StringComparison.Ordinal);
        Assert.Contains("\\| rationale", markdown, StringComparison.Ordinal);
        Assert.Contains("first \\`snippet\\`", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("aMetric=1", StringComparison.Ordinal) < markdown.IndexOf("zMetric=2", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("A \\| label", StringComparison.Ordinal) < markdown.IndexOf("B \\| label", StringComparison.Ordinal));
        Assert.Contains("| Detected | 2 |", markdown, StringComparison.Ordinal);
    }

    private static ReviewConfig CreateConfig(string root, params ReportRule[] rules)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var registry = new RuleRegistry(rules);
        var entries = string.Join(',', rules.Select(static rule => "\"" + rule.Descriptor.RuleId + "\":{}"));
        var config = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"rules\":{" + entries + "}}";
        return new ReviewConfigValidator(registry).Validate(root, config);
    }

    private static FindingDraft Finding(string path, int line, string subject, string rationale, string snippet, string discriminator) => new(
        "Sample/Sample.csproj", path, subject, discriminator, line, rationale,
        new Dictionary<string, double> { ["zMetric"] = 2, ["aMetric"] = 1 },
        [
            new FindingEvidence(path, line, "B | label", "detail `text`", snippet),
            new FindingEvidence(path, line, "A | label", "earlier evidence", snippet),
        ]);

    private sealed class ReportRule : IReviewRule
    {
        internal ReportRule(string id, string title, string optionValue)
        {
            Descriptor = new RuleDescriptor(id, title, 1, "A test purpose.", "A test measurement.", ["Review this result?"],
                [
                    RuleOptionDescriptor.String("scenario", "Scenario", optionValue),
                    RuleOptionDescriptor.String("zeta", "Last option", "z"),
                    RuleOptionDescriptor.String("alpha", "First option", "a"),
                ]);
        }

        public RuleDescriptor Descriptor { get; }

        public Task<RuleResult> ExecuteAsync(AiNetReview.Core.Analysis.ReviewContext context, RuleOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(RuleResult.Empty);
    }
}
