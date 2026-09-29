using System.Text;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

public sealed class GogInfoReaderTests
{
    private const string Folder = @"D:\GOG Games\Card Duel";

    private readonly InMemoryFileSystem _files = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_base_game_is_picked_among_its_dlc()
    {
        _files.AddInfo(Folder, "2001", "Card Duel: Expansion", rootGameId: "1971477531", playTasks: false);
        _files.AddInfo(Folder, "1971477531", "Card Duel", buildId: "59534219748634025", languages: "\"pl-PL\", \"en-US\"");
        _files.AddInfo(Folder, "2002", "Card Duel: Soundtrack", rootGameId: "1971477531");

        var infos = await Read();

        infos.Entries.Should().HaveCount(3);
        infos.Base.Should().Be(new GogGameInfo("1971477531", "1971477531", "Card Duel", "59534219748634025", HasPlayTasks: true,
            GogFiles.WrittenAt, ["pl-PL", "en-US"]));
        infos.Find("2001")!.IsDlc.Should().BeTrue();
    }

    [Fact]
    public async Task A_byte_order_mark_trailing_commas_and_numeric_ids_are_accepted()
    {
        var json = "\uFEFF{ \"gameId\": 1207658924, \"buildId\": 59534219748634025, \"name\": \"Example\", \"playTasks\": [ {}, ], }";
        _files.AddFile(Path.Join(Folder, "goggame-1207658924.info"), Encoding.UTF8.GetBytes(json));

        var info = (await Read()).Base!;

        info.GameId.Should().Be("1207658924");
        info.BuildId.Should().Be("59534219748634025");
        info.RootGameId.Should().BeNull();
    }

    [Fact]
    public async Task A_file_whose_id_differs_from_its_name_is_ignored_and_a_damaged_one_is_skipped()
    {
        _files.AddFile(Path.Join(Folder, "goggame-3001.info"), """{ "gameId": "3002", "name": "Copied" }""");
        _files.AddFile(Path.Join(Folder, "goggame-3003.info"), "{ broken");
        _files.AddInfo(Folder, "3004", "Kept");

        var infos = await Read();

        infos.Entries.Select(entry => entry.GameId).Should().Equal("3004");
    }

    [Fact]
    public async Task The_build_falls_back_to_the_id_file()
    {
        _files.AddInfo(Folder, "3001", "Example", buildId: null);
        _files.AddFile(Path.Join(Folder, "goggame-3001.id"), """{ "buildId": 51234567890123456 }""");

        (await Read()).Base!.BuildId.Should().Be("51234567890123456");
    }

    [Fact]
    public async Task Files_inside_a_mac_app_bundle_are_read()
    {
        _files.AddInfo(Path.Join(Folder, "Contents", "Resources"), "3001", "Example");

        (await Read()).Base!.GameId.Should().Be("3001");
    }

    [Fact]
    public async Task A_root_without_play_tasks_counts_when_nothing_better_exists()
    {
        _files.AddInfo(Folder, "3001", "Example", playTasks: false);

        (await Read()).Base!.GameId.Should().Be("3001");
    }

    [Fact]
    public async Task The_language_name_stands_in_when_no_tags_are_given()
    {
        _files.AddFile(Path.Join(Folder, "goggame-3001.info"), """{ "gameId": "3001", "language": "Polish", "playTasks": [ {} ] }""");

        (await Read()).Base!.Languages.Should().Equal("pl");
    }

    [Fact]
    public async Task A_missing_folder_has_no_files() =>
        (await Read()).Should().BeEquivalentTo(GogInfoSet.Empty);

    private Task<GogInfoSet> Read() => new GogInfoReader(_files).ReadAsync(Folder, Token);
}
