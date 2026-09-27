namespace AiNetReview.Core.Rules;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AiNetReview.Core.Findings;

public sealed class RuleResult
{
    private readonly ReadOnlyCollection<FindingDraft> findings;

    public RuleResult(IEnumerable<FindingDraft> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var copy = findings.ToArray();
        if (copy.Any(static finding => finding is null))
        {
            throw new ArgumentException("Findings cannot contain null values.", nameof(findings));
        }

        this.findings = Array.AsReadOnly(copy);
    }

    public static RuleResult Empty { get; } = new(Array.Empty<FindingDraft>());

    public IReadOnlyList<FindingDraft> Findings => findings;
}
