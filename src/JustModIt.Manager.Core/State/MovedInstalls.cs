using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.State;

/// <summary>
/// Decides when an install follows its game to the folder the store moved it to, and when an install whose game is gone
/// is forgotten; it runs on the state coordinator's loop, like the state it reads, so it needs no lock.
/// </summary>
internal sealed class MovedInstalls
{
    private readonly HashSet<(Guid Record, string Folder)> _followAttempts = [];
    private readonly HashSet<Guid> _deferredFollows = [];
    private readonly HashSet<Guid> _followsInVain = [];
    private readonly HashSet<Guid> _goneChecks = [];
    private GameKey? _forgetting;

    /// <summary>
    /// The install of the game to follow to the folder the store moved the game to, tried once per folder; null when there
    /// is none.
    /// </summary>
    /// <param name="game">A game without a job that is not running.</param>
    public (InstallRecord Record, string Folder)? NextFollow(MyGame game) =>
        game.Game is { IsReady: true } moved
        && game.Installs.FirstOrDefault(install => install.Status.Orphan == OrphanReason.GameMoved
            && !_followAttempts.Contains((install.Record.Id, moved.InstallDirectory))) is { Record: var record }
            ? (record, moved.InstallDirectory)
            : null;

    /// <summary>Notes that the install started to follow its game.</summary>
    /// <param name="record">The install.</param>
    /// <param name="folder">The folder the game was moved to.</param>
    public void OnFollowStarted(InstallRecord record, string folder)
    {
        _followAttempts.Add((record.Id, folder));
        _followsInVain.Remove(record.Id);
    }

    /// <summary>Learns how following a moved game ended.</summary>
    /// <param name="record">The install that followed its game.</param>
    /// <param name="result">How the job ended.</param>
    public void OnFollowEnded(Guid record, JobResult result)
    {
        if (result.Outcome == JobOutcome.Deferred)
        {
            _deferredFollows.Add(record);
        }
        else if (result is { IsSuccess: true, Installed: null })
        {
            _followsInVain.Add(record);
        }
    }

    /// <summary>A follow that found its game in use is tried again, since the game exited or the store changed something.</summary>
    public void RetryDeferred()
    {
        _followAttempts.RemoveWhere(attempt => _deferredFollows.Contains(attempt.Record));
        _deferredFollows.Clear();
    }

    /// <summary>Retries deferred follows and looks at every install once more for whether its game is gone.</summary>
    public void OnLibraryChanged()
    {
        RetryDeferred();
        _goneChecks.Clear();
    }

    /// <summary>Looks at the installs once more for whether their game is gone, e.g. because the player opened it.</summary>
    /// <param name="records">The installs.</param>
    public void Recheck(IEnumerable<Guid> records) => _goneChecks.ExceptWith(records);

    /// <summary>
    /// The installs of one game whose game is surely gone from the folder, taken as being checked; null while one game is
    /// checked or when none is gone. Surely gone means: a fresh scan that read the whole store reports the game nowhere or
    /// elsewhere, nothing works on it, a moved game was looked for the install's files in vain, and each install is looked
    /// at once per library change. A game's installs go together, so the notices name their projects alike.
    /// </summary>
    /// <param name="records">Every install.</param>
    /// <param name="snapshot">The library as it now is.</param>
    /// <param name="isBusy">Tells whether a game has a job or is running.</param>
    public IReadOnlyList<InstallRecord>? NextGone(IEnumerable<InstallRecord> records, LibrarySnapshot snapshot,
        Func<GameKey, bool> isBusy)
    {
        if (_forgetting is not null)
        {
            return null;
        }

        var gone = records.Where(record => !_goneChecks.Contains(record.Id) && !isBusy(record.Game)
                && snapshot.ConfirmsGone(record.Game, record.InstallDirectory)
                && (snapshot.Find(record.Game) is not { } moved
                    || (_followAttempts.Contains((record.Id, moved.InstallDirectory)) && _followsInVain.Contains(record.Id))))
            .GroupBy(record => record.Game)
            .FirstOrDefault();
        if (gone is null)
        {
            return null;
        }

        List<InstallRecord> taken = [.. gone];
        _goneChecks.UnionWith(taken.Select(record => record.Id));
        _forgetting = gone.Key;
        return taken;
    }

    /// <summary>Lets the next game's installs be checked once the engine looked at the last ones.</summary>
    public void OnGoneChecked() => _forgetting = null;
}
