namespace AiNetReview.Core.ReviewAnalyses;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

public sealed class ReviewAnalysisOptions
{
    private readonly ReadOnlyDictionary<string, JsonElement> values;

    internal ReviewAnalysisOptions(SortedDictionary<string, JsonElement> values)
    {
        this.values = new ReadOnlyDictionary<string, JsonElement>(values);
    }

    public IReadOnlyDictionary<string, JsonElement> Values => values;

    public JsonElement this[string name] => values[name];
}
