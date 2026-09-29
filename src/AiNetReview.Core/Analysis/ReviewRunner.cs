namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;
using Microsoft.CodeAnalysis;

/// <summary>Runs configured analyses against one loaded solution without retaining results between calls.</summary>
public sealed class ReviewRunner
{
    private readonly CurrentFindingValidator findingValidator = new();

    public async Task<ReviewRunResult> RunAsync(
        ReviewConfig config,
        LoadedSolution loadedSolution,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? baselineFiles = null)
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
        var context = new ReviewContext(solution, config.ProjectRoot, loadedSolution.MarkupDocuments);
        var results = new List<ReviewAnalysisRunResult>();
        foreach (var configuredAnalysis in config.Analyses.OrderBy(static analysis => analysis.AnalysisId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReviewAnalysisResult result;
            try
            {
                result = await configuredAnalysis.Analysis.ExecuteAsync(
                    context,
                    configuredAnalysis.EffectiveOptions,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Review analysis '{configuredAnalysis.AnalysisId}' returned no result.");
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
                throw new AnalysisFailedException($"Review analysis '{configuredAnalysis.AnalysisId}' did not complete successfully.", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var findings = await findingValidator.ValidateAndSortAsync(
                configuredAnalysis.AnalysisId,
                context,
                result.Findings,
                cancellationToken).ConfigureAwait(false);
            results.Add(new ReviewAnalysisRunResult(configuredAnalysis.AnalysisId, new ReviewAnalysisResult(findings)));
        }

        return new ReviewRunResult(Array.AsReadOnly(results.ToArray()))
        {
            Findings = ReviewFindingBuilder.Build(results, loadedSolution.SourceFiles, baselineFiles),
        };
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed record ReviewAnalysisRunResult(string AnalysisId, ReviewAnalysisResult Result)
{
    public int DetectedCount => Result.Findings.Count;
}

public sealed record ReviewRunResult(IReadOnlyList<ReviewAnalysisRunResult> Analyses)
{
    public int DetectedCount => Analyses.Sum(static analysis => analysis.DetectedCount);

    /// <summary>Per-finding source, comparison, and cross-analysis relationships for report generation.</summary>
    public IReadOnlyList<ReviewFinding> Findings { get; init; } = Array.Empty<ReviewFinding>();
}
