using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.Tests.Library;

public sealed class LibrarySnapshotMergeTests
{
    private const string First = @"D:\Library";
    private const string Second = @"E:\Library";

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Games_of_a_library_that_cannot_be_read_are_kept_as_stale()
    {
        var next = Next(Previous(Game("1001", First)), Scan(Library(First, LibraryState.Unreadable)));

        next.Find(Key("1001")).Should().Be(Game("1001", First) with { IsStale = true });
    }

    [Fact]
    public void Games_of_an_offline_library_are_kept_as_offline()
    {
        var next = Next(Previous(Game("1001", First)), Scan(Library(First, LibraryState.Offline)));

        next.Find(Key("1001")).Should().Be(Game("1001", First).AsOffline());
    }

    [Fact]
    public void An_incomplete_scan_keeps_the_libraries_it_did_not_list_with_their_games()
    {
        var previous = Previous(Game("1001", First), Game("2001", Second));

        var next = Next(previous, Scan(Library(First, LibraryState.Online)) with { Games = [Game("1001", First)], IsComplete = false });

        next.Find(Key("2001")).Should().Be(Game("2001", Second) with { IsStale = true });
        next.FindStore(StoreId.Steam)!.Libraries.Select(library => (library.Id, library.State))
            .Should().Equal((First, LibraryState.Online), (Second, LibraryState.Unreadable));
    }

    [Fact]
    public void A_game_whose_folder_did_not_answer_stays_offline()
    {
        var next = Next(Previous(Game("1001", First)),
            Scan(Library(First, LibraryState.Online)) with { UnansweredGames = [new UnansweredGame(Key("1001"), Folder("1001", First))] });

        next.Find(Key("1001")).Should().Be(Game("1001", First).AsOffline());
    }

    [Fact]
    public void The_most_trusted_copy_of_a_game_reported_twice_wins()
    {
        var updating = Game("1001", First) with { State = GameInstallState.Updating };

        var next = Next(Previous(), Scan(Library(First, LibraryState.Online), Library(Second, LibraryState.Online))
            with { Games = [updating, Game("1001", Second)] });

        next.Find(Key("1001")).Should().Be(Game("1001", Second));
    }

    private static LibrarySnapshot Next(LibrarySnapshot previous, StoreScan scan) =>
        LibrarySnapshotMerge.Next(previous, [StoreId.Steam], new Dictionary<StoreId, StoreScan?> { [StoreId.Steam] = scan }, Now);

    private static LibrarySnapshot Previous(params InstalledGame[] games) => new()
    {
        Version = 1,
        Games = new ValueDictionary<GameKey, InstalledGame>(games.ToImmutableDictionary(game => game.Key)),
        Stores =
        [
            new StoreStatus(StoreId.Steam, true, false, true,
                [.. games.Select(game => game.LibraryId).Distinct().Select(library => Library(library, LibraryState.Online))]),
        ],
    };

    private static StoreScan Scan(params LibraryLocation[] libraries) =>
        new() { Store = StoreId.Steam, IsPresent = true, Libraries = [.. libraries] };

    private static LibraryLocation Library(string path, LibraryState state) => new(path, "steam-library", path, state);

    private static GameKey Key(string id) => new(StoreId.Steam, id);

    private static string Folder(string id, string library) => Path.Join(library, "steamapps", "common", id);

    private static InstalledGame Game(string id, string library) => new()
    {
        Key = Key(id),
        Name = $"Game {id}",
        InstallDirectory = Folder(id, library),
        LibraryId = library,
        State = GameInstallState.Installed,
    };
}
