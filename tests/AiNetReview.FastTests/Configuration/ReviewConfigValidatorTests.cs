namespace AiNetReview.FastTests.Configuration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.MethodControlFlowOutliers;
using AiNetReview.Core.Rules.DeadCodeCandidates;

public sealed class ReviewConfigValidatorTests
{
    private static RuleRegistry Registry() => new([new MethodControlFlowOutliersRule(), new DeadCodeCandidatesRule()]);

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

    [Fact]
    public void ValidateForAudit_RequiresAbsoluteOutputDirectory()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var outputDirectory = temp.GetPath("central/reports");
        var relativeOutputDirectory = Path.GetRelativePath(Environment.CurrentDirectory, outputDirectory);

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).ValidateForAudit(
            temp.DirectoryPath,
            ValidJson(),
            relativeOutputDirectory));
        Assert.False(Directory.Exists(Path.GetDirectoryName(outputDirectory)));
    }

    [Theory]
    [InlineData("missing.slnx", "method-control-flow-outliers")]
    [InlineData("Project.slnx", "unknown-rule")]
    public void ValidateForAudit_RejectsInvalidSolutionOrRuleWithoutCreatingCentralOutput(string solution, string ruleId)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var outputDirectory = temp.GetPath("central/audit-target");
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution,
            outputDirectory = "reports",
            rules = new Dictionary<string, object> { [ruleId] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry())
            .ValidateForAudit(temp.DirectoryPath, json, outputDirectory));

        Assert.False(Directory.Exists(outputDirectory));
        Assert.False(Directory.Exists(Path.Combine(temp.DirectoryPath, "reports")));
    }

    [Fact]
    public void ValidateForAudit_UsesCentralOutputWithoutCreatingReportsInTargetRepository()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var outputDirectory = temp.GetPath("central/audit-target");

        var config = new ReviewConfigValidator(Registry()).ValidateForAudit(
            temp.DirectoryPath,
            ValidJson(),
            outputDirectory);

        Assert.Equal(Path.GetFullPath(outputDirectory), config.ResolvedOutputDirectory);
        Assert.True(Directory.Exists(outputDirectory));
        Assert.False(Directory.Exists(Path.Combine(temp.DirectoryPath, "reports")));
    }

    [Fact]
    public void Validate_ResolvesDeadCodeOptionsAndRejectsMalformedValues()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());
        var defaultConfig = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{}}}");
        var defaults = defaultConfig.Rules.Single().EffectiveOptions;

        Assert.Equal("external_library", defaults["apiSurface"].GetString());
        Assert.Empty(defaults["entryPointAttributes"].EnumerateArray());

        var configured = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{\"apiSurface\":\"closed_solution\",\"entryPointAttributes\":[\"Example.EntryPointAttribute\"]}}}");
        Assert.Equal("closed_solution", configured.Rules.Single().EffectiveOptions["apiSurface"].GetString());
        Assert.Equal("Example.EntryPointAttribute", configured.Rules.Single().EffectiveOptions["entryPointAttributes"].EnumerateArray().Single().GetString());

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{\"apiSurface\":\"unknown\"}}}"));
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{\"entryPointAttributes\":[\"Unqualified\"]}}}"));
    }

    [Fact]
    public void Validate_DisabledRuleIsValidatedButExcludedFromExecution()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());

        var config = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{\"enabled\":false,\"apiSurface\":\"closed_solution\"},\"method-control-flow-outliers\":{}}}");

        var activeRule = Assert.Single(config.Rules);
        Assert.Equal("method-control-flow-outliers", activeRule.RuleId);

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"dead-code-candidates\":{\"enabled\":false,\"unknown\":true},\"method-control-flow-outliers\":{}}}"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("\"false\"")]
    public void Validate_RejectsNonBooleanEnabled(string enabled)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath,
            $"{{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{{\"method-control-flow-outliers\":{{\"enabled\":{enabled}}}}}}}"));
    }

    [Fact]
    public void Validate_AllowsEveryConfiguredRuleToBeDisabled()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        var config = new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"rules\":{\"method-control-flow-outliers\":{\"enabled\":false},\"dead-code-candidates\":{\"enabled\":false}}}");
        Assert.Empty(config.Rules);
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
