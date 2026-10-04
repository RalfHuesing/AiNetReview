namespace AiNetReview.Core.Analysis;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Builds one immutable artifact per review context and only retains successful results.</summary>
internal sealed class RunScopedArtifact<T>
    where T : class
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private T? value;

    public async Task<T> GetAsync(Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value is not null)
            {
                return value;
            }

            var created = await factory(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            value = created ?? throw new InvalidOperationException("An analysis artifact factory returned null.");
            return value;
        }
        finally
        {
            gate.Release();
        }
    }
}
