using System.Text.Json;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Tests;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class LaunchOptionsRecordTests : IAsyncDisposable
{
    private static readonly InstalledLaunchOptions Options = new(["-examplelog", "-windowed"]) { Added = ["-examplelog"] };

    private readonly InstallHarness _harness = new();

    private static CancellationToken Token => InstallHarness.Token;

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task An_install_keeps_the_launch_options_in_its_record()
    {
        var package = _harness.Zip(new TestPackage().Entry("readme.txt", "Translated read me"));

        await _harness.Engine.InstallAsync(_harness.Request(package) with { LaunchOptions = Options }, null, Token);

        (await _harness.Engine.LoadRecordsAsync(Token)).Should().ContainSingle().Which.LaunchOptions.Should().Be(Options);
    }

    [Fact]
    public async Task The_record_keeps_options_the_app_added_later_unless_the_install_is_gone_or_waits_for_recovery()
    {
        var package = _harness.Zip(new TestPackage().Entry("readme.txt", "Translated read me"));
        var needed = new InstalledLaunchOptions(Options.Required);
        var installed = (await _harness.Engine.InstallAsync(_harness.Request(package) with { LaunchOptions = needed }, null, Token)).Record;

        (await _harness.Engine.SetLaunchOptionsAsync(installed.Id, Options, Token))!.LaunchOptions.Should().Be(Options);

        (await _harness.Engine.LoadRecordsAsync(Token)).Should().ContainSingle().Which.LaunchOptions.Should().Be(Options);
        var journal = Path.Join(_harness.InstallsDirectory, installed.Id.ToString("N"), "journal.jsonl");
        await File.WriteAllTextAsync(journal, "", Token);
        await _harness.Engine.Invoking(engine => engine.SetLaunchOptionsAsync(installed.Id, needed, Token)).Should()
            .ThrowAsync<InstallBlockedException>();
        File.Delete(journal);
        await _harness.UninstallAsync(installed.Id);
        (await _harness.Engine.SetLaunchOptionsAsync(installed.Id, Options, Token)).Should().BeNull();
    }

    [Fact]
    public async Task On_linux_the_loaders_beside_a_windows_builds_programs_get_the_wine_overrides_before_the_projects_options()
    {
        var engine = _harness.CreateEngine(AppPlatform.Linux);
        var package = _harness.Zip(new TestPackage()
            .Entry("bin/DInput8.dll", "loader")
            .Entry("winhttp.dll", "loader")
            .Entry("plugins/version.dll", "a plugin's own library")
            .Entry("bin/scripts/translation.asi", "plugin"));
        var request = _harness.Request(package) with
        {
            Game = _harness.Game with { Platform = OperatingSystemKind.Windows },
            LaunchOptions = new InstalledLaunchOptions(["-examplelog"]),
        };

        var record = (await engine.InstallAsync(request, null, Token)).Record;

        record.LaunchOptions!.Required.Should().Equal("WINEDLLOVERRIDES=dinput8,winhttp=n,b", CheckedLaunchOptions.GameCommand, "-examplelog");
        InstalledLaunchOptions.TakenBack(record.LaunchOptions with { Added = record.LaunchOptions.Required }, null).Should()
            .Equal(["WINEDLLOVERRIDES=dinput8,winhttp=n,b", "-examplelog"], "a wrapper the player put before Steam's command line may need it");
    }

    [Theory]
    [InlineData("steam", OperatingSystemKind.Linux, AppPlatform.Linux, false)]
    [InlineData("steam", OperatingSystemKind.Windows, AppPlatform.Windows, false)]
    [InlineData("gog", OperatingSystemKind.Windows, AppPlatform.Linux, false)]
    [InlineData("steam", OperatingSystemKind.Windows, AppPlatform.Linux, true)]
    public void Only_a_steam_games_windows_build_on_linux_with_its_options_accepted_gets_the_overrides(string store,
        OperatingSystemKind build, AppPlatform platform, bool refused)
    {
        var options = new InstalledLaunchOptions([]) { Refusal = refused ? LaunchOptionsRefusal.UnsafeOption : null };
        var game = _harness.Game with { Key = new GameKey(new StoreId(store), "1001"), Platform = build };

        WineLibraryOverrides.Add(options, game, platform, [new InstalledFile("dinput8.dll", FileChange.Added)], _ => true).Should().Be(options);
    }

    [Fact]
    public void Every_system_library_a_loader_of_the_hosting_rules_stands_in_for_gets_an_override()
    {
        using var rules = JsonDocument.Parse(
            File.ReadAllText(RepositoryFiles.PathOf("src", "JustModIt.Manager.Creator", "Rules", "HostingRules.json")));

        // A loader's own library is named after it, such as UE4SS.dll, and Windows never offers one by that name.
        var standIns = rules.RootElement.GetProperty("thirdPartyTools").EnumerateArray().SelectMany(tool => tool.GetProperty("files")
            .EnumerateArray().Select(file => file.GetString()!)
            .Where(file => Path.GetExtension(file) == ".dll" && Path.GetFileNameWithoutExtension(file) != tool.GetProperty("name").GetString()));

        WineLibraryOverrides.ProxyLibraries.Should().BeEquivalentTo(standIns);
    }

    [Fact]
    public async Task Launch_options_round_trip_and_a_record_without_them_stays_as_it_was()
    {
        var saved = await File.ReadAllBytesAsync(Path.Join(AppContext.BaseDirectory, "Installation", "Fixtures", "v1", "AppData", "installs",
            "5c43ae62d789404da0523d46ec29bcf6", "record.json"), Token);
        var context = InstallationJsonContext.Default.InstallRecord;
        var record = JsonSerializer.Deserialize(saved, context)!;
        var refused = new InstalledLaunchOptions([]) { Refusal = LaunchOptionsRefusal.UnsafeOption };

        var again = JsonSerializer.SerializeToUtf8Bytes(record, context);

        JsonNode.DeepEquals(JsonNode.Parse(again), JsonNode.Parse(saved)).Should().BeTrue();
        record.LaunchOptions.Should().BeNull();
        foreach (var options in new[] { Options, refused })
        {
            JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(record with { LaunchOptions = options }, context), context)!
                .LaunchOptions.Should().Be(options);
        }
    }
}
