namespace AiNetReview.FastTests.Configuration;

using System.Text.Json;
using System.Linq;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;

public sealed class DefaultReviewConfigGeneratorTests
{
    [Fact]
    public void Constructor_RejectsRegistryWithoutAnalyses()
    {
        Assert.Throws<ArgumentException>(() => new DefaultReviewConfigGenerator(new ReviewAnalysisRegistry([])));
    }

    [Fact]
    public void Generate_EmitsSchemaVersionOneWithEveryAnalysisDefaultAndValidatorAcceptsIt()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var registry = new ReviewAnalysisRegistry(
        [
            new DuplicateCodeCandidatesAnalysis(),
            new DeadCodeCandidatesAnalysis(),
            new MethodControlFlowOutliersAnalysis(),
            new NonAsciiIdentifiersAnalysis(),
            new IndirectionDriftCandidatesAnalysis(),
            new MissingTestEvidenceCandidatesAnalysis(),
        ]);
        var generator = new DefaultReviewConfigGenerator(registry);

        var json = generator.Generate("Project.slnx");

        Assert.False(System.IO.Directory.Exists(temp.GetPath("audit-reporting")));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Project.slnx", root.GetProperty("solution").GetString());
        Assert.Equal("audit-reporting", root.GetProperty("outputDirectory").GetString());
        var generatedAnalyses = root.GetProperty("analyses");
        Assert.Equal(registry.Analyses.Count, generatedAnalyses.EnumerateObject().Count());

        foreach (var analysis in registry.Analyses)
        {
            var generatedAnalysis = generatedAnalyses.GetProperty(analysis.Descriptor.AnalysisId);
            Assert.Equal(analysis.Descriptor.DefaultEnabled, generatedAnalysis.GetProperty("enabled").GetBoolean());
            foreach (var option in analysis.Descriptor.Options)
            {
                Assert.True(generatedAnalysis.TryGetProperty(option.Name, out var generatedOption));
                Assert.Equal(option.DefaultValue.GetRawText(), generatedOption.GetRawText());
            }
        }

        var config = new ReviewConfigValidator(registry).Validate(temp.DirectoryPath, json);
        Assert.Equal("Project.slnx", config.SolutionPath);
        Assert.Equal("audit-reporting", config.OutputDirectory);
        Assert.Equal(registry.Analyses.Count, config.Analyses.Count);

        var missingTestEvidence = generatedAnalyses.GetProperty("missing-test-evidence-candidates");
        Assert.True(missingTestEvidence.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, missingTestEvidence.GetProperty("minDecisionCount").GetInt32());
        Assert.Equal(2, missingTestEvidence.GetProperty("minDecisionNesting").GetInt32());
        Assert.Equal(5, missingTestEvidence.GetProperty("minIndirectDecisionCount").GetInt32());
        Assert.Equal(3, missingTestEvidence.GetProperty("minIndirectDecisionNesting").GetInt32());
    }

    [Fact]
    public void Generate_UsesIndentedJsonAndRejectsRootedOrWindowsSeparatedSolutionPath()
    {
        var generator = new DefaultReviewConfigGenerator(new ReviewAnalysisRegistry([new MethodControlFlowOutliersAnalysis()]));

        var json = generator.Generate("Project.slnx");

        Assert.Contains(Environment.NewLine + "  \"schemaVersion\": 1,", json, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => generator.Generate("../Project.slnx"));
        Assert.Throws<ArgumentException>(() => generator.Generate("nested\\Project.slnx"));
        Assert.Throws<ArgumentException>(() => generator.Generate("Project.csproj"));
    }
}
