using JustModIt.Manager.Core.Library;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>
/// A fake clock that counts the timers created on it, so a test can tell when a background loop has gone back to
/// sleep before it moves the clock, and the drive time limits running, so it can tell which library reads are under way.
/// </summary>
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    private int _timers;
    private int _driveLimits;

    public CountingTimeProvider()
    {
    }

    public CountingTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime)
    {
    }

    public int TimersCreated => Volatile.Read(ref _timers);

    /// <summary>
    /// Timers as long as a drive's time limit that have neither run out nor been stopped: one per library read under way,
    /// and any wait that lasts as long.
    /// </summary>
    public int DriveLimits => Volatile.Read(ref _driveLimits);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // A time limit on a drive that may not answer does not mean the loop sleeps.
        if (dueTime == LibraryAccess.TimeLimit)
        {
            Interlocked.Increment(ref _driveLimits);
            var limit = new DriveLimit(() => Interlocked.Decrement(ref _driveLimits));
            limit.Timer = base.CreateTimer(running =>
            {
                limit.End();
                callback(running);
            }, state, dueTime, period);
            return limit;
        }

        // Counted only once it exists: a test that sees the count moves the clock, and a timer made after that would
        // count its due time from the moved clock.
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timers);
        return timer;
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

    // Ends once: when it runs out, or when the read answers in time and stops it.
    private sealed class DriveLimit(Action ended) : ITimer
    {
        private int _ended;

        public ITimer Timer { get; set; } = null!;

        public void End()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                ended();
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => Timer.Change(dueTime, period);

        public void Dispose()
        {
            End();
            Timer.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            End();
            return Timer.DisposeAsync();
        }
    }
}
