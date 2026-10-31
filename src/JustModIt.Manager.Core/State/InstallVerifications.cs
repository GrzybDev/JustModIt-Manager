using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.State;

/// <summary>
/// Decides which install's files are checked next, one at a time, and keeps what the checks found; it runs on the state
/// coordinator's loop, like the state it reads, so it needs no lock.
/// </summary>
internal sealed class InstallVerifications
{
    private readonly Dictionary<Guid, Verification> _found = [];
    private readonly HashSet<Guid> _requested = [];
    private Guid? _checking;

    /// <summary>What the last checks found to repair, by install.</summary>
    public ImmutableDictionary<Guid, RepairReason> Repairs() =>
        _found.Where(pair => pair.Value.Repair is not null).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Repair!.Value);

    /// <summary>What the last checks found missing from the installs' backups, by install.</summary>
    public ImmutableDictionary<Guid, ValueList<string>> MissingOriginals() =>
        _found.Where(pair => pair.Value.MissingOriginals.Count > 0).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.MissingOriginals);

    /// <summary>Whether the last check found originals of the install missing from its backups.</summary>
    /// <param name="record">The install.</param>
    public bool MissesOriginals(Guid record) => _found.TryGetValue(record, out var found) && found.MissingOriginals.Count > 0;

    /// <summary>Checks the installs at the next chance, however recently they were checked.</summary>
    /// <param name="records">The installs to check.</param>
    public void Request(IEnumerable<Guid> records) => _requested.UnionWith(records);

    /// <summary>Drops what was found for an install that is gone or was installed anew.</summary>
    /// <param name="record">The install.</param>
    public void Forget(Guid record) => _found.Remove(record);

    /// <summary>
    /// Drops what was found for the installs whose game the store works on, so they are checked again once it is done.
    /// </summary>
    /// <param name="records">Every install.</param>
    /// <param name="snapshot">The library as it now is.</param>
    public void OnLibraryChanged(IEnumerable<InstallRecord> records, LibrarySnapshot snapshot)
    {
        foreach (var record in records)
        {
            if (snapshot.Find(record.Game) is { IsReady: false } game && PathComparison.AreSame(game.InstallDirectory, record.InstallDirectory))
            {
                _found.Remove(record.Id);
            }
        }
    }

    /// <summary>The install to check now, taken as being checked; null while one is checked or when none is due.</summary>
    /// <param name="records">Every install.</param>
    /// <param name="snapshot">The library, which tells whether each game is ready.</param>
    /// <param name="hasJob">Tells whether a game has a job.</param>
    /// <param name="now">The current time.</param>
    public InstallRecord? TryStart(IEnumerable<InstallRecord> records, LibrarySnapshot snapshot, Func<GameKey, bool> hasJob,
        DateTimeOffset now)
    {
        if (_checking is not null)
        {
            return null;
        }

        var record = records.FirstOrDefault(record => DueAt(record, snapshot, hasJob, now) <= now);
        if (record is not null)
        {
            _requested.Remove(record.Id);
            _checking = record.Id;
        }

        return record;
    }

    /// <summary>Keeps what a check found once it ended.</summary>
    /// <param name="record">The install that was checked.</param>
    /// <param name="buildId">The game's build when the check started.</param>
    /// <param name="verification">What the check found; null when it failed.</param>
    /// <param name="isKnown">False when the install was forgotten meanwhile, so there is nothing to keep.</param>
    /// <param name="now">The current time.</param>
    public void OnChecked(Guid record, string? buildId, Verification? verification, bool isKnown, DateTimeOffset now)
    {
        if (_checking == record)
        {
            _checking = null;
        }

        if (isKnown)
        {
            // A check that failed keeps what the last one found, and waits as long as a successful one would.
            _found[record] = verification
                ?? (_found.TryGetValue(record, out var last) ? last with { At = now } : new Verification(now, buildId, null));
        }
    }

    /// <summary>When the next check falls due; null while one is checked or when none falls due without another change.</summary>
    /// <param name="records">Every install.</param>
    /// <param name="snapshot">The library, which tells whether each game is ready.</param>
    /// <param name="hasJob">Tells whether a game has a job.</param>
    /// <param name="now">The current time.</param>
    public DateTimeOffset? NextDueAt(IEnumerable<InstallRecord> records, LibrarySnapshot snapshot, Func<GameKey, bool> hasJob,
        DateTimeOffset now) =>
        _checking is not null ? null : records.Select(record => DueAt(record, snapshot, hasJob, now)).Min();

    private DateTimeOffset? DueAt(InstallRecord record, LibrarySnapshot snapshot, Func<GameKey, bool> hasJob, DateTimeOffset now) =>
        VerificationSchedule.DueAt(record, snapshot.Find(record.Game), hasJob(record.Game), _found.GetValueOrDefault(record.Id),
            _requested.Contains(record.Id), now);
}
