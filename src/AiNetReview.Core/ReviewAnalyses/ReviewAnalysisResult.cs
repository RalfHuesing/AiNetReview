namespace AiNetReview.Core.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AiNetReview.Core.Findings;

public sealed class ReviewAnalysisResult
{
    private readonly ReadOnlyCollection<FindingDraft> findings;

    public ReviewAnalysisResult(IEnumerable<FindingDraft> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var copy = findings.ToArray();
        if (copy.Any(static finding => finding is null))
        {
            throw new ArgumentException("Findings cannot contain null values.", nameof(findings));
        }

        this.findings = Array.AsReadOnly(copy);
    }

    public static ReviewAnalysisResult Empty { get; } = new(Array.Empty<FindingDraft>());

    public IReadOnlyList<FindingDraft> Findings => findings;

    /// <summary>Conservative project-area exclusions caused by binding uncertainty.</summary>
    public IReadOnlyList<ReviewAnalysisScopeExclusion> ScopeExclusions { get; init; } = Array.Empty<ReviewAnalysisScopeExclusion>();
}

/// <summary>A project area excluded from candidate selection, with the binding reason.</summary>
public sealed record ReviewAnalysisScopeExclusion(string ProjectPath, string Reason);
