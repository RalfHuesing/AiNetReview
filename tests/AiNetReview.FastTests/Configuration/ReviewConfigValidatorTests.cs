namespace AiNetReview.FastTests.Configuration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.MethodControlFlowOutliers;

public sealed class ReviewConfigValidatorTests
{
    private static RuleRegistry Registry() => new([new MethodControlFlowOutliersRule()]);

    [Fact]
    public void Load_RequiresAbsoluteAinetreviewFileAtProjectRoot()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var configPath = temp.CreateFile("ainetreview.json", ValidJson());

        var config = new ReviewConfigValidator(Registry()).Load(configPath);

        Assert.Equal("Project.slnx", config.SolutionPath);
        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Load(temp.CreateFile("other.json", ValidJson())));
        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Load("ainetreview.json"));
    }

    [Fact]
    public void Validate_AppliesRuleDefaultsAndCreatesNormalizedDirectories()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var config = new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, ValidJson());

        Assert.Equal(Path.GetFullPath(temp.DirectoryPath), config.ProjectRoot);
        Assert.EndsWith("Project.slnx", config.SolutionPath, StringComparison.Ordinal);
        Assert.Equal("Project.slnx", config.SolutionPath);
        Assert.Equal("reports/current", config.OutputDirectory);
        Assert.True(Directory.Exists(config.ResolvedOutputDirectory));
        Assert.Single(config.Rules);
        Assert.Equal("method-control-flow-outliers", config.Rules[0].RuleId);
        Assert.Equal(90, config.Rules[0].EffectiveOptions["percentile"].GetInt32());
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"method-control-flow-outliers\":{\"option\":1,\"option\":2}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"method-control-flow-outliers\":{},\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"extra\":true,\"rules\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":2,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"unknown\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"method-control-flow-outliers\":{\"unknown\":true}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":[]} ")]
    public void Validate_RejectsInvalidJsonContract(string json)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    [Theory]
    [InlineData("../outside.slnx", "out")]
    [InlineData("Project.slnx", "../outside")]
    [InlineData("Project.slnx", "C:/outside")]
    [InlineData("missing.slnx", "out")]
    public void Validate_RejectsInvalidPaths(string solution, string output)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution,
            outputDirectory = output,
            rules = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    [Theory]
    [InlineData("solution")]
    [InlineData("outputDirectory")]
    public void Validate_RejectsPathsContainingNullCharacters(string fieldName)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = fieldName == "solution" ? "Project\0.slnx" : "Project.slnx",
            outputDirectory = fieldName == "outputDirectory" ? "reports\0invalid" : "reports",
            rules = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    [Theory]
    [InlineData("solution")]
    [InlineData("outputDirectory")]
    public void Validate_RejectsBackslashesInProjectRelativePaths(string fieldName)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("nested/Project.slnx", "<Solution />");

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = fieldName == "solution" ? "nested\\Project.slnx" : "nested/Project.slnx",
            outputDirectory = fieldName == "outputDirectory" ? "nested\\reports" : "reports",
            rules = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    [Fact]
    public void Validate_RejectsPathThatEscapesThroughDirectorySymlink()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        using var outside = TestTempDirectory.Create();
        try
        {
            Directory.CreateSymbolicLink(temp.GetPath("escape"), outside.DirectoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution = "Project.slnx",
            outputDirectory = "escape/reports",
            rules = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    private static string ValidJson() => """
        {
          "schemaVersion": 1,
          "solution": "Project.slnx",
          "outputDirectory": "reports/./current",
          "rules": { "method-control-flow-outliers": {} }
        }
        """;
}
