using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Library;

/// <summary>Keeps the installed games of every store up to date and checks one game right before it is changed.</summary>
public interface ILibraryService
{
    /// <summary>The newest snapshot; before the first scan, the one saved at the last run, marked cached, if any.</summary>
    LibrarySnapshot Snapshot { get; }

    /// <summary>Raised on the library's background loop after a scan changed the snapshot; handlers must return quickly.</summary>
    event EventHandler<LibraryChangedEventArgs>? Changed;

    /// <summary>Asks for a scan of every store as soon as the current one, if any, is done.</summary>
    void RequestCheck();

    /// <summary>
    /// Like <see cref="RequestCheck"/>, completing once that scan has finished; cancelled when the library stops first.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting; the scan still runs.</param>
    Task CheckNowAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one game's store records again and looks for processes running from its folder, right before its files
    /// are changed. The snapshot alone is never trusted for writing.
    /// </summary>
    Task<GameProbe> ProbeAsync(GameKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a folder the player picked because detection did not find a game, when a store's own records in it prove
    /// what is there.
    /// </summary>
    /// <param name="path">The folder the player picked.</param>
    /// <param name="cancellationToken">Cancels the inspection of the folder.</param>
    Task<LibraryFolderResult> AddLibraryFolderAsync(string path, CancellationToken cancellationToken);

    /// <summary>Stops scanning a folder the player added and scans the stores again.</summary>
    /// <param name="folder">A folder from the settings.</param>
    void RemoveLibraryFolder(LibraryFolder folder);
}

/// <summary>A game as its store describes it at this moment.</summary>
/// <param name="Key">The game probed.</param>
/// <param name="Game">Null when the store no longer lists the game.</param>
/// <param name="RunningProcesses">Programs running from the game's folder, which make it <see cref="GameInstallState.Running"/>.</param>
public sealed record GameProbe(GameKey Key, InstalledGame? Game, ValueList<RunningProcess> RunningProcesses)
{
    public bool IsGone => Game is null;

    /// <summary>The store reports the game installed and idle, and nothing runs from its folder.</summary>
    public bool CanWrite => Game is { IsReady: true } && RunningProcesses.Count == 0;
}

/// <summary>The outcome of adding a folder the player picked.</summary>
/// <param name="Folder">The folder added, which may be an ancestor of the one picked; null when it was refused.</param>
/// <param name="Rejection">Why the folder was refused; null when it was added.</param>
public sealed record LibraryFolderResult(LibraryFolder? Folder, LibraryFolderRejection? Rejection)
{
    /// <summary>Creates the result for a refused folder.</summary>
    /// <param name="rejection">Why it was refused.</param>
    public static LibraryFolderResult Rejected(LibraryFolderRejection rejection) => new(null, rejection);
}
