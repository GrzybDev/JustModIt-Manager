using System.Threading.Channels;
using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent;

/// <summary>When the agent hands memory back to the system.</summary>
/// <param name="QuietPeriod">How long the agent must have had nothing to do before its memory goes back to the system.</param>
/// <param name="MinimumAllocation">How many bytes the work since the last time must have allocated to be worth a collection.</param>
/// <param name="FollowUp">How long after a collection one more follows while the agent stays quiet.</param>
public sealed record MemoryTrimSchedule(TimeSpan QuietPeriod, long MinimumAllocation, TimeSpan FollowUp)
{
    // The quiet period outlasts the site's idle connections, so what they held is garbage by then. The thread pool lets
    // its idle threads go only minutes later, and the pooled buffers they held become garbage then.
    public static MemoryTrimSchedule Default { get; } =
        new(CatalogueHttp.IdleConnectionTimeout * 2, 1024 * 1024, TimeSpan.FromMinutes(3));
}

public interface IManagedHeap
{
    /// <summary>Everything allocated since the process started, garbage included.</summary>
    long AllocatedBytes { get; }

    /// <summary>Collects and compacts every generation and hands the freed memory back to the system.</summary>
    /// <returns>The bytes still in use.</returns>
    long Compact();
}

public sealed class ManagedHeap : IManagedHeap
{
    private ManagedHeap()
    {
    }

    public static ManagedHeap Instance { get; } = new();

    public long AllocatedBytes => GC.GetTotalAllocatedBytes();

    public long Compact()
    {
        // An aggressive collection also decommits the free regions, which an ordinary one keeps for the allocations to come.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        return GC.GetGCMemoryInfo().HeapSizeBytes;
    }
}

/// <summary>
/// Hands the memory that work left behind back to the system once the agent has been quiet for a while with no window
/// open, since the collector waits for a budget of tens of megabytes that an idle agent never fills.
/// </summary>
/// <param name="publisher">Announces state changes and job progress, which count as work.</param>
/// <param name="ui">Tells whether a window is open, and when it closes.</param>
/// <param name="heap">The heap to compact.</param>
/// <param name="schedule">How quiet the agent must be, and for how long.</param>
/// <param name="time">Times the quiet periods.</param>
/// <param name="logger">Receives each compaction.</param>
public sealed partial class MemoryTrimmer(
    StatePublisher publisher,
    UiLauncher ui,
    IManagedHeap heap,
    MemoryTrimSchedule schedule,
    TimeProvider time,
    ILogger<MemoryTrimmer> logger) : IAgentService
{
    private readonly Channel<bool> _activity = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        publisher.Published += OnActivity;
        publisher.JobsProgressed += OnActivity;
        ui.Closed += OnActivity;

        // Starting is work too, and what it announced before this subscribed went unheard.
        _activity.Writer.TryWrite(true);
        try
        {
            var compactedAt = 0L;
            var followUpDue = false;
            while (true)
            {
                if (followUpDue)
                {
                    if (await StaysQuietAsync(schedule.FollowUp, cancellationToken).ConfigureAwait(false))
                    {
                        compactedAt = Compact();
                        followUpDue = false;
                        continue;
                    }
                }
                else
                {
                    await _activity.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }

                await WaitUntilQuietAsync(cancellationToken).ConfigureAwait(false);
                if (heap.AllocatedBytes - compactedAt >= schedule.MinimumAllocation)
                {
                    compactedAt = Compact();
                    followUpDue = true;
                }
            }
        }
        finally
        {
            publisher.Published -= OnActivity;
            publisher.JobsProgressed -= OnActivity;
            ui.Closed -= OnActivity;
        }
    }

    // An open window asks for pages of the catalogue that no state change announces, so its whole visit counts as work.
    // Its closing is announced, so it is waited out without a timer; a window the agent closes itself is followed by an
    // exit or a state change.
    private async Task WaitUntilQuietAsync(CancellationToken cancellationToken)
    {
        do
        {
            while (ui.Window is not null)
            {
                await _activity.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(schedule.QuietPeriod, time, cancellationToken).ConfigureAwait(false);
        }
        while (_activity.Reader.TryRead(out _) || ui.IsOpenOrStarting);
    }

    // False as soon as work comes, or when a window is open at the end.
    private async Task<bool> StaysQuietAsync(TimeSpan period, CancellationToken cancellationToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Task.WhenAny(Task.Delay(period, time, waiting.Token), _activity.Reader.WaitToReadAsync(waiting.Token).AsTask())
            .ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return !_activity.Reader.TryRead(out _) && !ui.IsOpenOrStarting;
    }

    // Returns the bytes allocated so far, which the next collection is weighed against.
    private long Compact()
    {
        var allocated = heap.AllocatedBytes;
        var started = time.GetTimestamp();
        var inUse = heap.Compact();
        LogCompacted(inUse, Math.Round(time.GetElapsedTime(started).TotalMilliseconds, 1));
        return allocated;
    }

    private void OnActivity<T>(object? sender, T args) => _activity.Writer.TryWrite(true);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Compacted the heap to {InUse} bytes in {Milliseconds} ms")]
    private partial void LogCompacted(long inUse, double milliseconds);
}
