namespace AiNetReview.Core.Configuration;

using System;
using System.Collections.Generic;
using AiNetReview.Core.Rules;

/// <summary>A validated, normalized configuration for one project root.</summary>
public sealed class ReviewConfig
{
    internal ReviewConfig(
        string projectRoot,
        string solutionPath,
        string outputDirectory,
        string resolvedSolutionPath,
        string resolvedOutputDirectory,
        IReadOnlyList<ConfiguredRule> rules)
    {
        ProjectRoot = projectRoot;
        SolutionPath = solutionPath;
        OutputDirectory = outputDirectory;
        ResolvedSolutionPath = resolvedSolutionPath;
        ResolvedOutputDirectory = resolvedOutputDirectory;
        Rules = rules;
    }

    public string ProjectRoot { get; }

    public string SolutionPath { get; }

    public string OutputDirectory { get; }

    public string ResolvedSolutionPath { get; }

    public string ResolvedOutputDirectory { get; }

    public IReadOnlyList<ConfiguredRule> Rules { get; }
}

public sealed record ConfiguredRule(string RuleId, IReviewRule Rule, RuleOptions EffectiveOptions);
