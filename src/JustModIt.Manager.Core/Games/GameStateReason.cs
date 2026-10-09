namespace JustModIt.Manager.Core.Games;

/// <summary>Why a game has its <see cref="GameInstallState"/>, so the app can explain it.</summary>
public enum GameStateReason
{
    None,

    /// <summary>The store has an update to install.</summary>
    UpdateRequired,

    /// <summary>A download or update was started and then paused or suspended.</summary>
    UpdatePaused,

    /// <summary>The store keeps download data for the game, so a download or update is under way.</summary>
    DownloadInProgress,

    /// <summary>The store reports work on the game's files.</summary>
    StoreBusy,

    Uninstalling,

    /// <summary>The store is moving the game to another library.</summary>
    MovingFolder,

    /// <summary>Another app of the store shares the game's folder and is being installed, updated or run.</summary>
    SharedFolderBusy,

    /// <summary>The launcher's record and the game's own files name different builds, which happens during an update.</summary>
    BuildMismatch,

    /// <summary>The game is treated as installed although its folder lacks the store's description of the game.</summary>
    InfoFileMissing,

    /// <summary>
    /// The game is installed and idle, but the store keeps an unfinished download of it that no job claims, which it may
    /// still apply later.
    /// </summary>
    UnfinishedDownload,

    FilesMissing,

    FilesCorrupt,

    InstallDirectoryMissing,

    /// <summary>The drive or library holding the game is unavailable.</summary>
    LibraryOffline,

    GameRunning,

    /// <summary>The store reports a combination of states the app does not understand.</summary>
    UnknownStoreState,

    /// <summary>The store's records of the game could not be read.</summary>
    UnreadableStoreData,
}
