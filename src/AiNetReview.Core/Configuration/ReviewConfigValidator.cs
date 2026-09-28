namespace AiNetReview.Core.Configuration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiNetReview.Core.Rules;

public sealed class ReviewConfigValidator
{
    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion", "solution", "outputDirectory", "rules",
    };

    private readonly RuleRegistry registry;

    public ReviewConfigValidator(RuleRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ReviewConfig Load(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        if (!Path.IsPathFullyQualified(configPath) ||
            !Path.GetFileName(configPath).Equals("ainetreview.json", StringComparison.Ordinal))
        {
            throw new InvalidReviewInputException("Configuration path must be an absolute path to 'ainetreview.json'.");
        }

        var canonicalConfigPath = ProjectPathResolver.Canonicalize(configPath);
        if (!File.Exists(canonicalConfigPath))
        {
            throw new InvalidReviewInputException("Configuration file 'ainetreview.json' does not exist.");
        }

        try
        {
            return Validate(Path.GetDirectoryName(canonicalConfigPath)!, File.ReadAllText(canonicalConfigPath));
        }
        catch (IOException ex)
        {
            throw new InvalidReviewInputException("Configuration file could not be read.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidReviewInputException("Configuration file could not be read.", ex);
        }
    }

    public ReviewConfig Validate(string projectRoot, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(json);

        string canonicalRoot;
        try
        {
            canonicalRoot = ProjectPathResolver.Canonicalize(projectRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidReviewInputException("Project root could not be resolved.", ex);
        }

        if (!Directory.Exists(canonicalRoot))
        {
            throw new InvalidReviewInputException("Project root does not exist.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
            RejectDuplicateKeys(document.RootElement);
            return ValidateDocument(canonicalRoot, document.RootElement, null);
        }
        catch (JsonException ex)
        {
            throw new InvalidReviewInputException("Configuration is not valid JSON.", ex);
        }
    }

    /// <summary>Validates a review configuration for an explicitly started audit whose reports are stored separately.</summary>
    public ReviewConfig ValidateForAudit(string projectRoot, string standardConfigJson, string absoluteOutputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(standardConfigJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteOutputDirectory);
        if (!Path.IsPathFullyQualified(absoluteOutputDirectory))
        {
            throw new InvalidReviewInputException("Audit output directory must be an absolute path.");
        }
        if (File.Exists(absoluteOutputDirectory))
        {
            throw new InvalidReviewInputException("Audit output path must be a directory.");
        }

        string canonicalRoot;
        string canonicalOutput;
        try
        {
            canonicalRoot = ProjectPathResolver.Canonicalize(projectRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidReviewInputException("Audit root or output directory could not be resolved.", ex);
        }

        if (!Directory.Exists(canonicalRoot))
        {
            throw new InvalidReviewInputException("Project root does not exist.");
        }

        try
        {
            canonicalOutput = ProjectPathResolver.Canonicalize(absoluteOutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidReviewInputException("Audit output directory could not be resolved.", ex);
        }

        try
        {
            using var document = JsonDocument.Parse(standardConfigJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
            RejectDuplicateKeys(document.RootElement);
            return ValidateDocument(canonicalRoot, document.RootElement, canonicalOutput);
        }
        catch (JsonException ex)
        {
            throw new InvalidReviewInputException("Configuration is not valid JSON.", ex);
        }
    }

    private ReviewConfig ValidateDocument(string root, JsonElement document, string? auditOutputDirectory)
    {
        RequireKind(document, JsonValueKind.Object, "Configuration must be a JSON object.");
        foreach (var property in document.EnumerateObject())
        {
            if (!RootProperties.Contains(property.Name))
            {
                throw new InvalidReviewInputException($"Unknown configuration field '{property.Name}'.");
            }
        }

        if (!document.TryGetProperty("schemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number || !schemaVersion.TryGetInt32(out var version) || version != 1)
        {
            throw new InvalidReviewInputException("'schemaVersion' must be the integer 1.");
        }

        var solutionValue = RequiredString(document, "solution");
        var outputValue = RequiredString(document, "outputDirectory");
        if (!document.TryGetProperty("rules", out var configuredRules))
        {
            throw new InvalidReviewInputException("'rules' is required.");
        }

        RequireKind(configuredRules, JsonValueKind.Object, "'rules' must be an object.");
        if (!configuredRules.EnumerateObject().Any())
        {
            throw new InvalidReviewInputException("'rules' must contain at least one registered rule.");
        }

        var solution = ProjectPathResolver.ResolveRelative(root, solutionValue, "solution");
        if (!Path.GetExtension(solution).Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(solution).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidReviewInputException("'solution' must point to a .sln or .slnx file.");
        }

        if (!File.Exists(solution))
        {
            throw new InvalidReviewInputException("Configured solution file does not exist.");
        }

        var output = auditOutputDirectory ?? ProjectPathResolver.ResolveRelative(root, outputValue, "outputDirectory");
        if (File.Exists(output))
        {
            throw new InvalidReviewInputException("Output path must be a directory.");
        }

        var activeRules = new List<ConfiguredRule>();
        foreach (var configuredRule in configuredRules.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            if (!registry.TryGet(configuredRule.Name, out var rule) || rule is null)
            {
                throw new InvalidReviewInputException($"Unknown rule ID '{configuredRule.Name}'.");
            }

            RequireKind(configuredRule.Value, JsonValueKind.Object, $"Configuration for rule '{configuredRule.Name}' must be an object.");
            var enabled = rule.Descriptor.DefaultEnabled;
            if (configuredRule.Value.TryGetProperty("enabled", out var enabledValue))
            {
                if (enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidReviewInputException($"Configuration field 'enabled' for rule '{configuredRule.Name}' must be a boolean.");
                }

                enabled = enabledValue.GetBoolean();
            }

            var optionValues = configuredRule.Value.EnumerateObject()
                .Where(static option => !string.Equals(option.Name, "enabled", StringComparison.Ordinal))
                .Select(static option => new KeyValuePair<string, JsonElement>(option.Name, option.Value));
            RuleOptions options;
            try
            {
                options = rule.Descriptor.ResolveOptions(optionValues);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidReviewInputException(ex.Message, ex);
            }

            if (enabled)
            {
                activeRules.Add(new ConfiguredRule(configuredRule.Name, rule, options));
            }
        }

        try
        {
            Directory.CreateDirectory(output);
            output = ProjectPathResolver.Canonicalize(output);
            if (auditOutputDirectory is null && !ProjectPathResolver.IsWithin(root, output))
            {
                throw new InvalidReviewInputException("Output directory resolves outside the project root.");
            }
        }
        catch (InvalidReviewInputException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidReviewInputException("Output directory could not be created safely.", ex);
        }

        return new ReviewConfig(
            root,
            ProjectPathResolver.ToRelativeForwardSlashes(root, solution),
            ProjectPathResolver.ToRelativeForwardSlashes(root, output),
            solution,
            output,
            activeRules.AsReadOnly());
    }

    private static string RequiredString(JsonElement document, string name)
    {
        if (!document.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidReviewInputException($"'{name}' must be a nonempty string.");
        }

        var value = property.GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidReviewInputException($"'{name}' must be a nonempty string.");
    }

    private static void RequireKind(JsonElement value, JsonValueKind expected, string message)
    {
        if (value.ValueKind != expected)
        {
            throw new InvalidReviewInputException(message);
        }
    }

    private static void RejectDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidReviewInputException($"Duplicate JSON field '{property.Name}'.");
                }

                RejectDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateKeys(item);
            }
        }
    }
}
