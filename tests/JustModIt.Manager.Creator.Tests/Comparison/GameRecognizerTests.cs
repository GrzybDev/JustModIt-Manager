using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator.Comparison;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Creator.Tests.Comparison;

public sealed class GameRecognizerTests
{
    private static readonly GameKey LanternSteam = new(StoreId.Steam, "5100");
    private static readonly GameKey LanternGog = new(StoreId.Gog, "1205100");
    private static readonly GameKey Tidewater = new(StoreId.Steam, "5200");
    private static readonly GameKey Ashfall = new(StoreId.Gog, "5300");

    private readonly InMemoryFileSystem _files = new();

    [Fact]
    public async Task Recognises_a_game_while_another_drive_hangs()
    {
        Add(@"D:\Work\Translation", "data/strings.bin", "data/fonts/title.ttf", "loader/proxy.dll");
        Add(@"C:\Steam\Lantern Isle", "data/strings.bin", "data/fonts/body.ttf", "bin/lantern.exe");
        Add(@"C:\GOG\Lantern Isle", "data/strings.bin", "data/fonts/body.ttf", "bin/lantern.exe");
        Add(@"C:\Steam\Tidewater", "Tide_Data/level.bin");
        Add(@"X:\Games\Ashfall", "data/strings.bin");
        using var hanging = new HangingFileSystem(_files);
        hanging.Hang(@"X:\Games\Ashfall");
        // A game that has none of the folder's top-level names is read no further than its top level.
        hanging.Hang(@"C:\Steam\Tidewater\Tide_Data");
        var time = new FakeTimeProvider();

        var recognizing = GameRecognizer.RecognizeAsync(@"D:\Work\Translation",
        [
            new(Ashfall, "Ashfall", @"X:\Games\Ashfall"),
            new(Tidewater, "Tidewater", @"C:\Steam\Tidewater"),
            new(LanternSteam, "Lantern Isle", @"C:\Steam\Lantern Isle"),
            new(LanternGog, "Lantern Isle", @"C:\GOG\Lantern Isle"),
        ], new LibraryAccess(time), hanging, TestContext.Current.CancellationToken);
        await hanging.RunOutOfTimeAsync(time);
        var recognition = await recognizing;

        recognition.Winner!.Key.Should().Be(LanternSteam, "copies of one title are no rivals of each other");
        recognition.Games.Select(game => (game.Key, game.Score)).Should().Equal((LanternSteam, 3), (LanternGog, 3));
        (recognition.FileCount, hanging.HungCalls).Should().Be((3, 1));
    }

    [Fact]
    public async Task Offers_no_winner_when_two_games_score_alike_unless_the_folder_is_named_after_one()
    {
        Add(@"D:\Work\Translation", "data/strings.bin");
        Add(@"D:\Work\Tidewater PL", "data/strings.bin");
        Add(@"C:\Steam\Lantern Isle", "data/strings.bin");
        Add(@"C:\Steam\Tidewater", "data/strings.bin");
        IReadOnlyList<RecognitionCandidate> games = [new(LanternSteam, "Lantern Isle", @"C:\Steam\Lantern Isle"), new(Tidewater, "Tidewater", @"C:\Steam\Tidewater")];

        var unnamed = await GameRecognizer.RecognizeAsync(@"D:\Work\Translation", games, LibraryAccess.Unlimited, _files, TestContext.Current.CancellationToken);
        var named = await GameRecognizer.RecognizeAsync(@"D:\Work\Tidewater PL", games, LibraryAccess.Unlimited, _files, TestContext.Current.CancellationToken);

        unnamed.Games.Select(game => game.Score).Should().Equal(2, 2);
        unnamed.Winner.Should().BeNull("either game could be meant");
        named.Winner.Should().Be(new RecognizedGame(Tidewater, 2, NameMatches: true));
    }

    private void Add(string folder, params string[] paths)
    {
        foreach (var path in paths)
        {
            _files.AddFile(Path.Join(folder, path), path);
        }
    }
}
