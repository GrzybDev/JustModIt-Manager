using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.State;

public enum MyGameState
{
    /// <summary>
    /// Something can be installed and nothing is, also while several teams offer a project and the player has not
    /// chosen one (<see cref="MyGame.Project"/> is null).
    /// </summary>
    NotInstalled,

    Installed,

    /// <summary>A newer release, or a corrected package, fits the game; see <see cref="MyGameStatus.Update"/>.</summary>
    UpdateAvailable,

    /// <summary>Something, usually the store, changed the installed files; see <see cref="MyGameStatus.Repair"/>.</summary>
    NeedsRepair,

    /// <summary>
    /// The store works on the game, cannot be read, reports it broken or offline, or the game runs; see
    /// <see cref="MyGameStatus.GameState"/>.
    /// </summary>
    GameNotReady,

    /// <summary>A job is queued or running; see <see cref="MyGame.Job"/>.</summary>
    Busy,

    /// <summary>An install whose game or project is gone, which can still be uninstalled; see <see cref="MyGameStatus.Orphan"/>.</summary>
    Orphaned,

    /// <summary>
    /// Installed, but the folder holding the originals is not there, e.g. on a disconnected drive, so the install can be
    /// neither uninstalled nor replaced; see <see cref="MyGameStatus.BackupDirectory"/>.
    /// </summary>
    BackupsUnavailable,

    /// <summary>A package the package creator installed to try it out; it can be uninstalled, never updated or repaired.</summary>
    TestInstalled,
}

public enum RepairReason
{
    /// <summary>Installed files are neither the installed nor the original content, e.g. after a game update.</summary>
    FilesModified,

    /// <summary>The store put the original files back, e.g. after checking them.</summary>
    OriginalsRestored,

    FilesMissing,
}

public enum OrphanReason
{
    /// <summary>No store lists the game any more.</summary>
    GameNotDetected,

    /// <summary>The store reports the game in another folder than the one the install changed.</summary>
    GameMoved,

    /// <summary>
    /// The catalogue does not have the project, or has not been read yet, or the install came from no known project.
    /// </summary>
    NotInCatalog,
}

/// <summary>What a game in the player's list, or an install on it, is, and why, so the app can say it in its own words.</summary>
/// <param name="State">What the game or install is, which tells which of the other properties are set.</param>
public sealed record MyGameStatus(MyGameState State)
{
    public GameInstallState? GameState { get; init; }

    public GameStateReason? StateReason { get; init; }

    public RepairReason? Repair { get; init; }

    public OrphanReason? Orphan { get; init; }

    /// <summary>
    /// For an installed release the catalogue knows: whether and what to update to; for one that needs repairing, what the
    /// repair installs.
    /// </summary>
    public UpdateCheck? Update { get; init; }

    /// <summary>The folder that should hold the originals, for <see cref="MyGameState.BackupsUnavailable"/>.</summary>
    public string? BackupDirectory { get; init; }
}
