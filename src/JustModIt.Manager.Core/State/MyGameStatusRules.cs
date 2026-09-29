using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.State;

/// <summary>
/// What a game in the player's list is. The order is what the player must be told first: work in progress, then whether
/// the game can be touched at all, then what there is to do.
/// </summary>
public static class MyGameStatusRules
{
    /// <param name="game">What its store reports, running when it is known to run; null when no store lists it.</param>
    /// <param name="record">The install on the game; null when nothing is installed.</param>
    /// <param name="project">The installed project as the catalogue has it now, or the one to install; null when there is none.</param>
    /// <param name="repair">What verification found wrong with the install; null while it is intact or unchecked.</param>
    /// <param name="hasJob">The game has a job, which keeps every install on it from changing otherwise.</param>
    /// <param name="backupsUnavailable">The folder holding the install's originals is not there.</param>
    public static MyGameStatus Resolve(InstalledGame? game, InstallRecord? record, ProjectOffer? project, RepairReason? repair,
        bool hasJob, bool backupsUnavailable)
    {
        if (hasJob)
        {
            return new MyGameStatus(MyGameState.Busy);
        }

        if (record is not null)
        {
            if (game is null)
            {
                return Orphaned(OrphanReason.GameNotDetected);
            }

            if (!PathComparison.AreSame(game.InstallDirectory, record.InstallDirectory))
            {
                return Orphaned(OrphanReason.GameMoved);
            }

            if (!game.IsReady)
            {
                return NotReady(game);
            }

            if (backupsUnavailable)
            {
                return new MyGameStatus(MyGameState.BackupsUnavailable) { BackupDirectory = record.Backup.Directory };
            }

            if (record.IsTest)
            {
                return new MyGameStatus(MyGameState.TestInstalled);
            }

            // A repair installs the current release anyway, so it comes before an update.
            if (repair is { } reason)
            {
                return new MyGameStatus(MyGameState.NeedsRepair) { Repair = reason };
            }

            if (project is null)
            {
                return Orphaned(OrphanReason.NotInCatalog);
            }

            var update = InstalledReleaseOf(record) is { } installed ? ReleaseUpdates.Check(installed, project.Selection) : null;
            return new MyGameStatus(update?.State == UpdateState.Available ? MyGameState.UpdateAvailable : MyGameState.Installed)
            {
                Update = update,
            };
        }

        if (game is not { IsReady: true })
        {
            return NotReady(game);
        }

        return project is { Selection.IsInstallable: false }
            ? new MyGameStatus(MyGameState.Unavailable) { Unavailability = project.Selection.Unavailable }
            : new MyGameStatus(MyGameState.NotInstalled);
    }

    /// <summary>The release an install came from, for telling whether the catalogue offers a newer one; null when unknown.</summary>
    /// <param name="record">The install's record.</param>
    public static InstalledRelease? InstalledReleaseOf(InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Source is { } source && Guid.TryParse(source.ReleaseId, out var release)
            && Guid.TryParse(source.ReleaseFileId, out var file)
                ? new InstalledRelease(release, source.ReleaseVersion ?? 0, source.ReleasePublishedAt, file, source.VariantName)
                : null;
    }

    private static MyGameStatus Orphaned(OrphanReason reason) => new(MyGameState.Orphaned) { Orphan = reason };

    // Records kept from an earlier scan because the store's could not be read are no ground for changing anything.
    private static MyGameStatus NotReady(InstalledGame? game) => new(MyGameState.GameNotReady)
    {
        GameState = game is { IsStale: true } ? GameInstallState.Unknown : game?.State,
        StateReason = game is { IsStale: true } ? GameStateReason.UnreadableStoreData : game?.Reason,
    };
}
