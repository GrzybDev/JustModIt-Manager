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

public sealed class ClassicModeTests : IAsyncDisposable
{
    private const string Owner = "ExampleApp";

    private static readonly GameKey Game = new(StoreId.Steam, "400");

    private static readonly AchievementTexts Texts = new()
    {
        SteamAppId = 400,
        Language = "polish",
        Revision = 1,
        Achievements = [new AchievementText { ApiName = "FIRST", Name = "Pierwsze kroki", Description = "Ukończ samouczek" }],
    };

    private readonly CancellationTokenSource _stop = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"), "Steam");
    private readonly FakeSteamClient _steam = new();
    private readonly FakeDevTools _port = new();
    private readonly FakeSettings _settings = new(AppSettings.Default with { OnboardingCompleted = true });
    private readonly AppIdentity _identity = new(Owner, "ExampleVendor", "example-app", "com.example.ExampleApp", isDevelopmentBuild: false);
    private readonly SteamUiService _service;
    private Task _run = Task.CompletedTask;

    public ClassicModeTests()
    {
        Directory.CreateDirectory(_root);
        _steam.Root = _root;
        var options = new AgentOptions
        {
            Identity = _identity,
            ProductName = "Example App",
            Version = "1.4.0",
            Locations = new AppDataLocations(_root, _root, _root),
            Names = InstanceNames.ForRuntimeDirectory(Owner, _root),
            Api = new CatalogueApiOptions(new Uri("https://api.example.com/"), null, Owner, "1.4.0"),
            PackageExtension = ".examplemod",
            ExecutablePath = Path.Combine(_root, "ExampleApp.exe"),
            InstallDirectory = _root,
        };
        _service = new SteamUiService(_steam, new SteamProcess(_steam, TimeProvider.System), _port, new FakeFolderWatcher(), _settings,
            options, TimeProvider.System, NullLogger<SteamUiService>.Instance);
    }

    private string SwitchFile => Path.Combine(_root, ".cef-enable-remote-debugging");

