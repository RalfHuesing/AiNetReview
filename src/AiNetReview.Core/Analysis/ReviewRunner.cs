namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using Microsoft.CodeAnalysis;

/// <summary>Runs configured rules against one loaded solution without retaining results between calls.</summary>
public sealed class ReviewRunner
{
    public async Task<ReviewRunResult> RunAsync(
        ReviewConfig config,
        LoadedSolution loadedSolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loadedSolution);
        var solution = loadedSolution.Solution;
        if (solution.FilePath is null || !PathComparer.Equals(
                ProjectPathResolver.Canonicalize(solution.FilePath),
                ProjectPathResolver.Canonicalize(config.ResolvedSolutionPath)))
        {
            throw new AnalysisFailedException("Loaded solution does not match the validated configuration.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var context = new ReviewContext(solution, config.ProjectRoot);
        var results = new List<RuleRunResult>();
        foreach (var configuredRule in config.Rules.OrderBy(static rule => rule.RuleId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RuleResult result;
            try
            {
                result = await configuredRule.Rule.ExecuteAsync(
                    context,
                    configuredRule.EffectiveOptions,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Rule '{configuredRule.RuleId}' returned no result.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AnalysisFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AnalysisFailedException($"Rule '{configuredRule.RuleId}' did not complete successfully.", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            results.Add(new RuleRunResult(configuredRule.RuleId, result));
        }

        return new ReviewRunResult(Array.AsReadOnly(results.ToArray()));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed record RuleRunResult(string RuleId, RuleResult Result);

public sealed record ReviewRunResult(IReadOnlyList<RuleRunResult> Rules);
