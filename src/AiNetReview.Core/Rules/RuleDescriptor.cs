namespace AiNetReview.Core.Rules;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

public sealed class RuleDescriptor
{
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
        bool isTemplate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (!StringComparer.Ordinal.Equals(ruleId, ruleId.Trim()))
        {
            throw new ArgumentException("Rule ID cannot start or end with whitespace.", nameof(ruleId));
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
    }

    public string RuleId { get; }

    public string Title { get; }

    public int BehaviorVersion { get; }

    public string Purpose { get; }

    public string Measurement { get; }

    public IReadOnlyList<string> ReviewQuestions => reviewQuestions;

    public IReadOnlyList<RuleOptionDescriptor> Options => options;

    public bool IsTemplate { get; }

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
