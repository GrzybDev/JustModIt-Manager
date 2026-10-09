using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.State;

/// <summary>
/// What a game in the player's list and each install on it are, in the order the player must be told: work in progress,
/// then whether the game can be touched at all, then what there is to do.
/// </summary>
internal static class MyGameStatusRules
{
    /// <summary>What one install on a game is.</summary>
    /// <param name="game">What its store reports, running when it is known to run; null when no store lists it.</param>
    /// <param name="record">The install's record.</param>
    /// <param name="project">The installed project as the catalogue has it now; null when it does not have it.</param>
    /// <param name="repair">What verification found wrong with the install; null while it is intact or unchecked.</param>
    /// <param name="hasJob">The game has a job, which keeps every install on it from changing otherwise.</param>
    /// <param name="backupsUnavailable">The folder holding the install's originals is not there.</param>
    public static MyGameStatus Resolve(InstalledGame? game, InstallRecord record, ProjectOffer? project, RepairReason? repair,
        bool hasJob, bool backupsUnavailable)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (hasJob)
        {
            return new MyGameStatus(MyGameState.Busy);
        }

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

    /// <summary>
    /// What a game is: its job, else what the player must hear first of its installs, else whether it can take one.
    /// </summary>
    /// <param name="game">What its store reports, running when it is known to run; null when no store lists it.</param>
    /// <param name="hasJob">The game has a job.</param>
    /// <param name="installs">What each install on the game is.</param>
    public static MyGameStatus Resolve(InstalledGame? game, bool hasJob, IEnumerable<MyGameStatus> installs)
    {
        ArgumentNullException.ThrowIfNull(installs);
        return hasJob ? new MyGameStatus(MyGameState.Busy)
            : installs.MinBy(Urgency) is { } first ? first
            : game is { IsReady: true } ? new MyGameStatus(MyGameState.NotInstalled)
            : NotReady(game);
    }

    /// <summary>The release an install came from, for telling whether the catalogue offers a newer one; null when unknown.</summary>
    /// <param name="record">The install's record.</param>
    public static InstalledRelease? InstalledReleaseOf(InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Source is { ReleaseUuid: { } release, ReleaseFileUuid: { } file } source
            ? new InstalledRelease(release, source.ReleaseVersion ?? 0, source.ReleasePublishedAt, file, source.VariantName)
            : null;
    }

    // Not the order the rules for one install test in: a test install must not hide that another install needs a repair.
    private static int Urgency(MyGameStatus status) => status switch
    {
        { State: MyGameState.Busy } => 0,
        { State: MyGameState.Orphaned, Orphan: not OrphanReason.NotInCatalog } => 1,
        { State: MyGameState.GameNotReady } => 2,
        { State: MyGameState.BackupsUnavailable } => 3,
        { State: MyGameState.NeedsRepair } => 4,
        { State: MyGameState.UpdateAvailable } => 5,
        { State: MyGameState.TestInstalled } => 6,
        { State: MyGameState.Orphaned } => 7,
        _ => 8,
    };

    private static MyGameStatus Orphaned(OrphanReason reason) => new(MyGameState.Orphaned) { Orphan = reason };

    private static MyGameStatus NotReady(InstalledGame? game)
    {
        var trusted = game?.TrustedState();
        return new(MyGameState.GameNotReady) { GameState = trusted?.State, StateReason = trusted?.Reason };
    }
}
