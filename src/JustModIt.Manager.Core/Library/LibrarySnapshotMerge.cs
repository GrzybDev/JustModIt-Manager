using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// Builds the library's next snapshot from the stores' scans, keeping what a scan could not read, so that a hiccup, an
/// unplugged drive or a list caught mid-write never looks like an uninstall.
/// </summary>
internal static class LibrarySnapshotMerge
{
    /// <summary>The snapshot after a scan of some of the stores.</summary>
    /// <param name="previous">The snapshot before the scan.</param>
    /// <param name="stores">Every store the library reads, in the order the snapshot lists them.</param>
    /// <param name="scans">What each scanned store reported, null when its scan failed; a store left out keeps its state.</param>
    /// <param name="now">When the scan ran.</param>
    public static LibrarySnapshot Next(LibrarySnapshot previous, IEnumerable<StoreId> stores,
        IReadOnlyDictionary<StoreId, StoreScan?> scans, DateTimeOffset now)
    {
        var statuses = new List<StoreStatus>();
        var games = ImmutableDictionary.CreateBuilder<GameKey, InstalledGame>();
        foreach (var store in stores)
        {
            var previousStatus = previous.FindStore(store);
            List<InstalledGame> previousGames = [.. previous.Games.Values.Where(game => game.Key.Store == store)];
            var (status, storeGames) = !scans.TryGetValue(store, out var scan) ? (previousStatus, previousGames)
                : scan is null ? Failed(store, previousStatus, previousGames)
                : Merge(scan, previousStatus, previousGames, now);
            if (status is not null)
            {
                statuses.Add(status);
            }

            foreach (var game in storeGames.GroupBy(game => game.Key).Select(sameKey => sameKey.MinBy(Trust)!))
            {
                games[game.Key] = game;
            }
        }

        return new LibrarySnapshot
        {
            Version = previous.Version + 1,
            Games = new ValueDictionary<GameKey, InstalledGame>(games.ToImmutable()),
            Stores = [.. statuses],
            ScannedAt = now,
        };
    }

    private static (StoreStatus Status, List<InstalledGame> Games) Merge(StoreScan scan, StoreStatus? previousStatus,
        List<InstalledGame> previousGames, DateTimeOffset now)
    {
        var games = new List<InstalledGame>(scan.Games);
        var libraries = new List<LibraryLocation>(scan.Libraries);
        var found = games.Select(game => game.Key).ToHashSet();
        void Keep(InstalledGame game)
        {
            if (found.Add(game.Key))
            {
                games.Add(game);
            }
        }

        IEnumerable<InstalledGame> Of(string libraryId) => previousGames.Where(game => game.LibraryId == libraryId);

        // An unplugged drive or a list caught mid-write must not look like an uninstall, which would orphan what was
        // installed into those games.
        foreach (var library in scan.Libraries)
        {
            foreach (var game in library.State switch
            {
                LibraryState.Offline => Of(library.Id).Select(game => game.AsOffline()),
                LibraryState.Unreadable => Of(library.Id).Select(game => game with { IsStale = true }),
                _ => [],
            })
            {
                Keep(game);
            }
        }

        if (!scan.IsComplete && previousStatus is not null)
        {
            foreach (var library in previousStatus.Libraries.Where(library => scan.Libraries.All(current => current.Id != library.Id)))
            {
                libraries.Add(library with { State = LibraryState.Unreadable });
                foreach (var game in Of(library.Id))
                {
                    Keep(game with { IsStale = true });
                }
            }
        }

        foreach (var unreadable in scan.UnreadableGames)
        {
            if (previousGames.FirstOrDefault(game => game.Key == unreadable.Key) is { } last)
            {
                // The store is most likely rewriting the record right now, so the quiet period starts again.
                var activity = unreadable.StoreActivityAt ?? now;
                Keep(last with { IsStale = true, StoreActivityAt = last.StoreActivityAt > activity ? last.StoreActivityAt : activity });
            }
        }

        foreach (var last in previousGames.Where(game => scan.UnansweredGames.Any(unanswered => unanswered.Matches(game))))
        {
            Keep(last.AsOffline());
        }

        return (new StoreStatus(scan.Store, scan.IsPresent, ScanFailed: false, scan.IsComplete, [.. libraries]), games);
    }

    private static (StoreStatus Status, List<InstalledGame> Games) Failed(StoreId store, StoreStatus? previousStatus,
        List<InstalledGame> previousGames) =>
        ((previousStatus ?? new StoreStatus(store, IsPresent: false, ScanFailed: true, IsComplete: true, [])) with { ScanFailed = true },
            [.. previousGames.Select(game => game with { IsStale = true })]);

    // A key reported twice (the same game in two libraries, or kept from an earlier scan) goes to the most trustworthy copy.
    private static int Trust(InstalledGame game) =>
        game.IsReady ? 0
        : game.IsStale ? 3
        : game.State == GameInstallState.Offline ? 2
        : 1;
}
