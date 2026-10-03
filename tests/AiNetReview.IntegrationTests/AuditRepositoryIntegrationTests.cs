namespace AiNetReview.IntegrationTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using Microsoft.Extensions.DependencyInjection;

public sealed partial class AuditRepositoryIntegrationTests
{
    private const string TargetEnvironmentVariable = "AINETREVIEW_AUDIT_TARGET";

    [Fact]
    [Trait("Category", "Audit")]
    public async Task ConfiguredTarget_PublishesOnlyCentralMarkdown()
    {
        var selectedTargetName = Environment.GetEnvironmentVariable(TargetEnvironmentVariable);
        Assert.Matches(TargetNamePattern(), selectedTargetName ?? string.Empty);
        var targetName = selectedTargetName ?? throw new InvalidOperationException("The manual audit target was not selected.");

        var hostRoot = SolutionRootLocator.Find();
        var profilePath = Path.Combine(hostRoot, "audit-targets", targetName + ".json");
        Assert.True(File.Exists(profilePath), $"Audit profile was not found: '{profilePath}'.");
        using var profileDocument = JsonDocument.Parse(await File.ReadAllTextAsync(profilePath));
        var profile = profileDocument.RootElement;
        Assert.Equal(JsonValueKind.Object, profile.ValueKind);
        if (!IsProfileEnabled(profile))
        {
            return;
        }

        AssertProfileProperties(profile);

        var repositoryPathValue = ReadNonemptyString(profile, "repositoryPath");
        var repositoryPath = Path.IsPathFullyQualified(repositoryPathValue)
            ? Path.GetFullPath(repositoryPathValue)
            : Path.GetFullPath(Path.Combine(hostRoot, repositoryPathValue));
        Assert.True(Directory.Exists(repositoryPath), $"Target repository does not exist: '{repositoryPath}'.");

        var solution = ReadNonemptyString(profile, "solution");
        Assert.DoesNotContain('\\', solution);
        Assert.False(Path.IsPathFullyQualified(solution), "The solution path must be relative to repositoryPath.");
        var solutionPath = Path.GetFullPath(Path.Combine(repositoryPath, solution.Replace('/', Path.DirectorySeparatorChar)));
        Assert.True(IsWithin(repositoryPath, solutionPath), "The solution path must stay inside repositoryPath.");
        Assert.True(File.Exists(solutionPath), $"Configured solution does not exist: '{solutionPath}'.");
        Assert.Contains(Path.GetExtension(solutionPath), new[] { ".sln", ".slnx" }, StringComparer.OrdinalIgnoreCase);

        var analyses = profile.GetProperty("analyses");
        Assert.Equal(JsonValueKind.Object, analyses.ValueKind);
        Assert.NotEmpty(analyses.EnumerateObject());
        var standardConfigJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution,
            outputDirectory = "audit-reporting",
            analyses,
        });

        var targetConfigPath = Path.Combine(repositoryPath, "ainetreview.json");
        var targetConfigExisted = File.Exists(targetConfigPath);
        var targetConfigBytes = targetConfigExisted ? await File.ReadAllBytesAsync(targetConfigPath) : null;

        var outputDirectory = Path.Combine(hostRoot, "audit-reporting", targetName);
        await using var services = BuildProductionServices();
        var config = services.GetRequiredService<ReviewConfigValidator>()
            .ValidateForAudit(repositoryPath, standardConfigJson, outputDirectory);
        using var loaded = await services.GetRequiredService<SolutionLoader>().LoadAsync(config);

        var result = await services.GetRequiredService<ReviewRunner>().RunAsync(config, loaded);
        var published = await services.GetRequiredService<AiNetReview.Core.Reporting.MarkdownReportWriter>()
            .WriteAsync(config, result);

        var runDirectory = Path.Combine(outputDirectory, published.RunId);
        Assert.True(File.Exists(Path.Combine(runDirectory, "index.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "maps", "audit", "index.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "maps", "index.md")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "maps", "projects.md")));
        var index = await File.ReadAllTextAsync(Path.Combine(runDirectory, "index.md"));
        var repositoryMetadata = Assert.Single(index.Split('\n').Where(static line => line.StartsWith("Repository:", StringComparison.Ordinal)));
        Assert.Equal(
            $"Repository: {MarkdownReportWriter.FormatCodeSpan(repositoryPath)}; solution: {MarkdownReportWriter.FormatCodeSpan(solution)}.",
            repositoryMetadata);
        Assert.Contains("maps/audit/index.md", index, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(outputDirectory, published.RunId, "index.md"),
            Path.GetFullPath(Path.Combine(repositoryPath, published.IndexPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(targetConfigExisted, File.Exists(targetConfigPath));
        if (targetConfigBytes is not null)
        {
            Assert.Equal(targetConfigBytes, await File.ReadAllBytesAsync(targetConfigPath));
        }

        var publishedFiles = Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(runDirectory, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.DoesNotContain("findings.json", publishedFiles, StringComparer.Ordinal);
        Assert.All(publishedFiles, path => Assert.True(
            path == "index.md"
                || (path.StartsWith("maps/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal))
                || (new[] { "production", "tests", "mixed" }.Any(area =>
                    path.StartsWith(area + "/", StringComparison.Ordinal))
                    && path.EndsWith(".md", StringComparison.Ordinal)),
            $"Unexpected manual audit artifact: '{path}'."));
        Assert.Contains("index.md", publishedFiles, StringComparer.Ordinal);
    }

    [Fact]
    public void AuditProfileEnabled_DefaultsToTrue_AndCanDisableTheWholeTarget()
    {
        using var defaultProfile = JsonDocument.Parse("{}");
        using var enabledProfile = JsonDocument.Parse("{\"enabled\":true}");
        using var disabledProfile = JsonDocument.Parse("{\"enabled\":false}");
        using var invalidProfile = JsonDocument.Parse("{\"enabled\":\"false\"}");

        Assert.True(IsProfileEnabled(defaultProfile.RootElement));
        Assert.True(IsProfileEnabled(enabledProfile.RootElement));
        Assert.False(IsProfileEnabled(disabledProfile.RootElement));
        Assert.Throws<InvalidOperationException>(() => IsProfileEnabled(invalidProfile.RootElement));
    }

    private static ServiceProvider BuildProductionServices()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewAnalyses();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static void AssertProfileProperties(JsonElement profile)
    {
        var expected = new HashSet<string>(["enabled", "repositoryPath", "solution", "analyses"], StringComparer.Ordinal);
        var enabledIsOptional = true;
        foreach (var property in profile.EnumerateObject())
        {
            var wasExpected = expected.Remove(property.Name);
            Assert.True(wasExpected, $"Unexpected or duplicate profile property '{property.Name}'.");
            if (property.Name == "enabled")
            {
                enabledIsOptional = false;
            }
        }

        if (enabledIsOptional)
        {
            expected.Remove("enabled");
        }

        Assert.Empty(expected);
    }

    private static bool IsProfileEnabled(JsonElement profile)
    {
        if (!profile.TryGetProperty("enabled", out var enabled))
        {
            return true;
        }

        if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException("Audit profile field 'enabled' must be a boolean.");
        }

        return enabled.GetBoolean();
    }

    private static string ReadNonemptyString(JsonElement element, string propertyName)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Profile is missing '{propertyName}'.");
        Assert.Equal(JsonValueKind.String, property.ValueKind);
        var value = property.GetString();
        Assert.False(string.IsNullOrWhiteSpace(value), $"Profile field '{propertyName}' must be nonempty.");
        return value!;
    }

    private static bool IsWithin(string root, string path)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var canonicalPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return canonicalPath.Equals(canonicalRoot, comparison)
            || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, comparison);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TargetNamePattern();

}
