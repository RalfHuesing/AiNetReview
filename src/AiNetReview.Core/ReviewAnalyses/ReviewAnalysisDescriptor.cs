namespace AiNetReview.Core.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

public sealed class ReviewAnalysisDescriptor
{
    private const int MaximumAnalysisIdLength = 252;

    private readonly ReadOnlyCollection<ReviewAnalysisOptionDescriptor> options;
    private readonly ReadOnlyCollection<string> reviewQuestions;
    private readonly IReadOnlyDictionary<string, ReviewAnalysisOptionDescriptor> optionsByName;

    public ReviewAnalysisDescriptor(
        string analysisId,
        string title,
        int behaviorVersion,
        string purpose,
        string measurement,
        IEnumerable<string> reviewQuestions,
        IEnumerable<ReviewAnalysisOptionDescriptor>? options = null,
        bool isTemplate = false,
        bool defaultEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        if (!StringComparer.Ordinal.Equals(analysisId, analysisId.Trim()))
        {
            throw new ArgumentException("Review analysis ID cannot start or end with whitespace.", nameof(analysisId));
        }

        if (!IsSafeAnalysisId(analysisId))
        {
            throw new ArgumentException(
                $"Review analysis ID must be a lowercase ASCII slug of at most {MaximumAnalysisIdLength} characters and cannot be a reserved Windows device name.",
                nameof(analysisId));
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

        var optionList = (options ?? Array.Empty<ReviewAnalysisOptionDescriptor>()).ToArray();
        if (optionList.Any(static option => option is null))
        {
            throw new ArgumentException("Option descriptors cannot contain null values.", nameof(options));
        }

        var optionsByNameBuilder = new Dictionary<string, ReviewAnalysisOptionDescriptor>(StringComparer.Ordinal);
        foreach (var option in optionList)
        {
            if (!optionsByNameBuilder.TryAdd(option.Name, option))
            {
                throw new ArgumentException($"Duplicate option name '{option.Name}'.", nameof(options));
            }
        }

        this.options = Array.AsReadOnly(optionList.OrderBy(static option => option.Name, StringComparer.Ordinal).ToArray());
        optionsByName = new ReadOnlyDictionary<string, ReviewAnalysisOptionDescriptor>(optionsByNameBuilder);
        AnalysisId = analysisId;
        Title = title;
        BehaviorVersion = behaviorVersion;
        Purpose = purpose;
        Measurement = measurement;
        IsTemplate = isTemplate;
        DefaultEnabled = defaultEnabled;
    }

    public string AnalysisId { get; }

    public string Title { get; }

    public int BehaviorVersion { get; }

    public string Purpose { get; }

    public string Measurement { get; }

    public IReadOnlyList<string> ReviewQuestions => reviewQuestions;

    public IReadOnlyList<ReviewAnalysisOptionDescriptor> Options => options;

    public bool IsTemplate { get; }

    public bool DefaultEnabled { get; }

    private static bool IsSafeAnalysisId(string analysisId)
    {
        if (analysisId.Length > MaximumAnalysisIdLength || !IsAsciiLowerAlphaNumeric(analysisId[0]) || analysisId[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in analysisId)
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

        return !IsReservedWindowsDeviceName(analysisId);
    }

    private static bool IsAsciiLowerAlphaNumeric(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsReservedWindowsDeviceName(string analysisId) =>
        analysisId is "con" or "prn" or "aux" or "nul"
        || analysisId.Length == 4
        && (analysisId.StartsWith("com", StringComparison.Ordinal) || analysisId.StartsWith("lpt", StringComparison.Ordinal))
        && analysisId[3] is >= '1' and <= '9';

    public ReviewAnalysisOptions ResolveOptions(IEnumerable<KeyValuePair<string, JsonElement>>? configuredOptions = null)
    {
        var resolved = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            resolved.Add(option.Name, option.DefaultValue.Clone());
        }

        if (configuredOptions is null)
        {
            return new ReviewAnalysisOptions(resolved);
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
                throw new ArgumentException($"Unknown option '{pair.Key}' for analysis '{AnalysisId}'.", nameof(configuredOptions));
            }

            if (!descriptor.IsValidValue(pair.Value))
            {
                throw new ArgumentException($"Invalid value for option '{pair.Key}' of analysis '{AnalysisId}'.", nameof(configuredOptions));
            }

            resolved[pair.Key] = pair.Value.Clone();
        }

        return new ReviewAnalysisOptions(resolved);
    }
}
