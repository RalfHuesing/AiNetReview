namespace AiNetReview.Core.Rules;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

public sealed class RuleDescriptor
{
    private const int MaximumRuleIdLength = 252;

    private readonly ReadOnlyCollection<RuleOptionDescriptor> options;
    private readonly ReadOnlyCollection<string> reviewQuestions;
    private readonly IReadOnlyDictionary<string, RuleOptionDescriptor> optionsByName;

    public RuleDescriptor(
        string ruleId,
        string title,
        int behaviorVersion,
        string purpose,
        string measurement,
        IEnumerable<string> reviewQuestions,
        IEnumerable<RuleOptionDescriptor>? options = null,
        bool isTemplate = false,
        bool defaultEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (!StringComparer.Ordinal.Equals(ruleId, ruleId.Trim()))
        {
            throw new ArgumentException("Rule ID cannot start or end with whitespace.", nameof(ruleId));
        }

        if (!IsSafeRuleId(ruleId))
        {
            throw new ArgumentException(
                $"Rule ID must be a lowercase ASCII slug of at most {MaximumRuleIdLength} characters and cannot be a reserved Windows device name.",
                nameof(ruleId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(measurement);
        ArgumentNullException.ThrowIfNull(reviewQuestions);
        if (behaviorVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(behaviorVersion), "Behavior version must be positive.");
        }

        this.reviewQuestions = Array.AsReadOnly(reviewQuestions.ToArray());
        if (this.reviewQuestions.Count == 0 || this.reviewQuestions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one nonempty review question is required.", nameof(reviewQuestions));
        }

        var optionList = (options ?? Array.Empty<RuleOptionDescriptor>()).ToArray();
        if (optionList.Any(static option => option is null))
        {
            throw new ArgumentException("Option descriptors cannot contain null values.", nameof(options));
        }

        var optionsByNameBuilder = new Dictionary<string, RuleOptionDescriptor>(StringComparer.Ordinal);
        foreach (var option in optionList)
        {
            if (!optionsByNameBuilder.TryAdd(option.Name, option))
            {
                throw new ArgumentException($"Duplicate option name '{option.Name}'.", nameof(options));
            }
        }

        this.options = Array.AsReadOnly(optionList.OrderBy(static option => option.Name, StringComparer.Ordinal).ToArray());
        optionsByName = new ReadOnlyDictionary<string, RuleOptionDescriptor>(optionsByNameBuilder);
        RuleId = ruleId;
        Title = title;
        BehaviorVersion = behaviorVersion;
        Purpose = purpose;
        Measurement = measurement;
        IsTemplate = isTemplate;
        DefaultEnabled = defaultEnabled;
    }

    public string RuleId { get; }

    public string Title { get; }

    public int BehaviorVersion { get; }

    public string Purpose { get; }

    public string Measurement { get; }

    public IReadOnlyList<string> ReviewQuestions => reviewQuestions;

    public IReadOnlyList<RuleOptionDescriptor> Options => options;

    public bool IsTemplate { get; }

    public bool DefaultEnabled { get; }

    private static bool IsSafeRuleId(string ruleId)
    {
        if (ruleId.Length > MaximumRuleIdLength || !IsAsciiLowerAlphaNumeric(ruleId[0]) || ruleId[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in ruleId)
        {
            if (IsAsciiLowerAlphaNumeric(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character != '-' || previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = true;
        }

        return !IsReservedWindowsDeviceName(ruleId);
    }

    private static bool IsAsciiLowerAlphaNumeric(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsReservedWindowsDeviceName(string ruleId) =>
        ruleId is "con" or "prn" or "aux" or "nul"
        || ruleId.Length == 4
        && (ruleId.StartsWith("com", StringComparison.Ordinal) || ruleId.StartsWith("lpt", StringComparison.Ordinal))
        && ruleId[3] is >= '1' and <= '9';

    public RuleOptions ResolveOptions(IEnumerable<KeyValuePair<string, JsonElement>>? configuredOptions = null)
    {
        var resolved = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            resolved.Add(option.Name, option.DefaultValue.Clone());
        }

        if (configuredOptions is null)
        {
            return new RuleOptions(resolved);
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in configuredOptions)
        {
            if (!seenNames.Add(pair.Key))
            {
                throw new ArgumentException($"Duplicate option name '{pair.Key}'.", nameof(configuredOptions));
            }

            if (!optionsByName.TryGetValue(pair.Key, out var descriptor))
            {
                throw new ArgumentException($"Unknown option '{pair.Key}' for rule '{RuleId}'.", nameof(configuredOptions));
            }

            if (!descriptor.IsValidValue(pair.Value))
            {
                throw new ArgumentException($"Invalid value for option '{pair.Key}' of rule '{RuleId}'.", nameof(configuredOptions));
            }

            resolved[pair.Key] = pair.Value.Clone();
        }

        return new RuleOptions(resolved);
    }
}
