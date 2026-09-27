namespace AiNetReview.Core.Rules;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public sealed class RuleRegistry
{
    private readonly IReadOnlyDictionary<string, IReviewRule> rulesById;

    public RuleRegistry(IEnumerable<IReviewRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var byId = new Dictionary<string, IReviewRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                throw new ArgumentException("Rules cannot contain null values.", nameof(rules));
            }

            var descriptor = rule.Descriptor
                ?? throw new ArgumentException("Rules must provide a descriptor.", nameof(rules));
            if (!byId.TryAdd(descriptor.RuleId, rule))
            {
                throw new ArgumentException($"Duplicate rule ID '{descriptor.RuleId}'.", nameof(rules));
            }
        }

        Rules = Array.AsReadOnly(byId.Values.OrderBy(static rule => rule.Descriptor.RuleId, StringComparer.Ordinal).ToArray());
        rulesById = new ReadOnlyDictionary<string, IReviewRule>(byId);
    }

    public IReadOnlyList<IReviewRule> Rules { get; }

    public IReviewRule GetRequired(string ruleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        return rulesById.TryGetValue(ruleId, out var rule)
            ? rule
            : throw new KeyNotFoundException($"Rule '{ruleId}' is not registered.");
    }

    public bool TryGet(string ruleId, out IReviewRule? rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        return rulesById.TryGetValue(ruleId, out rule);
    }
}
