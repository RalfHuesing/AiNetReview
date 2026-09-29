namespace AiNetReview.Core.ReviewAnalyses;

using System;
using System.Text.Json;

public sealed class ReviewAnalysisOptionDescriptor
{
    private readonly Func<JsonElement, bool>? validator;

    public ReviewAnalysisOptionDescriptor(
        string name,
        string description,
        JsonElement defaultValue,
        Func<JsonElement, bool>? validator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!StringComparer.Ordinal.Equals(name, name.Trim()))
        {
            throw new ArgumentException("Option name cannot start or end with whitespace.", nameof(name));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (defaultValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new ArgumentException("Option defaults must have a JSON value and type.", nameof(defaultValue));
        }

        Name = name;
        Description = description;
        DefaultValue = defaultValue.Clone();
        this.validator = validator;
        if (!IsValidValue(DefaultValue))
        {
            throw new ArgumentException("The option validator must accept the default value.", nameof(validator));
        }
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement DefaultValue { get; }

    public bool IsValidValue(JsonElement value) =>
        value.ValueKind == DefaultValue.ValueKind && (validator is null || validator(value));

    public static ReviewAnalysisOptionDescriptor String(
        string name,
        string description,
        string defaultValue,
        Func<string, bool>? validator = null)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return new ReviewAnalysisOptionDescriptor(
            name,
            description,
            JsonSerializer.SerializeToElement(defaultValue),
            validator is null
                ? null
                : value => value.ValueKind == JsonValueKind.String && validator(value.GetString()!));
    }
}
