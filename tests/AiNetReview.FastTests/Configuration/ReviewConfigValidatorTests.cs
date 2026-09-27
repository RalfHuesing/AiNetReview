namespace AiNetReview.FastTests.Configuration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;

public sealed class ReviewConfigValidatorTests
{
    private static RuleRegistry Registry() => new([new TemplateNoOpRule()]);

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
        Assert.Equal(".review-store", config.StorageDirectory);
        Assert.True(Directory.Exists(config.ResolvedOutputDirectory));
        Assert.True(Directory.Exists(config.ResolvedStorageDirectory));
        Assert.Single(config.Rules);
        Assert.Equal("template-noop", config.Rules[0].RuleId);
        Assert.Empty(config.Rules[0].EffectiveOptions.Values);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"storageDirectory\":\"store\",\"rules\":{\"template-noop\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"storageDirectory\":\"store\",\"extra\":true,\"rules\":{\"template-noop\":{}}}")]
    [InlineData("{\"schemaVersion\":2,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"storageDirectory\":\"store\",\"rules\":{\"template-noop\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"storageDirectory\":\"store\",\"rules\":{\"unknown\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"storageDirectory\":\"store\",\"rules\":{\"template-noop\":{\"unknown\":true}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"storageDirectory\":\"store\",\"rules\":[]}")]
    public void Validate_RejectsInvalidJsonContract(string json)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    [Theory]
    [InlineData("../outside.slnx", "out", "store")]
    [InlineData("Project.slnx", "../outside", "store")]
    [InlineData("Project.slnx", "out", "out/nested")]
    [InlineData("Project.slnx", "C:/outside", "store")]
    [InlineData("missing.slnx", "out", "store")]
    public void Validate_RejectsInvalidPaths(string solution, string output, string storage)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution,
            outputDirectory = output,
            storageDirectory = storage,
            rules = new Dictionary<string, object> { ["template-noop"] = new { } },
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
            storageDirectory = "store",
            rules = new Dictionary<string, object> { ["template-noop"] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    private static string ValidJson() => """
        {
          "schemaVersion": 1,
          "solution": "Project.slnx",
          "outputDirectory": "reports/./current",
          "storageDirectory": ".review-store",
          "rules": { "template-noop": {} }
        }
        """;
}
