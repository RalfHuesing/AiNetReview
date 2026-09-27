namespace AiNetReview.FastTests.Findings;

using System.Collections.Generic;
using System.Text.Json;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Rules;

public sealed class StateMachineTests
{
    private const string RunId = "20260927T120000Z-a1b2c3d4";
    private readonly StateMachine stateMachine = new();

    [Fact]
    public void Reconcile_CreatesIndependentRandomIdsForDifferentKeys()
    {
        var first = stateMachine.Reconcile(null, Observation("first"), RunId);
        var second = stateMachine.Reconcile(null, Observation("second"), RunId);

        Assert.Matches("^F-[0-9a-f]{32}$", first.Finding.FindingId);
        Assert.Matches("^F-[0-9a-f]{32}$", second.Finding.FindingId);
        Assert.NotEqual(first.Finding.FindingId, second.Finding.FindingId);
        Assert.Equal(FindingEventType.New, first.Event!.EventType);
        Assert.Equal(FindingState.Open, first.Finding.State);
    }

    [Fact]
    public void Reconcile_OpenFindingIsUnchangedOrUpdatedWithoutChangingIdentity()
    {
        var created = stateMachine.Reconcile(null, Observation("case-a"), RunId).Finding;
        var same = stateMachine.Reconcile(created, Observation("case-a"), "20260927T120001Z-a1b2c3d4");
        var changed = stateMachine.Reconcile(same.Finding, Observation("case-a", fingerprint: "sha256:changed"), "20260927T120002Z-a1b2c3d4");

        Assert.Null(same.Event);
        Assert.Equal(created.FindingId, same.Finding.FindingId);
        Assert.Equal(FindingEventType.Updated, changed.Event!.EventType);
        Assert.Equal(created.FindingId, changed.Finding.FindingId);
        Assert.Equal(FindingState.Open, changed.Finding.State);
    }

    [Theory]
    [InlineData(FindingState.Accepted)]
    [InlineData(FindingState.FalsePositive)]
    public void Reconcile_KeptVerdictIsSuppressedUnlessComparisonVersionChanges(FindingState state)
    {
        var previous = Stored(Observation("case-a"), state);
        var same = stateMachine.Reconcile(previous, Observation("case-a"), RunId);
        var changed = stateMachine.Reconcile(previous, Observation("case-a", fingerprint: "sha256:changed"), RunId);

        Assert.Null(same.Event);
        Assert.Equal(state, same.Finding.State);
        Assert.Equal(FindingEventType.Reopened, changed.Event!.EventType);
        Assert.Equal(FindingState.Open, changed.Finding.State);
        Assert.Equal(previous.FindingId, changed.Finding.FindingId);
    }

    [Fact]
    public void Reconcile_ReopensResolvedFindingEvenWhenComparisonVersionMatches()
    {
        var previous = Stored(Observation("case-a"), FindingState.Resolved);

        var result = stateMachine.Reconcile(previous, Observation("case-a"), RunId);

        Assert.Equal(FindingEventType.Reopened, result.Event!.EventType);
        Assert.Equal(FindingState.Open, result.Finding.State);
        Assert.Equal(previous.FindingId, result.Finding.FindingId);
    }

    [Fact]
    public void Reconcile_UpdatesWhenBehaviorVersionOrEffectiveOptionsChangeWithoutFingerprintChange()
    {
        var previous = Stored(Observation("case-a"), FindingState.Open);
        var behaviorChanged = stateMachine.Reconcile(previous, Observation("case-a", behaviorVersion: 2), RunId);
        var optionChanged = stateMachine.Reconcile(previous, Observation("case-a", scenario: "variant"), RunId);

        Assert.Equal("sha256:initial", behaviorChanged.Event!.Observation!.Fingerprint);
        Assert.Equal(FindingEventType.Updated, behaviorChanged.Event.EventType);
        Assert.Equal(FindingEventType.Updated, optionChanged.Event!.EventType);
    }

    [Theory]
    [InlineData(FindingState.Open)]
    [InlineData(FindingState.Accepted)]
    [InlineData(FindingState.FalsePositive)]
    public void Resolve_MovesEveryActiveStateToResolved(FindingState previousState)
    {
        var result = stateMachine.Resolve(Stored(Observation("case-a"), previousState), RunId);

        Assert.Equal(FindingEventType.Resolved, result.Event!.EventType);
        Assert.Null(result.Event.Observation);
        Assert.Null(result.Event.Snapshot);
        Assert.Equal(FindingState.Resolved, result.Finding.State);
    }

    [Fact]
    public void Resolve_AlreadyResolvedFindingHasNoSecondEvent()
    {
        var previous = Stored(Observation("case-a"), FindingState.Resolved);

        var result = stateMachine.Resolve(previous, RunId);

        Assert.Null(result.Event);
        Assert.Same(previous, result.Finding);
    }

    private static FindingObservation Observation(
        string subjectId,
        string fingerprint = "sha256:initial",
        int behaviorVersion = 1,
        string scenario = "base")
    {
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(scenario));
        var options = new SortedDictionary<string, JsonElement>(System.StringComparer.Ordinal)
        {
            ["scenario"] = value.RootElement.Clone(),
        };
        return new FindingObservation(
            string.Empty,
            new FindingIdentity("fixture-finding", "src/Sample.csproj", "src/Sample.cs", subjectId, subjectId),
            1,
            1,
            fingerprint,
            behaviorVersion,
            new RuleOptions(options),
            "Review this fixture case.",
            new Dictionary<string, double>(),
            [],
            [],
            "void Fixture() { }");
    }

    private static StoredFinding Stored(FindingObservation observation, FindingState state)
    {
        const string findingId = "F-0123456789abcdef0123456789abcdef";
        return new StoredFinding(
            findingId,
            observation.Identity,
            state,
            observation with { FindingId = findingId },
            "R:20260927T110000Z-11111111:F-0123456789abcdef0123456789abcdef",
            "void Fixture() { }");
    }
}
