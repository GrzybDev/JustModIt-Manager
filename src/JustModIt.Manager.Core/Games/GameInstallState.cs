namespace JustModIt.Manager.Core.Games;

/// <summary>
/// What the store is doing with a game, where only <see cref="Installed"/> allows changing its files and every other
/// state, including the ones that are not understood, blocks writing.
/// </summary>
public enum GameInstallState
{
    /// <summary>Installed and idle as far as the store knows.</summary>
    Installed,

    /// <summary>The store wants to update the game but has not started; that may take days.</summary>
    UpdatePending,

    /// <summary>The store is writing the game's files.</summary>
    Updating,

    /// <summary>The store is checking the game's files.</summary>
    Validating,

    /// <summary>The first install is in progress, so the files are incomplete.</summary>
    Installing,

    /// <summary>Files or the whole folder are missing or damaged.</summary>
    Broken,

    /// <summary>The drive or library holding the game is unavailable.</summary>
    Offline,

    /// <summary>The game, or a program started from its folder, is running.</summary>
    Running,

    /// <summary>The store's records do not say clearly what it is doing with the game.</summary>
    Unknown,
}
