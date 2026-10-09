using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;

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
        var offline = Game with { State = GameInstallState.Offline, Reason = GameStateReason.LibraryOffline };
        (Scan(online) with { UnansweredGames = [new UnansweredGame(Game.Key, @"D:\Moved")] }).Describe(Game).Should().Be(offline);
        (Scan(online) with { UnansweredGames = [new UnansweredGame(new GameKey(StoreId.Steam, "1002"), Game.InstallDirectory)] })
            .Describe(Game).Should().Be(offline, "a record of a pack names the folder of the game found there");
        (Scan(online) with { UnansweredGames = [new UnansweredGame(new GameKey(StoreId.Gog, "1001"), @"D:\Moved")] }).Describe(Game)
            .Should().BeNull();
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
