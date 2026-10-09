using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Library;

/// <summary>What the last scan of one store found about the store itself.</summary>
/// <param name="Store">The store.</param>
/// <param name="IsPresent">The store's client, its data or a folder the player added for it exists.</param>
/// <param name="ScanFailed">The last scan failed as a whole; the store's games are the ones found before, marked stale.</param>
/// <param name="IsComplete">False when the store's list of libraries could not be read, or a library on it did not answer.</param>
/// <param name="Libraries">Its libraries, including ones kept from earlier scans while they could not be read.</param>
public sealed record StoreStatus(StoreId Store, bool IsPresent, bool ScanFailed, bool IsComplete, ValueList<LibraryLocation> Libraries)
{
    /// <summary>The last scan read every library and record, all of them online.</summary>
    public bool IsFullyRead => !ScanFailed && IsComplete && Libraries.All(library => library.IsFullyRead);
}

/// <summary>Every game the stores reported, one per key, with what each store's scan could and could not read.</summary>
public sealed record LibrarySnapshot
{
    public static LibrarySnapshot Empty { get; } = new();

    /// <summary>Grows with every published snapshot, so a snapshot that arrives late can be told apart and dropped.</summary>
    public long Version { get; init; }

    public ValueDictionary<GameKey, InstalledGame> Games { get; init; }

    public ValueList<StoreStatus> Stores { get; init; }

    public DateTimeOffset? ScannedAt { get; init; }

    /// <summary>
    /// Loaded from disk when the app started and shown until the first scan finishes; nothing may be decided from it,
    /// since any game may have changed while the app was not running.
    /// </summary>
    public bool IsCached { get; init; }

    /// <summary>Gives the game with <paramref name="key"/>; null when no store reports it.</summary>
    /// <param name="key">The game's store and id.</param>
    public InstalledGame? Find(GameKey key) => Games.TryGetValue(key, out var game) ? game : null;

    /// <summary>Gives what the last scan found about <paramref name="store"/>; null when it was never scanned.</summary>
    /// <param name="store">The store to look up.</param>
    public StoreStatus? FindStore(StoreId store) => Stores.FirstOrDefault(status => status.Store == store);

    /// <summary>
    /// Tells whether the store confirms a game gone from <paramref name="folder"/>, which takes a fresh scan that read every
    /// library and record, all online, and found the game nowhere or elsewhere.
    /// </summary>
    /// <param name="key">The game.</param>
    /// <param name="folder">The folder the game was installed in.</param>
    public bool ConfirmsGone(GameKey key, string folder) =>
        !IsCached
        && FindStore(key.Store) is { IsFullyRead: true }
        && (Find(key) is not { } game || !PathComparison.AreSame(game.InstallDirectory, folder));

    /// <summary>
    /// Tells whether <paramref name="other"/> holds the same games and store reports; the version and scan time do not
    /// count.
    /// </summary>
    /// <param name="other">The snapshot to compare with.</param>
    public bool HasSameContent(LibrarySnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Games == other.Games && Stores == other.Stores && IsCached == other.IsCached;
    }
}
