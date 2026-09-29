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
        IReadOnlyList<ConfiguredReviewAnalysis> analyses)
    {
        ProjectRoot = projectRoot;
        SolutionPath = solutionPath;
        OutputDirectory = outputDirectory;
        ResolvedSolutionPath = resolvedSolutionPath;
        ResolvedOutputDirectory = resolvedOutputDirectory;
        Analyses = analyses;
    }

    public string ProjectRoot { get; }

    public string SolutionPath { get; }

    public string OutputDirectory { get; }

    public string ResolvedSolutionPath { get; }

    public string ResolvedOutputDirectory { get; }

    public IReadOnlyList<ConfiguredReviewAnalysis> Analyses { get; }
}

public sealed record ConfiguredReviewAnalysis(string AnalysisId, IReviewAnalysis Analysis, ReviewAnalysisOptions EffectiveOptions);
