namespace AiNetReview.Core.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public sealed class ReviewAnalysisRegistry
{
    private readonly IReadOnlyDictionary<string, IReviewAnalysis> analysesById;

    public ReviewAnalysisRegistry(IEnumerable<IReviewAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        var byId = new Dictionary<string, IReviewAnalysis>(StringComparer.Ordinal);
        foreach (var analysis in analyses)
        {
            if (analysis is null)
            {
                throw new ArgumentException("Analyses cannot contain null values.", nameof(analyses));
            }

            var descriptor = analysis.Descriptor
                ?? throw new ArgumentException("Analyses must provide a descriptor.", nameof(analyses));
            if (!byId.TryAdd(descriptor.AnalysisId, analysis))
            {
                throw new ArgumentException($"Duplicate analysis ID '{descriptor.AnalysisId}'.", nameof(analyses));
            }
        }

        Analyses = Array.AsReadOnly(byId.Values.OrderBy(static analysis => analysis.Descriptor.AnalysisId, StringComparer.Ordinal).ToArray());
        analysesById = new ReadOnlyDictionary<string, IReviewAnalysis>(byId);
    }

    public IReadOnlyList<IReviewAnalysis> Analyses { get; }

    public IReviewAnalysis GetRequired(string analysisId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        return analysesById.TryGetValue(analysisId, out var analysis)
            ? analysis
            : throw new KeyNotFoundException($"Review analysis '{analysisId}' is not registered.");
    }

    public bool TryGet(string analysisId, out IReviewAnalysis? analysis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        return analysesById.TryGetValue(analysisId, out analysis);
    }
}
