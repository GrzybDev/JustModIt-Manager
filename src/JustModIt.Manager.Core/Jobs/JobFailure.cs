using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Jobs;

public enum JobFailureReason
{
    /// <summary>Another job for the same game is queued or running.</summary>
    AlreadyRunning,

    GameNotDetected,

    /// <summary>The store reports the game broken, offline or waiting for an update; see <see cref="JobFailure.GameState"/>.</summary>
    GameNotReady,

    GameRunning,

    GameFolderMissing,

    /// <summary>Another install, uninstall or recovery works on the same game folder, possibly in another copy of the app.</summary>
    GameBusy,

    /// <summary>Files are held open by <see cref="JobFailure.Processes"/>.</summary>
    FilesLocked,

    /// <summary>The user may not change <see cref="JobFailure.Files"/>, e.g. a game under Program Files.</summary>
    PermissionRequired,

    NotEnoughSpace,

    /// <summary>The game's files are not the version the package was made for.</summary>
    UnsupportedGameVersion,

    /// <summary>The project's new release was made for another version of the game, so the installed one was kept.</summary>
    NewReleaseIncompatible,

    /// <summary>The game's files already have the package's content, e.g. from a manual install.</summary>
    AlreadyApplied,

    /// <summary>Another project's install owns files this one would change.</summary>
    ConflictingInstall,

    /// <summary>
    /// Another app, or another build of this one, manages the game's files (<see cref="JobFailure.OtherApp"/>, whose folder
    /// is <see cref="JobFailure.Location"/>); once it has uninstalled, the game is free.
    /// </summary>
    ManagedByOtherApp,

    /// <summary>An interrupted operation on this game could not be finished or undone yet.</summary>
    RecoveryPending,

    /// <summary>The install is gone, e.g. it was uninstalled meanwhile.</summary>
    InstallNotFound,

    /// <summary>The install's record cannot be read, e.g. it was written by a newer version of the app.</summary>
    UnreadableInstall,

    /// <summary>The folder holding the originals is not there, e.g. on a disconnected drive; see <see cref="JobFailure.Location"/>.</summary>
    BackupFolderUnavailable,

    ProjectNotFound,

    /// <summary>Nothing in the release fits the game's store and system; see <see cref="JobFailure.Unavailability"/>.</summary>
    NoPackage,

    Offline,

    ServerError,

    DownloadCorrupt,

    InvalidPackage,

    /// <summary>The package was made for a newer version of the app.</summary>
    AppUpdateRequired,

    /// <summary>The operation failed part-way and every change was undone.</summary>
    InstallFailed,

    /// <summary>The operation failed part-way and some changes could not be undone yet; recovery tries again at the next start.</summary>
    RollbackIncomplete,

    AccessDenied,

    DiskError,

    Unexpected,
}

/// <summary>Why a job failed, with the details the app needs to tell the player.</summary>
/// <param name="Reason">What went wrong; the other properties carry the details that apply to it.</param>
public sealed record JobFailure(JobFailureReason Reason)
{
    /// <summary>Game-relative paths of the files concerned.</summary>
    public ValueList<string> Files { get; init; }

    /// <summary>Running game processes, or the programs holding locked files, by name.</summary>
    public ValueList<string> Processes { get; init; }

    public GameInstallState? GameState { get; init; }

    public GameStateReason? StateReason { get; init; }

    public SelectionUnavailability? Unavailability { get; init; }

    public CatalogueErrorReason? CatalogueError { get; init; }

    public long? RequiredBytes { get; init; }

    public long? AvailableBytes { get; init; }

    /// <summary>
    /// A full path: the folder whose volume lacks space, the backup folder that is not there, or another app's folder.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>The app that manages the game's files, as it names itself in its folder.</summary>
    public string? OtherApp { get; init; }
}
