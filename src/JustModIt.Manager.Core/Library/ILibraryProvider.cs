using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// Finds the games of one store without keeping state between calls (the caller remembers earlier scans), reading the
/// machine only through the file system and registry it was given.
/// </summary>
public interface ILibraryProvider
{
    /// <summary>The store whose games the provider finds.</summary>
    StoreId Store { get; }

    /// <summary>
    /// Scans every library of the store; an unreadable library or record is reported in the result instead of failing
    /// the scan.
    /// </summary>
    /// <param name="context">The folders the player added for the store and the access that reads each drive.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the store's records of one game again, right before its files are changed, since a scan may be hours old.
    /// </summary>
    /// <param name="game">The game as the last scan found it.</param>
    /// <param name="context">The folders the player added for the store and the access that reads each drive.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>
    /// The game as the store describes it now, in a blocking state when that cannot be told; null when the store no
    /// longer lists it.
    /// </returns>
    Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Tells whether <paramref name="path"/> holds this store's own records of games, for a folder the player wants to
    /// add because the store did not report it.
    /// </summary>
    /// <param name="path">The folder the player picked.</param>
    /// <param name="cancellationToken">Cancels the inspection.</param>
    Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken);
}
