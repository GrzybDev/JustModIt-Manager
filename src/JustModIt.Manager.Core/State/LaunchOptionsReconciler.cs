using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Steam;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.State;

/// <summary>What one install's record keeps of the launch options after a change of its game's options.</summary>
/// <param name="Record">The install's record as the change was planned.</param>
/// <param name="Adopted">Options the app added for installs that are gone and this one needs, so they stay the app's to take back.</param>
/// <param name="Adding">The options added to the player's own for this install.</param>
/// <param name="Found">Options this install needs that the player had already, which stay the player's.</param>
internal sealed record LaunchOptionsRecordChange(InstallRecord Record, ValueList<string> Adopted, ValueList<string> Adding,
    ValueList<string> Found)
{
    /// <summary>What the record keeps once the change was made.</summary>
    /// <param name="after">The player's options after the change.</param>
    public InstalledLaunchOptions Kept(IReadOnlySet<string> after)
    {
        var options = Record.LaunchOptions!;
        ValueList<string> added = [.. options.Added.Union(Adopted).Union(Adding.Where(after.Contains))];
        return options with { Added = added, Present = [.. options.Present.Union(Found).Except(added)] };
    }
}

/// <summary>A change of one game's launch options in Steam, and of what its installs' records keep of them.</summary>
/// <param name="Game">The Steam game.</param>
/// <param name="Add">The options to add to the player's own.</param>
/// <param name="Remove">The options to take out of them.</param>
/// <param name="Records">The installs whose records keep more once the change was made.</param>
/// <param name="TakenBack">The options waiting to be taken back that the change deals with.</param>
internal sealed record LaunchOptionsChange(GameKey Game, ValueList<string> Add, ValueList<string> Remove,
    ValueList<LaunchOptionsRecordChange> Records, ValueList<string> TakenBack);

/// <summary>
/// Keeps the player's launch options in Steam in step with what the installs need, while Steam's interface can be reached:
/// it adds what a project needs, never again what the player took out unless asked, and takes back what the app added for
/// installs that are gone. It runs on the state coordinator's loop, which makes its changes in the background one at a time.
/// </summary>
/// <param name="store">Where the options still to take back are kept.</param>
/// <param name="logger">Logs that they could not be saved.</param>
internal sealed class LaunchOptionsReconciler(IDocumentStore<LaunchOptionsRemovals> store, ILogger logger) : IAsyncDisposable
{
    private readonly LatestDocumentWriter<LaunchOptionsRemovals> _writer = new(store, "launch options to take back", logger);
    private readonly HashSet<GameKey> _failed = [];
    private readonly HashSet<Guid> _reapply = [];
    private LaunchOptionsChange? _running;

    /// <summary>The options the app added for installs that are gone, which it still has to take out, by game.</summary>
    public ValueDictionary<GameKey, ValueList<string>> Removals { get; private set; }

    /// <summary>Starts from the options left to take back at the last run.</summary>
    /// <param name="saved">What was saved; null when nothing was.</param>
    public void Load(LaunchOptionsRemovals? saved) => Removals = saved?.Games ?? default;

    /// <summary>Keeps options the app added for an install that is gone until it took them out of the player's own.</summary>
    /// <param name="game">The install's game.</param>
    /// <param name="options">The options the app added for it and no other install keeps.</param>
    public void TakeBack(GameKey game, IEnumerable<string> options)
    {
        var waiting = Removals.GetValueOrDefault(game);
        ValueList<string> more = [.. waiting.Union(options, StringComparer.Ordinal)];
        if (more.Count > waiting.Count)
        {
            Save(Removals.SetItem(game, more));
        }
    }

    /// <summary>Tries again what failed, since what kept it from working may have changed with Steam's interface.</summary>
    public void OnStatusChanged() => _failed.Clear();

    /// <summary>Adds again, at the player's request, the options of an install that the player took out.</summary>
    /// <param name="record">The install's record id.</param>
    public void Reapply(Guid record) => _reapply.Add(record);

