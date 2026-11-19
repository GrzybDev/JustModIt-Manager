using System.Text.Json;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class SteamUiServiceTests : IAsyncDisposable
{
    private const string Slug = "example-app";

    private static readonly GameKey Game = new(StoreId.Steam, "400");

    private static readonly AchievementTexts Texts = new()
    {
        SteamAppId = 400,
        Language = "polish",
        Revision = 3,
        UpdatedAt = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero),
        Achievements = [new AchievementText { ApiName = "FIRST", Name = "Pierwsze kroki", Description = "Ukończ samouczek" }],
    };

    private readonly CancellationTokenSource _stop = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"), "Steam");
    private readonly FakeSteamClient _steam = new();
    private readonly FakeFolderWatcher _folders = new();
    private readonly FakeSettings _settings = new(AppSettings.Default with { OnboardingCompleted = true });
    private readonly SteamUiService _service;
    private Task _run = Task.CompletedTask;

    public SteamUiServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "millennium", "config"));
        File.WriteAllText(Path.Combine(_root, "millennium", "config", "config.json"),
            """{"general": {"accentColor": "#123456"}, "plugins": {"enabledPlugins": ["another-plugin"]}}""");
        _steam.Root = _root;
        _service = new SteamUiService(_steam, new FakeDevTools(), _folders, _settings, Options, TimeProvider.System,
            NullLogger<SteamUiService>.Instance);
    }

    private static AgentOptions Options { get; } = new()
    {
        Identity = new AppIdentity("ExampleApp", "ExampleVendor", Slug, isDevelopmentBuild: false),
        ProductName = "Example App",
        Version = "1.4.0",
        Locations = new AppDataLocations(Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath()),
        Names = InstanceNames.ForCurrentUser("ExampleApp"),
        Api = new CatalogueApiOptions(new Uri("https://api.example.com/"), null, "ExampleApp", "1.4.0"),
        PackageExtension = ".examplemod",
        ExecutablePath = Path.Combine(Path.GetTempPath(), "ExampleApp.exe"),
        InstallDirectory = Path.GetTempPath(),
    };

    private string PluginFolder => Path.Combine(_root, "millennium", "plugins", Slug);

    private string ConfigFile => Path.Combine(_root, "millennium", "config", "config.json");

    [Fact]
    public async Task The_plugin_goes_in_while_steam_is_closed_once_the_setup_is_done_and_gets_the_texts_to_show()
    {
        _settings.Update(settings => settings with { OnboardingCompleted = false });
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.Delivery == SteamUiDelivery.Millennium);
        Directory.Exists(PluginFolder).Should().BeFalse("nothing goes into Steam's folder before the first-run setup");

        _settings.Update(settings => settings with { OnboardingCompleted = true });

        await Eventually.HoldsAsync(() => _service.Status.PluginVersion == "1.4.0");
        _service.Status.Should().Match<SteamUiStatus>(status => !status.Reachable && !status.RestartNeeded);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "millennium", "config", "config.json")));
        config.RootElement.GetProperty("plugins").GetProperty("enabledPlugins").EnumerateArray().Select(name => name.GetString())
            .Should().Equal("another-plugin", Slug);
        config.RootElement.GetProperty("general").GetProperty("accentColor").GetString().Should().Be("#123456");
        File.ReadAllText(Path.Combine(PluginFolder, ".millennium", "Dist", "index.js")).Should().Be(SteamScripts.PluginFrontend(Slug));

        using var texts = TextsFile();
        texts.RootElement.GetProperty("language").GetString().Should().Be("polish");
        texts.RootElement.GetProperty("texts").GetProperty("400").GetProperty("FIRST").EnumerateArray().Select(text => text.GetString())
            .Should().Equal("Pierwsze kroki", "Ukończ samouczek");
        texts.RootElement.GetProperty("panel").GetProperty("subtitle").GetString().Should().Contain("Example App").And.Contain("1.4.0");
        texts.RootElement.GetProperty("panel").GetProperty("manage").GetProperty("link").GetString().Should().Be($"{Slug}://library");
    }

    [Fact]
    public async Task A_running_steam_holds_the_plugin_back_until_it_exits()
    {
        _steam.Start(7);
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.RestartNeeded);
        Directory.Exists(PluginFolder).Should().BeFalse("Millennium writes its settings back when Steam exits");

        _steam.Exit(7);

        await Eventually.HoldsAsync(() => _service.Status is { RestartNeeded: false, PluginVersion: "1.4.0" });
    }

    [Fact]
    public async Task Restarting_steam_waits_for_the_game_to_end_answers_before_steam_exits_and_puts_the_plugin_in_before_the_start()
    {
        _steam.Start(7);
        _steam.Change(_steam.Session with { RunningGame = Game });
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status is { RestartNeeded: true, RunningGame: not null });

        _service.RestartSteam().Should().Be(SteamRestartResult.GameRunning);
        _steam.Opened.Should().BeEmpty();

        _steam.Change(_steam.Session with { RunningGame = null });
        await Eventually.HoldsAsync(() => _service.Status.RunningGame is null);
        _service.RestartSteam().Should().Be(SteamRestartResult.Restarting);
        _service.RestartSteam().Should().Be(SteamRestartResult.Restarting);
        _steam.Opened.Should().Equal(["steam://exit"], "the window's request is answered while Steam closes, and a second one joins the first");

        _steam.Exit(7);

        await Eventually.HoldsAsync(() => _steam.Opened.Count == 2);
        _steam.Opened.Should().Equal("steam://exit", "steam://open/main");
        File.Exists(Path.Combine(PluginFolder, "plugin.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Only_a_report_of_this_run_of_steam_tells_what_the_plugin_hooked_and_a_game_it_never_reported_stays_unknown()
    {
        Start(SteamUiDemand.From([], [Game]));
        await Eventually.HoldsAsync(() => _service.Status.PluginVersion == "1.4.0");
        Report("""{"hooks": {"achievements": true, "notifications": true}, "applied": 0, "launchOptions": {}, "millennium": "3.6.2"}""");
        File.SetLastWriteTimeUtc(Path.Combine(PluginFolder, "status.json"), DateTime.UtcNow.AddHours(-1));

        _steam.Start(7);
        await Eventually.HoldsAsync(() => _service.Status.InterfaceLanguage == "polish");
        _service.Status.Should().Match<SteamUiStatus>(status => !status.Reachable && status.Hooks == null && status.LaunchOptions.Count == 0,
            "Millennium has not loaded the plugin in this run yet, and the player's options of the game were never read");

        Report("""{"hooks": {"achievements": true, "notifications": false}, "applied": 0, "launchOptions": {"400": "-novid"}, "millennium": "3.6.3"}""");
        await Eventually.HoldsAsync(() => _service.Status.Reachable);
        _service.Status.Should().Match<SteamUiStatus>(status => status.LaunchOptions[Game] == "-novid"
            && status.Hooks == new SteamUiHooks(true, false) && status.MillenniumVersion == "3.6.3");
    }

    [Fact]
    public async Task A_plugin_millennium_did_not_load_is_told_as_blocked_once_it_had_its_time()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var service = new SteamUiService(_steam, new FakeDevTools(), _folders, _settings, Options, time, NullLogger<SteamUiService>.Instance);
        service.Demand(SteamUiDemand.From([Texts], []));
        _run = service.RunAsync(new LaunchArguments(ProcessRole.Agent, Background: false, Target: null), _stop.Token);
        await Eventually.HoldsAsync(() => service.Status.PluginVersion == "1.4.0");

        _steam.Start(7);
        await Eventually.HoldsAsync(() => service.Status.InterfaceLanguage == "polish");
        service.Status.Hooks.Should().BeNull("the plugin may still load");
        time.Advance(TimeSpan.FromMinutes(2));

        await Eventually.HoldsAsync(() => service.Status.Hooks == new SteamUiHooks(false, false));
        service.Status.Reachable.Should().BeFalse();
    }

    [Fact]
    public async Task A_plugin_the_player_turned_off_in_millennium_stays_off_and_needs_no_restart()
    {
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.PluginVersion == "1.4.0");
        await File.WriteAllTextAsync(ConfigFile, """{"plugins": {"enabledPlugins": ["another-plugin"]}}""", AgentHarness.Token);

        _steam.Start(7);
        await Eventually.HoldsAsync(() => _service.Status.PluginDisabled);
        _service.Status.RestartNeeded.Should().BeFalse("a new start of Steam would not load it either");
        _steam.Exit(7);
        _service.Demand(SteamUiDemand.From([Texts], [Game]));
        await Eventually.HoldsAsync(() =>
        {
            using var texts = TextsFile();
            return texts.RootElement.GetProperty("launchOptions").GetProperty("games").GetArrayLength() == 1;
        });

        _service.Status.PluginDisabled.Should().BeTrue();
        File.ReadAllText(ConfigFile).Should().NotContain(Slug);
    }

    [Fact]
    public async Task The_plugin_goes_away_while_steam_is_closed_once_nothing_can_need_it()
    {
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.PluginVersion == "1.4.0");

        _settings.Update(settings => settings with { AchievementTexts = false });
        _service.Demand(SteamUiDemand.From([], []));

        // The folder goes first, then the plugin's name in Millennium's settings.
        await Eventually.HoldsAsync(() => !Directory.Exists(PluginFolder) && !File.ReadAllText(ConfigFile).Contains(Slug, StringComparison.Ordinal));
        await Eventually.HoldsAsync(() => _service.Status is { PluginVersion: "", IsNeeded: false, RestartNeeded: false });
    }

    [Fact]
    public async Task Changes_of_launch_options_wait_in_the_texts_file_until_the_plugin_reports_them_made()
    {
        Start(SteamUiDemand.From([], [Game]));
        await Eventually.HoldsAsync(() => _service.Status.PluginVersion == "1.4.0");
        (await _service.ChangeLaunchOptionsAsync(Game, ["-examplelog"], [], AgentHarness.Token)).Failure
            .Should().Be(LaunchOptionsFailure.Unreachable, "nothing reaches Steam while it is closed");
        _steam.Start(7);
        Report("""{"applied": 4, "launchOptions": {"400": "-novid"}}""");
        await Eventually.HoldsAsync(() => _service.Status.Reachable && _service.Status.LaunchOptions[Game] == "-novid");

        var result = await _service.ChangeLaunchOptionsAsync(Game, ["-examplelog"], ["-novid"], AgentHarness.Token);

        result.Should().Be(new LaunchOptionsResult("-examplelog"));
        _service.Status.LaunchOptions[Game].Should().Be("-examplelog", "the options hold the change before it returns");
        Changes().Should().Equal((5L, "400", "-examplelog", "-novid"));

        Report("""{"applied": 5, "launchOptions": {"400": "-examplelog"}}""");
        await Eventually.HoldsAsync(() => Changes().Count == 0);
        _service.Status.LaunchOptions[Game].Should().Be("-examplelog");
    }

    [Fact]
    public async Task Uninstalling_the_app_takes_its_plugin_and_its_share_of_the_switch_out_of_steam_s_folder()
    {
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.PluginVersion == "1.4.0");
        await _stop.CancelAsync();
        await _run.ContinueWith(_ => { }, TaskScheduler.Default);
        File.WriteAllLines(Path.Combine(_root, ".cef-enable-remote-debugging"), [ClassicSwitch.OwnerOf(Options.Identity), "AnotherApp"]);

        SteamCleanup.Remove(_root, Options.Identity);

        Directory.Exists(PluginFolder).Should().BeFalse();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "millennium", "config", "config.json")));
        config.RootElement.GetProperty("plugins").GetProperty("enabledPlugins").EnumerateArray().Select(name => name.GetString())
            .Should().Equal("another-plugin");
        File.ReadAllLines(Path.Combine(_root, ".cef-enable-remote-debugging")).Should().Equal("AnotherApp");
    }

    [Theory]
    [InlineData("", "-examplelog", "", "-examplelog")]
    [InlineData("-novid", "-examplelog -novid", "", "-novid -examplelog")]
    [InlineData("\"C:\\My  Tools\\x.exe\" %command% -examplelog", "", "-examplelog", "\"C:\\My  Tools\\x.exe\" %command%")]
    [InlineData("-examplelog  -novid", "", "-examplelog", "-novid")]
    [InlineData("-novid -examplelogs", "", "-examplelog", "-novid -examplelogs")]
    public void Options_change_as_the_script_in_steam_changes_them(string before, string add, string remove, string after) =>
        LaunchOptionsText.Changed(before, add.Split(' ', StringSplitOptions.RemoveEmptyEntries), remove.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Should().Be(after);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run.ContinueWith(_ => { }, TaskScheduler.Default);
        _service.Dispose();
        Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
    }

    private void Start(SteamUiDemand demand)
    {
        _service.Demand(demand);
        _run = _service.RunAsync(new LaunchArguments(ProcessRole.Agent, Background: false, Target: null), _stop.Token);
    }

    private JsonDocument TextsFile() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(PluginFolder, "texts.json")));

    private List<(long Id, string App, string Add, string Remove)> Changes()
    {
        using var texts = TextsFile();
        return [.. texts.RootElement.GetProperty("launchOptions").GetProperty("changes").EnumerateArray().Select(change => (
            change.GetProperty("id").GetInt64(), change.GetProperty("app").GetString()!,
            string.Join(' ', change.GetProperty("add").EnumerateArray().Select(option => option.GetString())),
            string.Join(' ', change.GetProperty("remove").EnumerateArray().Select(option => option.GetString()))))];
    }

    private void Report(string status)
    {
        File.WriteAllText(Path.Combine(PluginFolder, "status.json"), status);
        _folders.Report(PluginFolder);
    }
}
