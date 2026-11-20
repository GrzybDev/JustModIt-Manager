using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator.Comparison;

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
        var translation = TestPaths.Rooted('D', "Work", "Translation");
        var lanternSteam = TestPaths.Rooted('C', "Steam", "Lantern Isle");
        var lanternGog = TestPaths.Rooted('C', "GOG", "Lantern Isle");
        var tidewater = TestPaths.Rooted('C', "Steam", "Tidewater");
        var ashfall = TestPaths.Rooted('X', "Games", "Ashfall");
        Add(translation, "data/strings.bin", "data/fonts/title.ttf", "loader/proxy.dll");
        Add(lanternSteam, "data/strings.bin", "data/fonts/body.ttf", "bin/lantern.exe");
        Add(lanternGog, "data/strings.bin", "data/fonts/body.ttf", "bin/lantern.exe");
        Add(tidewater, "Tide_Data/level.bin");
        Add(ashfall, "data/strings.bin");
        using var hanging = new HangingFileSystem(_files);
        hanging.Hang(ashfall);
        // A game that has none of the folder's top-level names is read no further than its top level.
        hanging.Hang(Path.Join(tidewater, "Tide_Data"));
        var time = new CountingTimeProvider();

        var recognizing = GameRecognizer.RecognizeAsync(translation,
        [
            new(Ashfall, "Ashfall", ashfall),
            new(Tidewater, "Tidewater", tidewater),
            new(LanternSteam, "Lantern Isle", lanternSteam),
            new(LanternGog, "Lantern Isle", lanternGog),
        ], new LibraryAccess(time, hanging), hanging, TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task Recognises_a_game_whose_files_are_spelled_in_another_letter_case()
    {
        Add(@"D:\Work\Translation", "data/paks/strings.pak");
        Add(@"C:\Steam\Lantern Isle", "Data/Paks/strings.pak", "Data/Paks/fonts.pak");

        var recognition = await GameRecognizer.RecognizeAsync(@"D:\Work\Translation", [new(LanternSteam, "Lantern Isle", @"C:\Steam\Lantern Isle")],
            LibraryAccess.Unlimited, _files, TestContext.Current.CancellationToken);

        recognition.Winner.Should().Be(new RecognizedGame(LanternSteam, 2, NameMatches: false));
    }

    private void Add(string folder, params string[] paths)
    {
        foreach (var path in paths)
        {
            _files.AddFile(Path.Join(folder, path), path);
        }
    }
}
