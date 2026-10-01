namespace AiNetReview.Core.Configuration;

using System;
using System.Collections.Generic;
using AiNetReview.Core.ReviewAnalyses;

/// <summary>A validated, normalized configuration for one project root.</summary>
public sealed class ReviewConfig
{
    internal ReviewConfig(
        string projectRoot,
        string solutionPath,
        string outputDirectory,
        string resolvedSolutionPath,
        string resolvedOutputDirectory,
        IReadOnlyList<ConfiguredReviewAnalysis> analyses,
        IReadOnlyList<ConfiguredReviewAnalysis>? allAnalyses = null)
    {
        ProjectRoot = projectRoot;
        SolutionPath = solutionPath;
        OutputDirectory = outputDirectory;
        ResolvedSolutionPath = resolvedSolutionPath;
        ResolvedOutputDirectory = resolvedOutputDirectory;
        Analyses = analyses;
        AllAnalyses = allAnalyses ?? analyses;
    }

    public string ProjectRoot { get; }

    public string SolutionPath { get; }

    public string OutputDirectory { get; }

    public string ResolvedSolutionPath { get; }

    public string ResolvedOutputDirectory { get; }

    public IReadOnlyList<ConfiguredReviewAnalysis> Analyses { get; }

    /// <summary>All configured registered analyses, including those disabled for this run.</summary>
    public IReadOnlyList<ConfiguredReviewAnalysis> AllAnalyses { get; }
}

public sealed record ConfiguredReviewAnalysis(
    string AnalysisId,
    IReviewAnalysis Analysis,
    ReviewAnalysisOptions EffectiveOptions,
    ReviewAnalysisOptions? EffectiveTestOptions = null,
    IReadOnlyDictionary<string, bool>? ExplicitTestOptions = null,
    bool Enabled = true);
