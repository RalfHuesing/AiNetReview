namespace AiNetReview.FastTests.Configuration;

using System.Text.Json;
using System.Linq;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.DeadCodeCandidates;
using AiNetReview.Core.Rules.DuplicateCodeCandidates;
using AiNetReview.Core.Rules.MethodControlFlowOutliers;

public sealed class DefaultReviewConfigGeneratorTests
{
    [Fact]
    public void Constructor_RejectsRegistryWithoutRules()
    {
        Assert.Throws<ArgumentException>(() => new DefaultReviewConfigGenerator(new RuleRegistry([])));
    }

    [Fact]
    public void Generate_EmitsSchemaVersionOneWithEveryRuleDefaultAndValidatorAcceptsIt()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var registry = new RuleRegistry(
        [
            new DuplicateCodeCandidatesRule(),
            new DeadCodeCandidatesRule(),
            new MethodControlFlowOutliersRule(),
        ]);
        var generator = new DefaultReviewConfigGenerator(registry);

        var json = generator.Generate("Project.slnx");

        Assert.False(System.IO.Directory.Exists(temp.GetPath("audit-reporting")));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Project.slnx", root.GetProperty("solution").GetString());
        Assert.Equal("audit-reporting", root.GetProperty("outputDirectory").GetString());
        var generatedRules = root.GetProperty("rules");
        Assert.Equal(registry.Rules.Count, generatedRules.EnumerateObject().Count());

        foreach (var rule in registry.Rules)
        {
            var generatedRule = generatedRules.GetProperty(rule.Descriptor.RuleId);
            Assert.Equal(rule.Descriptor.DefaultEnabled, generatedRule.GetProperty("enabled").GetBoolean());
            foreach (var option in rule.Descriptor.Options)
            {
                Assert.True(generatedRule.TryGetProperty(option.Name, out var generatedOption));
                Assert.Equal(option.DefaultValue.GetRawText(), generatedOption.GetRawText());
            }
        }

        var config = new ReviewConfigValidator(registry).Validate(temp.DirectoryPath, json);
        Assert.Equal("Project.slnx", config.SolutionPath);
        Assert.Equal("audit-reporting", config.OutputDirectory);
        Assert.Equal(registry.Rules.Count, config.Rules.Count);
    }

    [Fact]
    public void Generate_UsesIndentedJsonAndRejectsRootedOrWindowsSeparatedSolutionPath()
    {
        var generator = new DefaultReviewConfigGenerator(new RuleRegistry([new MethodControlFlowOutliersRule()]));

        var json = generator.Generate("Project.slnx");

        Assert.Contains(Environment.NewLine + "  \"schemaVersion\": 1,", json, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => generator.Generate("../Project.slnx"));
        Assert.Throws<ArgumentException>(() => generator.Generate("nested\\Project.slnx"));
        Assert.Throws<ArgumentException>(() => generator.Generate("Project.csproj"));
    }
}
