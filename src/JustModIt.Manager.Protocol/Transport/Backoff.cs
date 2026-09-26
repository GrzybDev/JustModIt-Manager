namespace JustModIt.Manager.Protocol.Transport;

/// <summary>Delays between retries that double after each failure.</summary>
/// <param name="initial">The delay after the first failure.</param>
/// <param name="maximum">The longest delay, which doubling stops at.</param>
internal sealed class Backoff(TimeSpan initial, TimeSpan maximum)
{
    private TimeSpan? _last;

    /// <summary>The delay before the next attempt.</summary>
    public TimeSpan Next()
    {
        var next = _last is { } last ? TimeSpan.FromTicks(Math.Min(last.Ticks * 2, maximum.Ticks)) : initial;
        _last = next;
        return next;
    }

    /// <summary>Starts over from the initial delay, e.g. after a success.</summary>
    public void Reset() => _last = null;
}
