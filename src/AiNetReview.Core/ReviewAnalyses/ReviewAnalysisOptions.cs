namespace AiNetReview.Core.ReviewAnalyses;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using AiNetReview.Core.Analysis;

public sealed class ReviewAnalysisOptions
{
    private readonly ReadOnlyDictionary<string, JsonElement> values;
    private readonly ReadOnlyDictionary<string, JsonElement>? testValues;

    internal ReviewAnalysisOptions(SortedDictionary<string, JsonElement> values, SortedDictionary<string, JsonElement>? testValues = null)
    {
        this.values = new ReadOnlyDictionary<string, JsonElement>(values);
        this.testValues = testValues is null ? null : new ReadOnlyDictionary<string, JsonElement>(testValues);
    }

    public IReadOnlyDictionary<string, JsonElement> Values => values;

    public JsonElement this[string name] => values[name];

    public ReviewAnalysisOptions ForProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (testValues is null || !ReviewSourceClassifier.IsTestProject(project))
        {
            return this;
        }

        return new ReviewAnalysisOptions(new SortedDictionary<string, JsonElement>(testValues, StringComparer.Ordinal));
    }

    internal IReadOnlyDictionary<string, JsonElement>? TestValues => testValues;

    internal ReviewAnalysisOptions WithTestValues(SortedDictionary<string, JsonElement> resolvedTestValues) =>
        new(new SortedDictionary<string, JsonElement>(values, StringComparer.Ordinal), resolvedTestValues);
}
