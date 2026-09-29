using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library;

/// <summary>Everything one scan of a store found, including what it could not read.</summary>
public sealed record StoreScan
{
    public required StoreId Store { get; init; }

    /// <summary>The store's client, its data or a folder the player added for it exists on this machine.</summary>
    public bool IsPresent { get; init; }

    public ValueList<LibraryLocation> Libraries { get; init; }

    /// <summary>At most one game per key, each in one of <see cref="Libraries"/>.</summary>
    public ValueList<InstalledGame> Games { get; init; }

    /// <summary>Games whose records exist but could not be read, such as a manifest caught while the store rewrote it.</summary>
    public ValueList<UnreadableGame> UnreadableGames { get; init; }

    /// <summary>
    /// False when the store's list of libraries could not be read, so a library missing from this scan may still exist.
    /// </summary>
    public bool IsComplete { get; init; } = true;

    /// <summary>What to watch so that changes to these records trigger the next scan.</summary>
    public WatchPlan Watch { get; init; } = WatchPlan.Empty;

    /// <summary>
    /// Describes <paramref name="known"/> as this scan sees it: found anywhere in the store, or in a blocking state when
    /// its library or its records could not be read; null when the store no longer lists it.
    /// </summary>
    /// <param name="known">The game as an earlier scan found it.</param>
    public InstalledGame? Describe(InstalledGame known)
    {
        ArgumentNullException.ThrowIfNull(known);
        if (Games.FirstOrDefault(game => game.Key == known.Key) is { } found)
        {
            return found;
        }

        var library = Libraries.FirstOrDefault(library => library.Id == known.LibraryId);
        if (UnreadableGames.Any(game => game.Key == known.Key)
            || library?.State == LibraryState.Unreadable
            || (library is null && !IsComplete))
        {
            return known with { State = GameInstallState.Unknown, Reason = GameStateReason.UnreadableStoreData, IsStale = false };
        }

        return library?.State == LibraryState.Offline
            ? known with { State = GameInstallState.Offline, Reason = GameStateReason.LibraryOffline, IsStale = false }
            : null;
    }
}

/// <summary>A game whose store record exists but could not be read.</summary>
/// <param name="Key">The game the record is for.</param>
/// <param name="LibraryId">The <see cref="LibraryLocation.Id"/> of the library holding the record.</param>
/// <param name="StoreActivityAt">When the store last wrote the unreadable record; null when it vanished meanwhile.</param>
public sealed record UnreadableGame(GameKey Key, string LibraryId, DateTimeOffset? StoreActivityAt);
