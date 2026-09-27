namespace AiNetReview.Core.Findings;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using AiNetReview.Core.Rules;

public sealed class StateMachine
{
    public ReconciliationResult Reconcile(StoredFinding? previous, FindingObservation candidate, string runId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var findingId = previous?.FindingId ?? CreateFindingId();
        var observation = candidate with { FindingId = findingId };
        if (previous is null)
        {
            return CreateChange(null, observation, runId, FindingEventType.New, FindingState.Open, observation.Snapshot);
        }

        var unchanged = previous.State != FindingState.Resolved &&
                        IsSameComparisonVersion(previous.Observation, candidate);
        if (unchanged)
        {
            var retained = new StoredFinding(
                findingId,
                previous.Identity,
                previous.State,
                observation,
                previous.LastEventId,
                previous.Snapshot);
            return new ReconciliationResult(retained, null);
        }

        var eventType = previous.State switch
        {
            FindingState.Open => FindingEventType.Updated,
            FindingState.Accepted or FindingState.FalsePositive or FindingState.Resolved => FindingEventType.Reopened,
            _ => throw new ArgumentOutOfRangeException(nameof(previous), "Unknown finding state."),
        };
        return CreateChange(previous, observation, runId, eventType, FindingState.Open, observation.Snapshot);
    }

    public ReconciliationResult Resolve(StoredFinding previous, string runId)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (previous.State == FindingState.Resolved)
        {
            return new ReconciliationResult(previous, null);
        }

        var change = new FindingTransition(
            EventId(runId, previous.FindingId),
            previous.LastEventId,
            FindingEventType.Resolved,
            previous.FindingId,
            previous.Identity,
            FindingState.Resolved,
            null,
            null);
        return new ReconciliationResult(
            previous with { State = FindingState.Resolved, LastEventId = change.EventId },
            change);
    }

    private static ReconciliationResult CreateChange(
        StoredFinding? previous,
        FindingObservation observation,
        string runId,
        FindingEventType eventType,
        FindingState state,
        string snapshot)
    {
        var change = new FindingTransition(
            EventId(runId, observation.FindingId),
            previous?.LastEventId,
            eventType,
            observation.FindingId,
            observation.Identity,
            state,
            observation,
            snapshot);
        return new ReconciliationResult(
            new StoredFinding(
                observation.FindingId,
                observation.Identity,
                state,
                observation,
                change.EventId,
                snapshot),
            change);
    }

    private static bool IsSameComparisonVersion(FindingObservation previous, FindingObservation current) =>
        previous.FingerprintVersion == current.FingerprintVersion &&
        StringComparer.Ordinal.Equals(previous.Fingerprint, current.Fingerprint) &&
        previous.BehaviorVersion == current.BehaviorVersion &&
        OptionsEqual(previous.EffectiveOptions, current.EffectiveOptions);

    private static bool OptionsEqual(RuleOptions left, RuleOptions right)
    {
        if (left.Values.Count != right.Values.Count)
        {
            return false;
        }

        return left.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Zip(right.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            .All(static pair => StringComparer.Ordinal.Equals(pair.First.Key, pair.Second.Key) &&
                                JsonEquals(pair.First.Value, pair.Second.Value));
    }

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectEquals(left, right),
            JsonValueKind.Array => JsonArrayEquals(left, right),
            JsonValueKind.String => StringComparer.Ordinal.Equals(left.GetString(), right.GetString()),
            JsonValueKind.Number => NumberEquals(left, right),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => false,
        };
    }

    private static bool JsonObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
        var rightProperties = right.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
        return leftProperties.Length == rightProperties.Length && leftProperties.Zip(rightProperties).All(pair =>
            StringComparer.Ordinal.Equals(pair.First.Name, pair.Second.Name) && JsonEquals(pair.First.Value, pair.Second.Value));
    }

    private static bool JsonArrayEquals(JsonElement left, JsonElement right)
    {
        var leftValues = left.EnumerateArray().ToArray();
        var rightValues = right.EnumerateArray().ToArray();
        return leftValues.Length == rightValues.Length &&
               leftValues.Zip(rightValues).All(static pair => JsonEquals(pair.First, pair.Second));
    }

    private static bool NumberEquals(JsonElement left, JsonElement right) =>
        left.TryGetDecimal(out var leftDecimal) && right.TryGetDecimal(out var rightDecimal)
            ? leftDecimal == rightDecimal
            : double.TryParse(left.GetRawText(), CultureInfo.InvariantCulture, out var leftDouble) &&
              double.TryParse(right.GetRawText(), CultureInfo.InvariantCulture, out var rightDouble) && leftDouble.Equals(rightDouble);

    private static string CreateFindingId() => "F-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string EventId(string runId, string findingId) => $"R:{runId}:{findingId}";

}

public enum FindingState
{
    Open,
    Accepted,
    FalsePositive,
    Resolved,
}

public enum FindingEventType
{
    New,
    Updated,
    Reopened,
    Resolved,
}

public sealed record FindingObservation(
    string FindingId,
    FindingIdentity Identity,
    int StartLine,
    int FingerprintVersion,
    string Fingerprint,
    int BehaviorVersion,
    RuleOptions EffectiveOptions,
    string Rationale,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<FindingEvidence> Evidence,
    IReadOnlyList<FindingFileHash> SourceFiles,
    string Snapshot);

public sealed record FindingFileHash(string Path, string Sha256);

public sealed record StoredFinding(
    string FindingId,
    FindingIdentity Identity,
    FindingState State,
    FindingObservation Observation,
    string? LastEventId,
    string Snapshot);

public sealed record FindingTransition(
    string EventId,
    string? PreviousEventId,
    FindingEventType EventType,
    string FindingId,
    FindingIdentity Identity,
    FindingState State,
    FindingObservation? Observation,
    string? Snapshot);

public sealed record ReconciliationResult(StoredFinding Finding, FindingTransition? Event);
