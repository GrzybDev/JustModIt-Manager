using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Core.Jobs;

internal enum GateStatus
{
    Ready,
    Wait,
    Fail,
}

/// <summary>Whether a job may change its game now.</summary>
/// <param name="Status">Go ahead, wait or fail.</param>
/// <param name="WaitReason">What the job waits for, while it must wait.</param>
/// <param name="Failure">Why the job fails, when it must.</param>
/// <param name="WaitUntil">For <see cref="JobWaitReason.QuietPeriod"/>: when the store's last touch is long enough ago.</param>
/// <param name="ClockSkewIgnored">The store's last touch lies far in the future, so the quiet period was skipped.</param>
internal sealed record GateVerdict(GateStatus Status, JobWaitReason? WaitReason = null, JobFailure? Failure = null,
    DateTimeOffset? WaitUntil = null, bool ClockSkewIgnored = false)
{
    public static GateVerdict Ready { get; } = new(GateStatus.Ready);

    public ValueList<RunningProcess> Processes { get; init; }

    /// <summary>A verdict to wait.</summary>
    /// <param name="reason">What the job waits for.</param>
    public static GateVerdict Waiting(JobWaitReason reason) => new(GateStatus.Wait, reason);

    /// <summary>A verdict to fail.</summary>
    /// <param name="failure">Why the job fails.</param>
    public static GateVerdict Failing(JobFailure failure) => new(GateStatus.Fail, Failure: failure);
}

/// <summary>
/// Whether a job may change a game right now, from a fresh look at it: anything that is not clearly "installed and idle"
/// either waits for the store or the game to finish or fails when only the player can help.
/// </summary>
internal static class SafetyGate
{
    /// <summary>
    /// Stores rewrite their records throughout a job and may still be finishing one that looks done, so a game the store
    /// touched this recently is left alone a little longer.
    /// </summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A touch further in the future than this comes from a wrong clock, e.g. files from another machine, and would
    /// otherwise hold the job until the wall clock caught up.
    /// </summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Decides whether a job may change the game now.</summary>
    /// <param name="probe">The game as its store records and running processes show it right now.</param>
    /// <param name="kind">What the job does; an uninstall may also run while the store waits to update or repair the game.</param>
    /// <param name="now">The current time, against which the store's last touch is measured.</param>
    public static GateVerdict Check(GameProbe probe, JobKind kind, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (probe.Game is not { } game)
        {
            return GateVerdict.Failing(new JobFailure(JobFailureReason.GameNotDetected));
        }

        if (IsStoreBusy(game.State, game.Reason) || game.IsStale)
        {
            return GateVerdict.Waiting(JobWaitReason.StoreBusy);
        }

        // A pending update may wait for days, and a broken or unplugged game needs the player, so waiting would not help.
        if (!probe.AllowsChange(forUninstall: kind == JobKind.Uninstall))
        {
            return GateVerdict.Failing(new JobFailure(JobFailureReason.GameNotReady) { GameState = game.State, StateReason = game.Reason });
        }

        if (probe.RunningProcesses.Count > 0)
        {
            return GateVerdict.Waiting(JobWaitReason.GameRunning) with { Processes = probe.RunningProcesses };
        }

        if (QuietUntil(game.StoreActivityAt, now) is { } until)
        {
            return GateVerdict.Waiting(JobWaitReason.QuietPeriod) with { WaitUntil = until };
        }

        return game.StoreActivityAt - now > ClockSkewTolerance ? GateVerdict.Ready with { ClockSkewIgnored = true } : GateVerdict.Ready;
    }

    /// <summary>When the quiet period after the store's last touch of a game ends.</summary>
    /// <param name="storeActivityAt">When the store last touched the game; null when it is not known.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Null when the period is over or the touch comes from a wrong clock.</returns>
    internal static DateTimeOffset? QuietUntil(DateTimeOffset? storeActivityAt, DateTimeOffset now) =>
        storeActivityAt is { } touched && touched - now <= ClockSkewTolerance && now - touched < QuietPeriod
            ? touched + QuietPeriod
            : null;

    /// <summary>
    /// Whether the engine refused only because the game started, or the store began working on it, after the job's own
    /// look: a wait fixes that.
    /// </summary>
    /// <param name="issues">Every problem the engine found.</param>
    public static bool IsOnlyInUse(IReadOnlyList<InstallIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return issues.Count > 0 && issues.All(issue => issue is GameRunningIssue
            || (issue is GameNotReadyIssue notReady && (IsStoreBusy(notReady.State, notReady.Reason)
                || notReady.State == GameInstallState.Running)));
    }

    // A record caught while the store rewrote it reads as unknown; the next look usually reads it whole.
    private static bool IsStoreBusy(GameInstallState state, GameStateReason reason) =>
        state is GameInstallState.Updating or GameInstallState.Validating or GameInstallState.Installing
        || (state == GameInstallState.Unknown && reason == GameStateReason.UnreadableStoreData);
}