    /// <summary>The next change to make; null while one is being made, or while nothing is to change or can be.</summary>
    /// <param name="records">The installs.</param>
    /// <param name="steam">What the app knows of Steam's interface and of the player's options there.</param>
    /// <param name="isBusy">Whether a job works on a game, which leaves its options alone until it ends.</param>
    public LaunchOptionsChange? TryStart(IEnumerable<InstallRecord> records, SteamUiStatus steam, Func<GameKey, bool> isBusy)
    {
        if (_running is not null || !steam.Reachable)
        {
            return null;
        }

        var managed = records.Where(record => record.LaunchOptions is not null).GroupBy(record => record.Game)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (var game in managed.Keys.Union(Removals.Keys))
        {
            if (isBusy(game) || _failed.Contains(game) || steam.LaunchOptionsOf(game) is not { } current)
            {
                continue;
            }

            var installs = managed.GetValueOrDefault(game) ?? [];
            if (Plan(game, installs, Removals.GetValueOrDefault(game), current) is { } change)
            {
                return _running = change;
            }

            _reapply.ExceptWith(installs.Select(record => record.Id));
        }

        return null;
    }

    /// <summary>Takes in how the change ended.</summary>
    /// <param name="change">The change.</param>
    /// <param name="succeeded">Steam took the change.</param>
    /// <param name="unowned">Options the change added for installs whose records could not keep them, now the app's to take back.</param>
    /// <param name="recordsKept">Every record that is still there kept what the change added for it.</param>
    public void OnChanged(LaunchOptionsChange change, bool succeeded, IEnumerable<string> unowned, bool recordsKept)
    {
        _running = null;
        if (!succeeded)
        {
            // Trying again at once would only fail again; a change of Steam's interface is worth another try.
            _failed.Add(change.Game);
            return;
        }

        _reapply.ExceptWith(change.Records.Select(record => record.Record.Id));
        ValueList<string> left = [.. Removals.GetValueOrDefault(change.Game).Except(change.TakenBack, StringComparer.Ordinal)
            .Union(unowned, StringComparer.Ordinal)];
        Save(left.Count == 0 ? Removals.Remove(change.Game) : Removals.SetItem(change.Game, left));
        // A record that could not be written, as while its install waits for recovery, would fail again at once.
        if (!recordsKept)
        {
            _failed.Add(change.Game);
        }
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    private LaunchOptionsChange? Plan(GameKey game, List<InstallRecord> installs, ValueList<string> waiting, IReadOnlySet<string> current)
    {
        var owned = installs.SelectMany(record => record.LaunchOptions!.Added).ToHashSet(StringComparer.Ordinal);
        var required = installs.SelectMany(record => record.LaunchOptions!.Required).ToHashSet(StringComparer.Ordinal);
        // What the app added for a gone install and a remaining one needs stays, as that one's to take back.
        var adoptable = waiting.Where(option => current.Contains(option) && required.Contains(option) && !owned.Contains(option))
            .ToHashSet(StringComparer.Ordinal);
        var add = new List<string>();
        var records = new List<LaunchOptionsRecordChange>();
        foreach (var record in installs)
        {
            var options = record.LaunchOptions!;
            ValueList<string> adopted = [.. options.Required.Where(adoptable.Contains)];
            adoptable.ExceptWith(adopted);
            // An option the app saw before that is gone was taken out by the player, who may have meant it.
            var reapply = _reapply.Contains(record.Id);
            ValueList<string> adding = [.. options.Required.Where(option => !current.Contains(option) && (reapply || !options.WasSeen(option)))];
            ValueList<string> found = [.. options.Required.Where(option => current.Contains(option) && !options.WasSeen(option)
                && !adopted.Contains(option))];
            add.AddRange(adding.Where(option => !add.Contains(option)));
            if (adopted.Count + adding.Count + found.Count > 0)
            {
                records.Add(new LaunchOptionsRecordChange(record, adopted, adding, found));
            }
        }

        ValueList<string> remove = [.. waiting.Where(option => current.Contains(option) && !required.Contains(option))];
        return add.Count + remove.Count + records.Count + waiting.Count == 0
            ? null
            : new LaunchOptionsChange(game, [.. add], remove, [.. records], waiting);
    }

    private void Save(ValueDictionary<GameKey, ValueList<string>> removals)
    {
        Removals = removals;
        _writer.Save(new LaunchOptionsRemovals(removals));
    }
}