    // Each second goes by on its own, so every try the connection waits for runs before the next one is due.
    private static async Task AdvanceAsync(FakeTimeProvider time, TimeSpan span)
    {
        for (var passed = TimeSpan.Zero; passed < span; passed += TimeSpan.FromSeconds(1))
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, AgentHarness.Token);
        }
    }

    [Fact]
    public async Task Steam_s_interface_gets_the_texts_and_reports_the_launch_options_over_the_port_steam_opened_at_its_start()
    {
        _port.LaunchOptions["400"] = "-novid";
        Start(SteamUiDemand.From([Texts], [Game]));
        await Eventually.HoldsAsync(() => File.Exists(SwitchFile));
        _service.Status.Should().Match<SteamUiStatus>(status => status.Delivery == SteamUiDelivery.Classic && !status.Reachable);

        _steam.Start(7);

        await Eventually.HoldsAsync(() => _service.Status is { Reachable: true, Hooks.Achievements: true });
        _service.Status.LaunchOptions[Game].Should().Be("-novid");
        _port.Page!.Texts.Should().Be("""[{"400":{"FIRST":["Pierwsze kroki","Ukończ samouczek"]}},"polish"]""");

        _port.Page.Edit("400", "-novid -windowed");
        await Eventually.HoldsAsync(() => _service.Status.LaunchOptions[Game] == "-novid -windowed");

        var result = await _service.ChangeLaunchOptionsAsync(Game, ["-examplelog"], ["-windowed"], AgentHarness.Token);
        result.Should().Be(new LaunchOptionsResult("-novid -examplelog"));
        _service.Status.LaunchOptions[Game].Should().Be("-novid -examplelog", "the options hold the change before it returns");
        _port.LaunchOptions["400"].Should().Be("-novid -examplelog");
    }

    [Fact]
    public async Task A_reload_of_steam_s_interface_gets_everything_again_and_a_lost_connection_is_made_again()
    {
        _steam.Start(7);
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.Reachable);
        var page = _port.Page!;

        page.Reload();
        await Eventually.HoldsAsync(() => page.HookRuns == 2);

        page.Close();
        await Eventually.HoldsAsync(() => _port.Connections == 2 && _port.Page!.Texts is not null);
        _port.Page!.Texts.Should().Contain("Pierwsze kroki");
        await Eventually.HoldsAsync(() => _service.Status.Reachable);
    }

    [Fact]
    public async Task A_steam_that_started_before_the_switch_needs_a_restart_and_a_taken_port_is_told_apart()
    {
        _port.Answer = DevToolsAnswer.NotListening;
        _steam.Start(7);
        Start(SteamUiDemand.From([Texts], []));

        await Eventually.HoldsAsync(() => _service.Status.RestartNeeded);
        _port.Connections.Should().Be(1, "a Steam that started without the switch has no port to wait for");

        _port.Answer = DevToolsAnswer.Taken;
        _steam.Exit(7);
        _steam.Start(8);
        await Eventually.HoldsAsync(() => _service.Status is { PortTaken: true, RestartNeeded: false });
    }

    [Fact]
    public async Task A_switch_in_place_stays_until_the_first_demand_and_a_steam_that_started_with_it_is_waited_for()
    {
        await File.WriteAllTextAsync(SwitchFile, ClassicSwitch.OwnerOf(_identity) + "\n", AgentHarness.Token);
        _port.Answer = DevToolsAnswer.NotListening;
        _steam.Start(7);
        _run = _service.RunAsync(new LaunchArguments(ProcessRole.Agent, Background: false, Target: null), _stop.Token);
        await Eventually.HoldsAsync(() => _service.Status.Delivery == SteamUiDelivery.Classic);
        File.Exists(SwitchFile).Should().BeTrue("nothing has told yet that the texts are off");

        _service.Demand(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _port.Connections == 1);
        _port.Answer = DevToolsAnswer.Connected;

        // Steam opens its port a while after its start.
        await Eventually.HoldsAsync(() => _service.Status.Reachable);
        _service.Status.RestartNeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Texts_no_longer_needed_leave_steam_s_interface_at_once()
    {
        _steam.Start(7);
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.Reachable);

        _service.Demand(SteamUiDemand.None);

        await Eventually.HoldsAsync(() => _port.Page!.Texts == """[{},""]""");
    }

    [Fact]
    public async Task A_steam_that_opens_its_port_minutes_after_its_start_is_still_reached()
    {
        var time = new FakeTimeProvider();
        _port.Answer = DevToolsAnswer.NotListening;
        await using var connection = new ClassicConnection(_port, 7, patient: true, "__launchOptionsOf_ExampleApp", time, () => { },
            NullLogger.Instance);

        await AdvanceAsync(time, TimeSpan.FromSeconds(90));
        connection.GaveUp.Should().BeFalse();
        _port.Answer = DevToolsAnswer.Connected;
        await AdvanceAsync(time, TimeSpan.FromSeconds(120));

        await Eventually.HoldsAsync(() => connection.IsConnected);
    }

    [Fact]
    public async Task The_switch_goes_once_nothing_needs_it_but_one_the_player_or_decky_keeps_stays()
    {
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => File.Exists(SwitchFile));

        _service.Demand(SteamUiDemand.None);
        await Eventually.HoldsAsync(() => !File.Exists(SwitchFile));

        await File.WriteAllTextAsync(SwitchFile, "", AgentHarness.Token);
        _service.Demand(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => _service.Status.Delivery == SteamUiDelivery.Classic);
        _settings.Update(settings => settings with { AchievementClassicMode = false });
        await Eventually.HoldsAsync(() => _service.Status.Delivery == SteamUiDelivery.None);
        File.Exists(SwitchFile).Should().BeTrue();

        // Decky Loader's installer only touches the switch the app wrote, and its loader needs the port as long as it is there.
        File.Delete(SwitchFile);
        _settings.Update(settings => settings with { AchievementClassicMode = true });
        await Eventually.HoldsAsync(() => File.Exists(SwitchFile));
        _steam.DeckyLoader = Path.Combine(_root, "PluginLoader");
        await File.WriteAllTextAsync(_steam.DeckyLoader, "", AgentHarness.Token);
        _service.Demand(SteamUiDemand.None);
        await Eventually.HoldsAsync(() => File.Exists(SwitchFile) && File.ReadAllText(SwitchFile).Length == 0);
    }

    [Fact]
    public async Task Copies_of_the_app_in_any_windows_account_share_the_switch_and_the_last_one_takes_it_away()
    {
        const string OtherAccount = "ExampleApp.S-1-5-21-1000-1000-1000-1001";
        ClassicSwitch.TurnOn(_root, OtherAccount).Should().BeTrue();
        Start(SteamUiDemand.From([Texts], []));
        await Eventually.HoldsAsync(() => File.ReadAllLines(SwitchFile).Length == 2);
        File.ReadAllLines(SwitchFile)[1].Should().EndWith(InstanceNames.CurrentUserId(), "the line names the Windows account too");

        _service.Demand(SteamUiDemand.None);
        await Eventually.HoldsAsync(() => File.ReadAllLines(SwitchFile).Length == 1);
        File.ReadAllLines(SwitchFile).Should().Equal(OtherAccount);
        ClassicSwitch.TurnOff(_steam.Find()!, OtherAccount);
        File.Exists(SwitchFile).Should().BeFalse();
    }

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
}
