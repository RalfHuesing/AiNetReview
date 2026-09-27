namespace AiNetReview.Core.Storage;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Findings;

public interface IFindingStore
{
    Task<FindingStoreSnapshot> ReadAsync(string projectRoot, CancellationToken cancellationToken);

    Task CommitCompletedRunAsync(string projectRoot, FindingRunCommit run, CancellationToken cancellationToken);
}

public sealed record FindingStoreSnapshot(IReadOnlyDictionary<FindingIdentity, StoredFinding> Findings)
{
    public static FindingStoreSnapshot Empty { get; } = new(
        new ReadOnlyDictionary<FindingIdentity, StoredFinding>(new Dictionary<FindingIdentity, StoredFinding>()));
}

public sealed record FindingRunCommit(
    string RunId,
    IReadOnlyList<string> ActiveRuleIds,
    IReadOnlyList<FindingTransition> Events,
    IReadOnlyList<FindingObservation> Observations,
    IReadOnlyDictionary<FindingIdentity, StoredFinding> Findings);
