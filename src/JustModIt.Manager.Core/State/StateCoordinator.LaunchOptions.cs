using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Steam;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.State;

public sealed partial class StateCoordinator
{
    private void ChangeLaunchOptions()
    {
        if (steamUi is not null && _launchOptions.TryStart(_records.Values, _steamUi, _activeJobs.ContainsKey) is { } change)
        {
            _ = ChangeLaunchOptionsAsync(steamUi, change);
        }
    }

    // Steam's interface first, then the records of the installs that keep what it added.
    private async Task ChangeLaunchOptionsAsync(ISteamUi steam, LaunchOptionsChange change)
    {
        var recorded = new List<InstallRecord>();
        var unowned = new List<string>();
        var succeeded = false;
        var recordsKept = true;
        try
        {
            var result = change.Add.Count + change.Remove.Count == 0
                ? new LaunchOptionsResult(null)
                : await steam.ChangeLaunchOptionsAsync(change.Game, change.Add, change.Remove, _stopping).ConfigureAwait(false);
            if (result.Failure is { } failure)
            {
                LogLaunchOptionsNotChanged(change.Game, failure.ToString());
            }
            else
            {
                var after = (result.Options ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                foreach (var record in change.Records)
                {
                    var kept = record.Kept(after);
                    try
                    {
                        if (await engine.SetLaunchOptionsAsync(record.Record.Id, kept, _stopping).ConfigureAwait(false) is { } updated)
                        {
                            recorded.Add(updated);
                            continue;
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        LogLaunchOptionsNotKept(record.Record.Id, exception.GetType().Name);
                        recordsKept = false;
                    }

                    // The install is gone or its record cannot be written, so what was added for it is the app's to take back.
                    unowned.AddRange(kept.Added);
                }

                succeeded = true;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogLaunchOptionsNotChanged(change.Game, exception.GetType().Name);
        }

        Post(new LaunchOptionsChanged(change, succeeded, recorded, unowned, recordsKept));
    }

    private void ApplyLaunchOptions(LaunchOptionsChanged changed)
    {
        var unowned = new List<string>(changed.Unowned);
        foreach (var record in changed.Recorded)
        {
            if (_records.ContainsKey(record.Id))
            {
                _records[record.Id] = record;
            }
            // Removed meanwhile by a job that read the record before the change.
            else
            {
                unowned.AddRange(record.LaunchOptions!.Added);
            }
        }

        _launchOptions.OnChanged(changed.Change, changed.Succeeded, unowned, changed.RecordsKept);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The launch options of {Game} could not be changed in Steam: {Error}")]
    private partial void LogLaunchOptionsNotChanged(GameKey game, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Install {RecordId} could not keep the launch options added for it: {Error}")]
    private partial void LogLaunchOptionsNotKept(Guid recordId, string error);

    /// <summary>A change of a game's launch options ended.</summary>
    /// <param name="Change">The change.</param>
    /// <param name="Succeeded">Steam took it.</param>
    /// <param name="Recorded">The records written with what the change added.</param>
    /// <param name="Unowned">What the change added for installs whose records could not keep it.</param>
    /// <param name="RecordsKept">Every record still there was written.</param>
    private sealed record LaunchOptionsChanged(LaunchOptionsChange Change, bool Succeeded, IReadOnlyList<InstallRecord> Recorded,
        IReadOnlyList<string> Unowned, bool RecordsKept) : Trigger;

    private sealed record LaunchOptionsReapplyRequested(Guid Install) : Trigger;
}
