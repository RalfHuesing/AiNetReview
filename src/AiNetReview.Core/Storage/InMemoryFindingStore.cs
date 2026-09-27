namespace AiNetReview.Core.Storage;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Findings;

/// <summary>Process-local transition store for the initial runner slice; it cannot persist review decisions.</summary>
public sealed class InMemoryFindingStore : IFindingStore
{
    private readonly ConcurrentDictionary<string, FindingStoreSnapshot> snapshots = new(PathComparer);

    public Task<FindingStoreSnapshot> ReadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var root = System.IO.Path.GetFullPath(projectRoot);
        return Task.FromResult(snapshots.TryGetValue(root, out var snapshot) ? snapshot : FindingStoreSnapshot.Empty);
    }

    public Task CommitCompletedRunAsync(string projectRoot, FindingRunCommit run, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        var root = System.IO.Path.GetFullPath(projectRoot);
        var copied = new Dictionary<FindingIdentity, StoredFinding>(run.Findings);
        snapshots[root] = new FindingStoreSnapshot(new ReadOnlyDictionary<FindingIdentity, StoredFinding>(copied));
        return Task.CompletedTask;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
