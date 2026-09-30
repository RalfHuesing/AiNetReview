namespace AiNetReview.FastTests.Configuration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;
using AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;

public sealed class ReviewConfigValidatorTests
{
    private static ReviewAnalysisRegistry Registry() => new([
        new MethodControlFlowOutliersAnalysis(),
        new DeadCodeCandidatesAnalysis(),
        new DuplicateCodeCandidatesAnalysis(),
        new MissingTestEvidenceCandidatesAnalysis(),
        new NonAsciiIdentifiersAnalysis(),
        new StructuralDuplicationCandidatesAnalysis()]);

    [Fact]
    public void Validate_AppliesAndValidatesNonAsciiIdentifiersOptions()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());

        var config = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"non-ascii-identifiers\":{}}}");

        var analysis = Assert.Single(config.Analyses);
        Assert.Equal("non-ascii-identifiers", analysis.AnalysisId);
        Assert.Empty(analysis.EffectiveOptions.Values);

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"non-ascii-identifiers\":{\"unknownOption\":true}}}"));
    }

    [Fact]
    public void Validate_AppliesAndValidatesDuplicateCodeOptions()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());

        var config = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"duplicate-code-candidates\":{}}}");

        var analysis = Assert.Single(config.Analyses);
        Assert.Equal("duplicate-code-candidates", analysis.AnalysisId);
        Assert.Equal(30, analysis.EffectiveOptions["minTokens"].GetInt32());
        Assert.Equal("exact", analysis.EffectiveOptions["minimumSimilarity"].GetString());
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"duplicate-code-candidates\":{\"minimumSimilarity\":\"loose\"}}}"));
    }

    [Fact]
    public void Validate_AcceptsStructuralDuplicationEnabledOnly()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());

        var defaults = Assert.Single(validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"structural-duplication-candidates\":{}}}").Analyses);
        Assert.Empty(defaults.EffectiveOptions.Values);

        Assert.Empty(validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"structural-duplication-candidates\":{\"enabled\":false}}}").Analyses);

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"structural-duplication-candidates\":{\"minTokens\":60}}}"));
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"structural-duplication-candidates\":{\"enabled\":1}}}"));
    }

    [Fact]
    public void Validate_AppliesAndValidatesMissingTestEvidenceOptions()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());
        const string prefix = "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"missing-test-evidence-candidates\":{";

        var defaults = Assert.Single(validator.Validate(temp.DirectoryPath, prefix + "}}}").Analyses).EffectiveOptions;
        Assert.Equal(3, defaults["minDecisionCount"].GetInt32());
        Assert.Equal(2, defaults["minDecisionNesting"].GetInt32());
        Assert.Equal(5, defaults["minIndirectDecisionCount"].GetInt32());
        Assert.Equal(3, defaults["minIndirectDecisionNesting"].GetInt32());

        var configured = Assert.Single(validator.Validate(temp.DirectoryPath,
            prefix + "\"enabled\":true,\"minDecisionCount\":1,\"minDecisionNesting\":2,\"minIndirectDecisionCount\":2147483647,\"minIndirectDecisionNesting\":3}}}").Analyses);
        Assert.Equal(1, configured.EffectiveOptions["minDecisionCount"].GetInt32());
        Assert.Equal(int.MaxValue, configured.EffectiveOptions["minIndirectDecisionCount"].GetInt32());

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            prefix + "\"enabled\":false,\"minDecisionCount\":0}}}"));
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            prefix + "\"minDecisionNesting\":2147483648}}}"));
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            prefix + "\"minIndirectDecisionCount\":\"5\"}}}"));
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            prefix + "\"minIndirectDecisionNesting\":null}}}"));
    }

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
    public void Validate_AppliesAnalysisDefaultsAndCreatesNormalizedDirectories()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var config = new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, ValidJson());

        Assert.Equal(Path.GetFullPath(temp.DirectoryPath), config.ProjectRoot);
        Assert.EndsWith("Project.slnx", config.SolutionPath, StringComparison.Ordinal);
        Assert.Equal("Project.slnx", config.SolutionPath);
        Assert.Equal("reports/current", config.OutputDirectory);
        Assert.True(Directory.Exists(config.ResolvedOutputDirectory));
        Assert.Single(config.Analyses);
        Assert.Equal("method-control-flow-outliers", config.Analyses[0].AnalysisId);
        Assert.Equal(90, config.Analyses[0].EffectiveOptions["percentile"].GetInt32());
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
    [InlineData("Project.slnx", "unknown-analysis")]
    public void ValidateForAudit_RejectsInvalidSolutionOrAnalysisWithoutCreatingCentralOutput(string solution, string analysisId)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var outputDirectory = temp.GetPath("central/audit-target");
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution,
            outputDirectory = "reports",
            analyses = new Dictionary<string, object> { [analysisId] = new { } },
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
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{}}}");
        var defaults = defaultConfig.Analyses.Single().EffectiveOptions;

        Assert.Equal("external_library", defaults["apiSurface"].GetString());
        Assert.Empty(defaults["entryPointAttributes"].EnumerateArray());

        var configured = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{\"apiSurface\":\"closed_solution\",\"entryPointAttributes\":[\"Example.EntryPointAttribute\"]}}}");
        Assert.Equal("closed_solution", configured.Analyses.Single().EffectiveOptions["apiSurface"].GetString());
        Assert.Equal("Example.EntryPointAttribute", configured.Analyses.Single().EffectiveOptions["entryPointAttributes"].EnumerateArray().Single().GetString());

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{\"apiSurface\":\"unknown\"}}}"));
        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{\"entryPointAttributes\":[\"Unqualified\"]}}}"));
    }

    [Fact]
    public void Validate_DisabledAnalysisIsValidatedButExcludedFromExecution()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");
        var validator = new ReviewConfigValidator(Registry());

        var config = validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{\"enabled\":false,\"apiSurface\":\"closed_solution\"},\"method-control-flow-outliers\":{}}}");

        var activeAnalysis = Assert.Single(config.Analyses);
        Assert.Equal("method-control-flow-outliers", activeAnalysis.AnalysisId);

        Assert.Throws<InvalidReviewInputException>(() => validator.Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"dead-code-candidates\":{\"enabled\":false,\"unknown\":true},\"method-control-flow-outliers\":{}}}"));
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
            $"{{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{{\"method-control-flow-outliers\":{{\"enabled\":{enabled}}}}}}}"));
    }

    [Fact]
    public void Validate_AllowsEveryConfiguredReviewAnalysisToBeDisabled()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Project.slnx", "<Solution />");

        var config = new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath,
            "{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"method-control-flow-outliers\":{\"enabled\":false},\"dead-code-candidates\":{\"enabled\":false}}}");
        Assert.Empty(config.Analyses);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":{\"method-control-flow-outliers\":{\"option\":1,\"option\":2}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":{\"method-control-flow-outliers\":{},\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"extra\":true,\"analyses\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"rules\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":2,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":{\"method-control-flow-outliers\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":{\"unknown\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":{\"method-control-flow-outliers\":{\"unknown\":true}}}")]
    [InlineData("{\"schemaVersion\":1,\"solution\":\"Project.slnx\",\"outputDirectory\":\"out\",\"analyses\":[]} ")]
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
            analyses = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
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
            analyses = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
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
            analyses = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
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
            analyses = new Dictionary<string, object> { ["method-control-flow-outliers"] = new { } },
        });

        Assert.Throws<InvalidReviewInputException>(() => new ReviewConfigValidator(Registry()).Validate(temp.DirectoryPath, json));
    }

    private static string ValidJson() => """
        {
          "schemaVersion": 1,
          "solution": "Project.slnx",
          "outputDirectory": "reports/./current",
          "analyses": { "method-control-flow-outliers": {} }
        }
        """;
}
