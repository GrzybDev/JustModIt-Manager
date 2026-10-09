namespace JustModIt.Manager.Core;

/// <summary>Waits that a test's clock can drive.</summary>
public static class TimeProviderWaits
{
    /// <summary>Waits until a signal comes or a time passes, whichever is first.</summary>
    /// <param name="time">The clock the time passes on.</param>
    /// <param name="signal">Starts the wait for the signal; the token it gets is cancelled once the wait is over.</param>
    /// <param name="timeout">How long to wait at most, a negative time counting as none; null waits for the signal alone.</param>
    /// <param name="cancellationToken">Stops the wait, which then throws.</param>
    public static async Task WaitForSignalAsync(this TimeProvider time, Func<CancellationToken, Task> signal, TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(signal);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalled = signal(waiting.Token);
        await (timeout is { } limit
            ? Task.WhenAny(signalled, Task.Delay(limit > TimeSpan.Zero ? limit : TimeSpan.Zero, time, waiting.Token))
            : Task.WhenAny(signalled)).ConfigureAwait(false);
        // Neither the timer nor the wait for the signal outlives the wait.
        await waiting.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
