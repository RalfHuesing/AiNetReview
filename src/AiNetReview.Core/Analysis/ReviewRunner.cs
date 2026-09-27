namespace AiNetReview.Core.Analysis;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Storage;
using Microsoft.CodeAnalysis;

public sealed class ReviewRunner
{
    private readonly IFindingStore findingStore;
    private readonly FindingObservationFactory observationFactory;
    private readonly StateMachine stateMachine;

    public ReviewRunner(IFindingStore findingStore, FingerprintService? fingerprintService = null, StateMachine? stateMachine = null)
    {
        this.findingStore = findingStore ?? throw new ArgumentNullException(nameof(findingStore));
        observationFactory = new FindingObservationFactory(fingerprintService ?? new FingerprintService());
        this.stateMachine = stateMachine ?? new StateMachine();
    }

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

        var current = await findingStore.ReadAsync(config.ProjectRoot, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var runId = CreateRunId(DateTimeOffset.UtcNow);
        var context = new ReviewContext(solution, config.ProjectRoot);
        var activeRules = config.Rules.OrderBy(static rule => rule.RuleId, StringComparer.Ordinal).ToArray();
        var candidates = new Dictionary<FindingIdentity, FindingObservation>();
        foreach (var configuredRule in activeRules)
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

            foreach (var draft in result.Findings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FindingObservation observation;
                try
                {
                    observation = observationFactory.Create(config, solution, configuredRule, draft);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not AnalysisFailedException)
                {
                    throw new AnalysisFailedException($"Rule '{configuredRule.RuleId}' returned an invalid finding.", ex);
                }

                if (!candidates.TryAdd(observation.Identity, observation))
                {
                    throw new AnalysisFailedException("A rule emitted the same finding identity more than once.");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var nextFindings = new Dictionary<FindingIdentity, StoredFinding>(current.Findings);
        var events = new List<FindingTransition>();
        var activeRuleIds = activeRules.Select(static rule => rule.RuleId).ToHashSet(StringComparer.Ordinal);

        foreach (var pair in candidates
                     .OrderBy(static pair => pair.Key.RuleId, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.ProjectPath, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.SubjectId, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.Discriminator, StringComparer.Ordinal))
        {
            current.Findings.TryGetValue(pair.Key, out var previous);
            var result = stateMachine.Reconcile(previous, pair.Value, runId);
            nextFindings[pair.Key] = result.Finding;
            if (result.Event is not null)
            {
                events.Add(result.Event);
            }
        }

        foreach (var previous in current.Findings.Values
                     .Where(finding => activeRuleIds.Contains(finding.Identity.RuleId) && !candidates.ContainsKey(finding.Identity))
                     .OrderBy(static finding => finding.Identity.RuleId, StringComparer.Ordinal)
                     .ThenBy(static finding => finding.Identity.ProjectPath, StringComparer.Ordinal)
                     .ThenBy(static finding => finding.Identity.SourcePath, StringComparer.Ordinal)
                     .ThenBy(static finding => finding.Identity.SubjectId, StringComparer.Ordinal)
                     .ThenBy(static finding => finding.Identity.Discriminator, StringComparer.Ordinal))
        {
            var result = stateMachine.Resolve(previous, runId);
            nextFindings[previous.Identity] = result.Finding;
            if (result.Event is not null)
            {
                events.Add(result.Event);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var observations = candidates.Keys
            .OrderBy(static item => item.RuleId, StringComparer.Ordinal)
            .ThenBy(static item => item.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ThenBy(static item => item.SubjectId, StringComparer.Ordinal)
            .ThenBy(static item => item.Discriminator, StringComparer.Ordinal)
            .Select(identity => nextFindings[identity].Observation)
            .ToArray();
        var immutableFindings = new ReadOnlyDictionary<FindingIdentity, StoredFinding>(nextFindings);
        var run = new ReviewRunResult(runId, Array.AsReadOnly(events.ToArray()), Array.AsReadOnly(observations), immutableFindings);
        cancellationToken.ThrowIfCancellationRequested();
        await findingStore.CommitCompletedRunAsync(
            config.ProjectRoot,
            new FindingRunCommit(runId, Array.AsReadOnly(activeRules.Select(static rule => rule.RuleId).ToArray()), run.Events, run.Observations, run.CurrentFindings),
            cancellationToken).ConfigureAwait(false);
        return run;
    }

    private static string CreateRunId(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" +
        Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed record ReviewRunResult(
    string RunId,
    IReadOnlyList<FindingTransition> Events,
    IReadOnlyList<FindingObservation> Observations,
    IReadOnlyDictionary<FindingIdentity, StoredFinding> CurrentFindings);
