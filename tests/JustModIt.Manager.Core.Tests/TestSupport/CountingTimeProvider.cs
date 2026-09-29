using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>
/// A fake clock that counts the timers created on it, so a test can tell when a background loop has gone back to
/// sleep before it moves the clock.
/// </summary>
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    private int _timers;

    public CountingTimeProvider()
    {
    }

    public CountingTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime)
    {
    }

    public int TimersCreated => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        return base.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>Runs <paramref name="action"/> and waits until the loop it wakes has started a new timer.</summary>
    /// <param name="action">What wakes the loop, such as moving the clock.</param>
    /// <param name="because">What the test waits for, as the timeout's message says it.</param>
    public async Task WakeAsync(Action action, string because)
    {
        var before = TimersCreated;
        action();
        await Eventually.HoldsAsync(() => TimersCreated > before, because);
    }

    /// <summary>Moves the clock past a timer the loop sleeps on and waits until the loop sleeps again.</summary>
    /// <param name="by">How far the clock moves, at least to the timer's due time.</param>
    public Task AdvanceAndWaitAsync(TimeSpan by) => WakeAsync(() => Advance(by), $"the loop sleeps again after {by}");
}
