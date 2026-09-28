namespace AiNetReview.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiNetReview.Bootstrap;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using Microsoft.Extensions.DependencyInjection;

public sealed partial class AuditRepositoryIntegrationTests
{
    private const string TargetEnvironmentVariable = "AINETREVIEW_AUDIT_TARGET";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    [Trait("Category", "Audit")]
    public async Task ConfiguredTarget_PublishesCentralMarkdownAndFindingsJson()
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

        var rules = profile.GetProperty("rules");
        Assert.Equal(JsonValueKind.Object, rules.ValueKind);
        Assert.NotEmpty(rules.EnumerateObject());
        var standardConfigJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            solution,
            outputDirectory = "audit-reporting",
            rules,
        });

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
        Assert.True(Directory.Exists(Path.Combine(runDirectory, "rules")));
        AssertMarkdownLinksResolve(runDirectory, repositoryPath);
        Assert.Equal(Path.Combine(outputDirectory, published.RunId, "index.md"),
            Path.GetFullPath(Path.Combine(repositoryPath, published.IndexPath.Replace('/', Path.DirectorySeparatorChar))));

        var gitState = await TryReadGitStateAsync(repositoryPath);
        var effectiveRules = config.Rules
            .OrderBy(static rule => rule.RuleId, StringComparer.Ordinal)
            .Select(rule => new
            {
                ruleId = rule.RuleId,
                title = rule.Rule.Descriptor.Title,
                behaviorVersion = rule.Rule.Descriptor.BehaviorVersion,
                effectiveOptions = rule.EffectiveOptions.Values.ToDictionary(
                    static option => option.Key,
                    static option => option.Value,
                    StringComparer.Ordinal),
            })
            .ToArray();
        var findingRecords = result.Rules
            .SelectMany(rule => rule.Result.Findings.Select(finding => new
            {
                ruleId = rule.RuleId,
                projectPath = finding.ProjectPath,
                sourcePath = finding.SourcePath,
                subjectId = finding.SubjectId,
                discriminator = finding.Discriminator,
                startLine = finding.StartLine,
                rationale = finding.Rationale,
                metrics = finding.Metrics,
                evidence = finding.Evidence.Select(static item => new
                {
                    sourcePath = item.SourcePath,
                    line = item.Line,
                    label = item.Label,
                    detail = item.Detail,
                    snippet = item.Snippet,
                }),
            }))
            .ToArray();
        var findingsDocument = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            repository = new { name = targetName, path = repositoryPath },
            solution = new { path = solution, absolutePath = solutionPath },
            git = new { commit = gitState.Commit, workingTreeDirty = gitState.WorkingTreeDirty },
            runId = published.RunId,
            rules = effectiveRules,
            counts = new
            {
                detected = result.DetectedCount,
                byRule = result.Rules.ToDictionary(static rule => rule.RuleId, static rule => rule.DetectedCount, StringComparer.Ordinal),
            },
            findings = findingRecords,
        };
        var findingsPath = Path.Combine(runDirectory, "findings.json");
        await WriteFindingsJsonAsync(findingsPath, JsonSerializer.Serialize(findingsDocument, JsonOptions));
        using var serializedFindings = JsonDocument.Parse(await File.ReadAllTextAsync(findingsPath));
        var findingsRoot = serializedFindings.RootElement;
        Assert.Equal(published.RunId, findingsRoot.GetProperty("runId").GetString());
        Assert.True(DateTimeOffset.TryParse(findingsRoot.GetProperty("generatedAtUtc").GetString(), out _));
        Assert.Equal(effectiveRules.Length, findingsRoot.GetProperty("rules").GetArrayLength());
        Assert.Equal(result.DetectedCount, findingsRoot.GetProperty("counts").GetProperty("detected").GetInt32());
        Assert.Equal(result.DetectedCount, findingsRoot.GetProperty("findings").GetArrayLength());
        Assert.True(findingsRoot.GetProperty("git").TryGetProperty("workingTreeDirty", out _));
    }

    private static ServiceProvider BuildProductionServices()
    {
        var services = new ServiceCollection();
        services.AddAiNetReviewServices();
        services.AddAiNetReviewRules();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static void AssertProfileProperties(JsonElement profile)
    {
        var expected = new HashSet<string>(["repositoryPath", "solution", "rules"], StringComparer.Ordinal);
        foreach (var property in profile.EnumerateObject())
        {
            Assert.True(expected.Remove(property.Name), $"Unexpected or duplicate profile property '{property.Name}'.");
        }

        Assert.Empty(expected);
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

    private static async Task<(string? Commit, bool? WorkingTreeDirty)> TryReadGitStateAsync(string repositoryPath)
    {
        var commit = await RunGitAsync(repositoryPath, "rev-parse", "HEAD");
        if (commit is null)
        {
            return (null, null);
        }

        var status = await RunGitAsync(repositoryPath, "status", "--porcelain");
        return (commit, status is null ? null : status.Length > 0);
    }

    private static async Task<string?> RunGitAsync(string repositoryPath, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                return null;
            }

            _ = await error;
            return process.ExitCode == 0 ? (await output).Trim() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or System.Threading.Tasks.TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task WriteFindingsJsonAsync(string path, string contents)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, contents + Environment.NewLine, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void AssertMarkdownLinksResolve(string runDirectory, string repositoryPath)
    {
        var linkPattern = SourceLinkPattern();
        foreach (var reportPath in Directory.EnumerateFiles(Path.Combine(runDirectory, "rules"), "*.md"))
        {
            var report = File.ReadAllText(reportPath);
            foreach (Match link in linkPattern.Matches(report))
            {
                var target = Uri.UnescapeDataString(link.Groups["target"].Value);
                var sourcePath = Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile
                    ? uri.LocalPath
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, target.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(sourcePath), $"Markdown source link does not resolve: '{target}' from '{reportPath}'.");
                Assert.True(IsWithin(repositoryPath, sourcePath), $"Markdown source link escaped its target repository: '{target}'.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TargetNamePattern();

    [GeneratedRegex(@"\]\((?<target>[^)]+)#L[0-9]+\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SourceLinkPattern();
}
