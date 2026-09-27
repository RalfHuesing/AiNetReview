namespace AiNetReview.Core.Rules;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

public sealed class RuleOptions
{
    private readonly ReadOnlyDictionary<string, JsonElement> values;

    internal RuleOptions(SortedDictionary<string, JsonElement> values)
    {
        this.values = new ReadOnlyDictionary<string, JsonElement>(values);
    }

    public IReadOnlyDictionary<string, JsonElement> Values => values;

    public JsonElement this[string name] => values[name];
}
