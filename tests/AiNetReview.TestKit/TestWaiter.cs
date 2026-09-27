namespace AiNetReview.TestKit;

using System;
using System.Threading.Tasks;

/// <summary>
/// Asynchroner Polling-Helfer zur Überprüfung von Zuständen mit Timeout.
/// </summary>
public static class TestWaiter
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(20);

    public static async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var interval = pollInterval ?? DefaultPollInterval;
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(interval).ConfigureAwait(false);
        }

        if (!condition())
        {
            throw new TimeoutException($"Bedingung wurde nicht innerhalb von {timeout.TotalSeconds:F1}s erfüllt.");
        }
    }

    public static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var interval = pollInterval ?? DefaultPollInterval;
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false)) return;
            await Task.Delay(interval).ConfigureAwait(false);
        }

        if (!await condition().ConfigureAwait(false))
        {
            throw new TimeoutException($"Asynchrone Bedingung wurde nicht innerhalb von {timeout.TotalSeconds:F1}s erfüllt.");
        }
    }
}
