using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library;

public sealed class LibraryModelTests
{
    private static readonly InstalledGame Game = new()
    {
        Key = new GameKey(StoreId.Steam, "1001"),
        Name = "Example Quest",
        InstallDirectory = @"D:\Library\steamapps\common\Example Quest",
        LibraryId = @"D:\Library",
        State = GameInstallState.Installed,
    };

    [Fact]
    public void A_folder_is_watched_toward_its_nearest_existing_ancestor()
    {
        var files = new InMemoryFileSystem();
        files.AddDirectory(@"C:\Users\Player\AppData");

        WatchedFolder.Toward(files, @"C:\Users\Player\AppData\heroic\gog_store\installed.json")
            .Should().Be(new WatchedFolder(@"C:\Users\Player\AppData", ["heroic"]));
        files.AddDirectory(@"C:\Users\Player\AppData\heroic\gog_store");
        WatchedFolder.Toward(files, @"C:\Users\Player\AppData\heroic\gog_store\installed.json")
            .Should().Be(new WatchedFolder(@"C:\Users\Player\AppData\heroic\gog_store", ["installed.json"]));
        WatchedFolder.Toward(files, @"Q:\Missing\file.txt").Should().BeNull();
    }

    [Fact]
    public void Watched_folders_are_equal_by_location_and_filters()
    {
        var folder = new WatchedFolder(@"C:\Steam\steamapps", ["downloading", "appmanifest_*.acf"]);

        folder.Should().Be(new WatchedFolder(@"c:\steam\STEAMAPPS\", ["APPMANIFEST_*.acf", "downloading", "downloading"]));
        folder.GetHashCode().Should().Be(new WatchedFolder(@"C:\STEAM\steamapps", []).GetHashCode());
        folder.Should().NotBe(new WatchedFolder(@"C:\Steam\steamapps", ["downloading"]));
        folder.Should().NotBe(new WatchedFolder(@"C:\Steam", ["downloading", "appmanifest_*.acf"]));
    }

    [Fact]
    public void Merged_watch_plans_hold_each_entry_once()
    {
        var key = new WatchedRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Example", IncludeSubkeys: true);
        var first = new WatchPlan([new WatchedFolder(@"C:\A", [])], [key]);
        var second = new WatchPlan([new WatchedFolder(@"c:\a", []), new WatchedFolder(@"C:\B", [])], [key]);

        var merged = first.Merge(second);

        merged.Folders.Should().Equal(new WatchedFolder(@"C:\A", []), new WatchedFolder(@"C:\B", []));
        merged.RegistryKeys.Should().Equal(key);
    }

    [Fact]
    public void A_scan_describes_a_known_game_as_found_offline_unreadable_or_gone()
    {
        var online = new LibraryLocation(@"D:\Library", "example", @"D:\Library", LibraryState.Online);
        var moved = Game with { LibraryId = @"E:\Other", State = GameInstallState.UpdatePending };

        Scan(online, games: [moved]).Describe(Game).Should().Be(moved);
        Scan(online).Describe(Game).Should().BeNull();
        Scan(online with { State = LibraryState.Offline }).Describe(Game)
            .Should().Match<InstalledGame>(game => game.State == GameInstallState.Offline && game.Reason == GameStateReason.LibraryOffline);
        Scan(online with { State = LibraryState.Unreadable }).Describe(Game)!.State.Should().Be(GameInstallState.Unknown);
        Scan(online, unreadable: [new UnreadableGame(Game.Key, Game.LibraryId, null)]).Describe(Game)!.Reason
            .Should().Be(GameStateReason.UnreadableStoreData);
        (Scan() with { IsComplete = false }).Describe(Game)!.State.Should().Be(GameInstallState.Unknown);
        Scan().Describe(Game).Should().BeNull();
    }

    [Fact]
    public void Only_an_installed_game_read_in_this_scan_is_ready()
    {
        Game.IsReady.Should().BeTrue();
        (Game with { IsStale = true }).IsReady.Should().BeFalse();
        (Game with { State = GameInstallState.Running }).IsReady.Should().BeFalse();
    }

    [Fact]
    public void A_game_has_a_language_in_any_region()
    {
        var game = Game with { Languages = ["pl-PL", "pt-BR"] };

        game.HasLanguage("pl").Should().BeTrue();
        game.HasLanguage("PT").Should().BeTrue();
        game.HasLanguage("pl-PL").Should().BeTrue();
        game.HasLanguage("p").Should().BeFalse();
        game.HasLanguage("en").Should().BeFalse();
    }

    [Theory]
    [InlineData("polish", "pl")]
    [InlineData("English", "en")]
    [InlineData("schinese", "zh-CN")]
    [InlineData("brazilian", "pt-BR")]
    [InlineData("latam", "es-419")]
    [InlineData("pl-pl", "pl-PL")]
    [InlineData("PL", "pl")]
    [InlineData("klingon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Store_language_names_and_codes_become_tags(string? text, string? tag) =>
        LanguageNames.FromTagOrName(text).Should().Be(tag);

    private static StoreScan Scan(LibraryLocation? library = null, InstalledGame[]? games = null, UnreadableGame[]? unreadable = null) =>
        new()
        {
            Store = StoreId.Steam,
            Libraries = library is null ? [] : [library],
            Games = [.. games ?? []],
            UnreadableGames = [.. unreadable ?? []],
        };
}
