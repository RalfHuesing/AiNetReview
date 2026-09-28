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
    public async Task WriteAsync_ReportsEmptyActiveRulesWithoutCreatingRuleFiles()
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
        Assert.Single(Directory.GetFiles(Path.Combine(config.ResolvedOutputDirectory, report.RunId), "*", SearchOption.AllDirectories));
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
        Assert.Empty(Directory.GetDirectories(Path.Combine(config.ResolvedOutputDirectory, report.RunId)));
        Assert.Equal("reports/" + report.RunId + "/index.md", report.IndexPath);
    }

    [Fact]
    public async Task WriteAsync_DistinguishesWhenNoRulesAreActive()
    {
        using var temp = TestTempDirectory.Create();
        var rule = new ReportRule("inactive-rule", "Inactive Rule", "unused");
        var config = CreateConfig(temp.DirectoryPath, false, rule);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));

        Assert.Contains("No review was performed because all rules are disabled.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("No findings were found", index, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(runDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WriteAsync_LinksOnlyRulesWithFindingsAndWritesOneTableRowPerFinding()
    {
        using var temp = TestTempDirectory.Create();
        var withFindings = new ReportRule("has-findings", "Has Findings", "active");
        var withoutFindings = new ReportRule("empty-rule", "Empty Rule", "active");
        var config = CreateConfig(temp.DirectoryPath, withFindings, withoutFindings);
        var finding = Finding("Sample.cs", 2, "C:Sample", "A concise signal", "class Sample", "type-candidate");
        var samePathInAnotherProject = new FindingDraft(
            "Other/Sample.csproj", "Sample.cs", "C:Other", "type-candidate", 3, "A concise signal",
            new Dictionary<string, double>(),
            [new FindingEvidence("Sample.cs", 3, "Type declaration", "A candidate declaration.", "class Other")]);

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new RuleRunResult(withFindings.Descriptor.RuleId, new RuleResult([finding, samePathInAnotherProject])),
            new RuleRunResult(withoutFindings.Descriptor.RuleId, RuleResult.Empty),
        ]));
        var runDirectory = Path.Combine(config.ResolvedOutputDirectory, report.RunId);
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var ruleReport = await File.ReadAllTextAsync(Path.Combine(runDirectory, "rules", "has-findings.md"));

        Assert.Contains("[Has Findings](rules/has-findings.md)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Empty Rule", index, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(runDirectory, "rules")));
        Assert.Equal(2, ruleReport.Split("| [", StringSplitOptions.None).Length - 1);
        Assert.Contains("(Sample/Sample.csproj)", ruleReport, StringComparison.Ordinal);
        Assert.Contains("(Other/Sample.csproj)", ruleReport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SortsFindingsAndEscapesContentAndSourceLinks()
    {
        using var temp = TestTempDirectory.Create();
        var rule = new ReportRule("fixture-rule", "Fixture", "safe");
        var config = CreateConfig(temp.DirectoryPath, rule);
        var z = Finding("z file#1.cs", 9, "Z", "last|rationale", "second`snippet", "zeta");
        var a = Finding("a file#1.cs", 3, "A", "first | rationale", "`snippet`", "alpha");

        var report = await new MarkdownReportWriter().WriteAsync(config, new ReviewRunResult([
            new RuleRunResult(rule.Descriptor.RuleId, new RuleResult([z, a])),
        ]));
        var markdown = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "rules", "fixture-rule.md"));

        Assert.Contains("| Source | Signal | Other Locations |", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("[a file\\#1.cs:3]", StringComparison.Ordinal) < markdown.IndexOf("[z file\\#1.cs:9]", StringComparison.Ordinal));
        Assert.Equal(2, markdown.Split("| [", StringSplitOptions.None).Length - 1);
        Assert.Contains("../../../a%20file%231.cs#L3", markdown, StringComparison.Ordinal);
        Assert.Contains("\\| rationale", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample/Sample.csproj", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("aMetric", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Metrics", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("detail", markdown, StringComparison.Ordinal);
        Assert.Contains("Effective options:", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("\"alpha\"", StringComparison.Ordinal) < markdown.IndexOf("\"scenario\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\\{", markdown, StringComparison.Ordinal);
        var index = await File.ReadAllTextAsync(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"));
        Assert.Contains("Working through findings", index, StringComparison.Ordinal);
        Assert.Contains("explain the decision to the user", index, StringComparison.Ordinal);
        Assert.Contains("All findings addressed", index, StringComparison.Ordinal);
    }

    private static ReviewConfig CreateConfig(string root, params ReportRule[] rules) => CreateConfig(root, true, rules);

    private static ReviewConfig CreateConfig(string root, bool enabled, params ReportRule[] rules)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var registry = new RuleRegistry(rules);
        var entries = string.Join(',', rules.Select(rule => "\"" + rule.Descriptor.RuleId + "\":{" + (enabled ? string.Empty : "\"enabled\":false") + "}"));
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
