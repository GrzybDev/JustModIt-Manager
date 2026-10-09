using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// What detection found on this machine, store by store, for a player whose game is missing: which stores exist, their
/// libraries and which of them could not be read.
/// </summary>
/// <param name="Stores">One entry per store the app supports.</param>
/// <param name="ScannedAt">When the stores were last scanned; null before the first scan.</param>
public sealed record DetectionReport(ValueList<StoreDetection> Stores, DateTimeOffset? ScannedAt)
{
    /// <summary>Builds the report from <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The library's latest snapshot.</param>
    public static DetectionReport From(LibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new DetectionReport(
            [
                .. snapshot.Stores.Select(store => new StoreDetection(
                    store.Store,
                    store.IsPresent,
                    store.ScanFailed,
                    store.IsComplete,
                    [.. store.Libraries.Select(library => new LibraryDetection(library,
                        snapshot.Games.Values.Count(game => game.Key.Store == store.Store && game.LibraryId == library.Id)))],
                    snapshot.Games.Values.Count(game => game.Key.Store == store.Store))),
            ],
            snapshot.ScannedAt);
    }
}

/// <summary>What detection found for one store.</summary>
/// <param name="Store">The store.</param>
/// <param name="IsFound">The store's client, its data or a folder the player added for it exists.</param>
/// <param name="ScanFailed">The store could not be scanned at all.</param>
/// <param name="IsComplete">False when the store's list of libraries could not be read, or a library on it did not answer.</param>
/// <param name="Libraries">Its libraries, each with the games found in it.</param>
/// <param name="GameCount">The games found in all its libraries.</param>
public sealed record StoreDetection(
    StoreId Store,
    bool IsFound,
    bool ScanFailed,
    bool IsComplete,
    ValueList<LibraryDetection> Libraries,
    int GameCount);

public sealed record LibraryDetection(LibraryLocation Library, int GameCount);
