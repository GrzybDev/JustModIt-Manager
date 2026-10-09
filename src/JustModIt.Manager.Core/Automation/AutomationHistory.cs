using System.Collections.Immutable;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Core.Automation;

public readonly record struct AttemptKey(GameKey Game, Guid Release, AutomationAction Action);

/// <summary>What the history keeps of the last try of an action on a release.</summary>
/// <param name="At">When the action was last started.</param>
/// <param name="Failures">How many tries in a row failed.</param>
/// <param name="Reason">Why the last try failed, if it did.</param>
/// <param name="Build">The game's build the last try failed on.</param>
public sealed record AttemptEntry(DateTimeOffset At, int Failures, JobFailureReason? Reason = null, string? Build = null);

public readonly record struct Announcement(GameKey Game, Guid Release);

/// <summary>A repair the player was told of instead of the app making it.</summary>
/// <param name="Install">
/// The install that needs the repair; a repair or an update installs anew, so what its successor needs is news again.
/// </param>
/// <param name="Build">The game's build that broke the install; null when its store names none.</param>
public readonly record struct RepairAnnouncement(Guid Install, string? Build);

/// <summary>
/// What the automation already did, so it never loops and tells each failure, release and needed repair once.
/// </summary>
public sealed class AutomationHistory
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(6);

    /// <summary>Attempts only matter while they can still hold a retry back; older ones are dropped.</summary>
    public static readonly TimeSpan KeepAttempts = TimeSpan.FromDays(30);

    /// <summary>A time further ahead than this comes from a clock that was wrong; it restarts the wait instead of holding it.</summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromHours(1);

    private readonly ImmutableDictionary<AttemptKey, AttemptEntry> _attempts;
    private readonly ImmutableHashSet<Announcement> _announcements;
    private readonly ImmutableHashSet<RepairAnnouncement> _repairAnnouncements;

    /// <summary>Creates a history as it was saved; every change then returns a new one.</summary>
    /// <param name="attempts">The last try of each action on a release.</param>
    /// <param name="announcements">The releases announced for each game.</param>
    /// <param name="repairAnnouncements">The repairs the player was told of.</param>
    /// <param name="baselineAt">When the automation first looked after the first-run setup; null before it did.</param>
    public AutomationHistory(IEnumerable<KeyValuePair<AttemptKey, AttemptEntry>> attempts, IEnumerable<Announcement> announcements,
        IEnumerable<RepairAnnouncement> repairAnnouncements, DateTimeOffset? baselineAt)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(announcements);
        ArgumentNullException.ThrowIfNull(repairAnnouncements);
        _attempts = attempts.ToImmutableDictionary();
        _announcements = [.. announcements];
        _repairAnnouncements = [.. repairAnnouncements];
        BaselineAt = baselineAt;
    }

    private AutomationHistory(ImmutableDictionary<AttemptKey, AttemptEntry> attempts, ImmutableHashSet<Announcement> announcements,
        ImmutableHashSet<RepairAnnouncement> repairAnnouncements, DateTimeOffset? baselineAt)
    {
        _attempts = attempts;
        _announcements = announcements;
        _repairAnnouncements = repairAnnouncements;
        BaselineAt = baselineAt;
    }

    public static AutomationHistory Empty { get; } = new([], [], [], null);

    public IReadOnlyDictionary<AttemptKey, AttemptEntry> Attempts => _attempts;

    public IReadOnlySet<Announcement> Announcements => _announcements;

    public IReadOnlySet<RepairAnnouncement> RepairAnnouncements => _repairAnnouncements;

    /// <summary>
    /// When the automation first looked at the player's games after the first-run setup, announcing everything on offer
    /// then as one summary rather than game by game.
    /// </summary>
    public DateTimeOffset? BaselineAt { get; }

    /// <summary>
    /// Whether the action on the release may be tried now: at most once per <see cref="RetryAfter"/>, not on a build its
    /// package did not fit, and never once the package turned out to be made for another game.
    /// </summary>
    /// <param name="game">The game the action is for.</param>
    /// <param name="release">The release the history tracks the action by.</param>
    /// <param name="action">The job the automation would start.</param>
    /// <param name="build">The game's current build.</param>
    /// <param name="now">The current time.</param>
    public bool CanTry(GameKey game, Guid release, AutomationAction action, string? build, DateTimeOffset now)
    {
        if (!_attempts.TryGetValue(new AttemptKey(game, release, action), out var attempt))
        {
            return true;
        }

        // Each try would download the whole package again only to find it still does not fit the same game files.
        var madeForAnotherBuild = attempt.Reason is JobFailureReason.UnsupportedGameVersion or JobFailureReason.NewReleaseIncompatible;
        return attempt.Reason != JobFailureReason.OtherGame && !(madeForAnotherBuild && attempt.Build == build)
            && now - attempt.At >= RetryAfter;
    }

    /// <summary>Records that the action started, keeping the count of failures in a row.</summary>
    /// <param name="game">The game the action is for.</param>
    /// <param name="release">The release the history tracks the action by.</param>
    /// <param name="action">The job the automation started.</param>
    /// <param name="now">When it started.</param>
    public AutomationHistory RecordAttempt(GameKey game, Guid release, AutomationAction action, DateTimeOffset now)
    {
        var key = new AttemptKey(game, release, action);
        var failures = _attempts.TryGetValue(key, out var previous) ? previous.Failures : 0;
        return With(_attempts.SetItem(key, new AttemptEntry(now, failures)));
    }

    /// <summary>Records that a try of the action failed.</summary>
    /// <param name="game">The game the action is for.</param>
    /// <param name="release">The release the history tracks the action by.</param>
    /// <param name="action">The job that failed.</param>
    /// <param name="reason">Why it failed, which may hold retries back on the same build or for good.</param>
    /// <param name="build">The game's build the try failed on.</param>
    /// <param name="now">When it failed, taken as its start when none was recorded.</param>
    /// <param name="failures">How many tries in a row failed, this one included.</param>
    public AutomationHistory RecordFailure(GameKey game, Guid release, AutomationAction action, JobFailureReason reason, string? build,
        DateTimeOffset now, out int failures)
    {
        var key = new AttemptKey(game, release, action);
        var previous = _attempts.GetValueOrDefault(key);
        failures = (previous?.Failures ?? 0) + 1;
        // The wait counts from when the try started.
        return With(_attempts.SetItem(key, new AttemptEntry(previous?.At ?? now, failures, reason, build)));
    }

    /// <summary>Records a success, which holds the same action back for a while too, so a repair that keeps being needed cannot loop.</summary>
    /// <param name="game">The game the action is for.</param>
    /// <param name="release">The release the history tracks the action by.</param>
    /// <param name="action">The job that succeeded.</param>
    /// <param name="now">When it succeeded.</param>
    public AutomationHistory RecordSuccess(GameKey game, Guid release, AutomationAction action, DateTimeOffset now) =>
        With(_attempts.SetItem(new AttemptKey(game, release, action), new AttemptEntry(now, 0)));

    /// <summary>Drops an attempt that never ran, e.g. because the game was in use or the app exited.</summary>
    /// <param name="game">The game the action is for.</param>
    /// <param name="release">The release the history tracks the action by.</param>
    /// <param name="action">The job that did not run.</param>
    public AutomationHistory ForgetAttempt(GameKey game, Guid release, AutomationAction action) =>
        With(_attempts.Remove(new AttemptKey(game, release, action)));

    /// <summary>Lets the automation act on the game again at once, since the player acted on it.</summary>
    /// <param name="game">The game the player acted on.</param>
    public AutomationHistory Reset(GameKey game) => With(_attempts.RemoveRange(_attempts.Keys.Where(key => key.Game == game)));

    /// <summary>Whether the release was announced for the game.</summary>
    /// <param name="game">The game the release is for.</param>
    /// <param name="release">The release's id.</param>
    public bool WasAnnounced(GameKey game, Guid release) => _announcements.Contains(new Announcement(game, release));

    /// <summary>Records that the release was announced for the game.</summary>
    /// <param name="game">The game the release is for.</param>
    /// <param name="release">The release's id.</param>
    public AutomationHistory RecordAnnouncement(GameKey game, Guid release)
    {
        var announcements = _announcements.Add(new Announcement(game, release));
        return announcements == _announcements ? this : new AutomationHistory(_attempts, announcements, _repairAnnouncements, BaselineAt);
    }

    /// <summary>Whether the player was told that the install needs a repair on this build.</summary>
    /// <param name="install">The install's record id.</param>
    /// <param name="build">The game's build that broke the install.</param>
    public bool WasRepairAnnounced(Guid install, string? build) => _repairAnnouncements.Contains(new RepairAnnouncement(install, build));

    /// <summary>Records that the player was told that the install needs a repair on this build.</summary>
    /// <param name="install">The install's record id.</param>
    /// <param name="build">The game's build that broke the install; null when its store names none.</param>
    public AutomationHistory RecordRepairAnnouncement(Guid install, string? build) =>
        WithRepairs(_repairAnnouncements.Add(new RepairAnnouncement(install, build)));

    /// <summary>Drops what was announced of installs that are gone, which nothing can ask about again.</summary>
    /// <param name="installs">The record ids of the installs that exist.</param>
    public AutomationHistory KeepRepairAnnouncementsOf(IReadOnlySet<Guid> installs)
    {
        ArgumentNullException.ThrowIfNull(installs);
        return WithRepairs(_repairAnnouncements.Except(
            _repairAnnouncements.Where(announcement => !installs.Contains(announcement.Install))));
    }

    /// <summary>The history with the time of the first look after the first-run setup.</summary>
    /// <param name="at">When that look ran.</param>
    public AutomationHistory WithBaseline(DateTimeOffset at) => new(_attempts, _announcements, _repairAnnouncements, at);

    /// <summary>
    /// Drops attempts too old to hold anything back, and restarts the wait of attempts dated ahead by a wrong clock.
    /// </summary>
    /// <param name="now">The current time.</param>
    public AutomationHistory Tidy(DateTimeOffset now) =>
        With(_attempts
            .Where(pair => now - pair.Value.At < KeepAttempts)
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value.At - now > ClockSkewTolerance ? pair.Value with { At = now } : pair.Value));

    private AutomationHistory With(ImmutableDictionary<AttemptKey, AttemptEntry> attempts) =>
        attempts == _attempts ? this : new AutomationHistory(attempts, _announcements, _repairAnnouncements, BaselineAt);

    private AutomationHistory WithRepairs(ImmutableHashSet<RepairAnnouncement> repairAnnouncements) =>
        repairAnnouncements == _repairAnnouncements ? this : new AutomationHistory(_attempts, _announcements, repairAnnouncements, BaselineAt);
}
