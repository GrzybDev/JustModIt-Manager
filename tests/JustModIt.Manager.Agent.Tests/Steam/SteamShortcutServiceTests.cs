using System.Globalization;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class SteamShortcutServiceTests : IAsyncDisposable
{
    // Steam ids of two invented accounts, whose folders in userdata Steam names by the account's part of the id.
    private const string FirstAccount = "76561198000000001";
    private const string SecondAccount = "76561198000000002";

    private readonly CancellationTokenSource _stop = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSteamClient _steam = new();
    private readonly FakeFolderWatcher _folders = new();
    private readonly AgentStatus _status;
    private Task _run = Task.CompletedTask;

    public SteamShortcutServiceTests()
    {
        Directory.CreateDirectory(ClientFolder);
        Directory.CreateDirectory(Path.Combine(Root, "config"));
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutsFile(FirstAccount))!);
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutsFile(SecondAccount))!);
        var session = Path.Combine(DataFolder, "wayland-sessions", "gamescope-wayland.desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(session)!);
        File.WriteAllText(session, "");
        _status = new AgentStatus(Options);
    }

    private string Home => Path.Combine(_folder, "home");

    private string ClientFolder => Path.Combine(Home, ".steam");

    private string Root => Path.Combine(Home, ".local", "share", "Steam");

    private string DataFolder => Path.Combine(_folder, "usr", "share");

    private string MenuEntry => Path.Combine(Home, ".local", "share", "applications", "com.example.ExampleApp.desktop");

    private LibraryEnvironment Environment => new() { Platform = AppPlatform.Linux, HomeDirectory = Home, SystemDataFolders = [DataFolder] };

    private AgentOptions Options => new()
    {
        Identity = new AppIdentity("ExampleApp", "ExampleVendor", "example-app", "com.example.ExampleApp", isDevelopmentBuild: false),
        ProductName = "Example App",
        Version = "1.0.0",
        Locations = new AppDataLocations(_folder, _folder, _folder),
        Names = InstanceNames.ForRuntimeDirectory("ExampleApp", _folder),
        Api = new CatalogueApiOptions(new Uri("https://api.example.com/"), null, "ExampleApp", "1.0.0"),
        PackageExtension = ".examplemod",
        ExecutablePath = Path.Combine(Home, "Applications", "ExampleApp.AppImage"),
        InstallDirectory = null,
        SteamShortcutTarget = MenuEntry,
    };

    [Fact]
    public async Task Adding_hands_Steam_the_menu_entry_through_its_gate_and_waits_for_a_shortcut_to_the_entrys_program()
    {
        await using var agent = new AgentHarness { Configure = services => services.AddSingleton(Environment) };
        agent.Options = agent.Options with { SteamShortcutTarget = MenuEntry };
        agent.Steam.Root = Root;
        SignIn(SecondAccount);
        agent.Start();
        var window = await agent.ConnectWindowAsync();

        var closed = await window.StateAsync(state => state.SteamShortcut is { State: SteamShortcutState.SteamNotRunning });
        closed.SteamShortcut.Should().Be(new SteamShortcutView(SteamShortcutState.SteamNotRunning, HasGameSession: true,
            agent.Options.ExecutablePath));
        await window.RequestAsync(new AddToSteam());
        agent.Steam.Added.Should().BeEmpty("a closed Steam takes the link only after its sign-in, if at all");

        StartSteam(agent.Steam, 4100);
        await window.StateAsync(state => state.SteamShortcut is { State: SteamShortcutState.Missing });
        await window.RequestAsync(new AddToSteam());
        agent.Steam.Added.Should().Equal(MenuEntry);
        agent.Steam.IsGateOpen.Should().BeTrue();
        await window.StateAsync(state => state.SteamShortcut is { State: SteamShortcutState.Adding });

        // Steam names the shortcut after the entry and keeps the entry's program, not the entry.
        WriteShortcuts(SecondAccount, ("Example App", $"\"{agent.Options.ExecutablePath}\""));
        await window.StateAsync(state => state.SteamShortcut is { State: SteamShortcutState.Added });
        agent.Steam.IsGateOpen.Should().BeFalse("no page may add a program to Steam once the shortcut is there");
        await window.RequestAsync(new AddToSteam());
        agent.Steam.Added.Should().ContainSingle("the app is in Steam's library already");
        agent.Steam.Waits.Should().Be(1, "the agent's services wait for Steam's exit together");
    }

    [Fact]
    public async Task A_shortcut_that_does_not_appear_within_30_seconds_fails_and_one_added_later_still_counts()
    {
        var timers = new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
        var time = new SteppedClock(timers);
        _steam.Root = Root;
        SignIn(FirstAccount);
        StartSteam(_steam, 4100);
        var service = Start(time);
        await StateAsync(SteamShortcutState.Missing);

        await service.AddAsync();
        await StateAsync(SteamShortcutState.Adding);
        // The system sets its clock back meanwhile, as after a wake, which holds no limit off.
        time.Back = TimeSpan.FromMinutes(1);
        timers.Advance(SteamShortcutView.AddTimeLimit);
        await StateAsync(SteamShortcutState.Failed);
        _steam.IsGateOpen.Should().BeFalse("no page may add a program to Steam after the app's request ended");

        WriteShortcuts(FirstAccount, ("Example App", Options.ExecutablePath));
        _folders.Report(Path.GetDirectoryName(ShortcutsFile(FirstAccount))!);
        await StateAsync(SteamShortcutState.Added);
    }

    [Fact]
    public async Task The_shortcut_follows_Steam_restarting_and_the_account_changing_and_tells_a_missing_file_from_another_copy()
    {
        Start();
        await Eventually.HoldsAsync(() => _folders.IsWatching(ClientFolder));
        _status.SteamShortcut.Should().BeNull("only the Steam behind the home folder takes a shortcut of the app");

        var moved = Path.Combine(Home, "Downloads", "ExampleApp.AppImage");
        _steam.Root = Root;
        SignIn(FirstAccount);
        WriteShortcuts(FirstAccount, ("Tide Atlas", "/opt/tide-atlas/atlas"), ("ExampleApp.AppImage", $"\"{moved}\""));
        StartSteam(_steam, 4100);
        _folders.Report(ClientFolder);
        (await StateAsync(SteamShortcutState.Elsewhere)).Should().Match<SteamShortcutView>(shortcut => shortcut.Other == moved && shortcut.OtherMissing);

        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.WriteAllText(moved, "");
        _steam.Exit(4100);
        await StateAsync(SteamShortcutState.SteamNotRunning);
        StartSteam(_steam, 4200);
        _folders.Report(ClientFolder);
        (await StateAsync(SteamShortcutState.Elsewhere)).OtherMissing.Should().BeFalse("the shortcut starts another copy of the app");

        WriteShortcuts(SecondAccount, ("Example App", Options.ExecutablePath));
        SignIn(SecondAccount);
        _folders.Report(Path.Combine(Root, "config"));
        await StateAsync(SteamShortcutState.Added);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private SteamShortcutService Start(TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var service = new SteamShortcutService(_steam, new SteamProcess(_steam, time), PhysicalFileSystem.Instance, _folders, Environment,
            Options, _status, time, NullLogger<SteamShortcutService>.Instance);
        _run = service.RunAsync(new LaunchArguments(ProcessRole.Agent, Background: true, Target: null), _stop.Token);
        return service;
    }

    private async Task<SteamShortcutView> StateAsync(SteamShortcutState state)
    {
        await Eventually.HoldsAsync(() => _status.SteamShortcut?.State == state);
        return _status.SteamShortcut!;
    }

    // Steam's process runs before it writes its id, which a service woken by the file then finds running.
    private void StartSteam(FakeSteamClient steam, int processId)
    {
        steam.Start(processId);
        File.WriteAllText(Path.Combine(ClientFolder, "steam.pid"), $"{processId}\n");
    }

    private void SignIn(string account) => File.WriteAllText(Path.Combine(Root, "config", "loginusers.vdf"), $$"""
        "users"
        {
            "{{FirstAccount}}" { "AccountName" "lantern_keeper" "MostRecent" "{{(account == FirstAccount ? 1 : 0)}}" }
            "{{SecondAccount}}" { "AccountName" "tide_reader" "MostRecent" "{{(account == SecondAccount ? 1 : 0)}}" }
        }
        """);

    private void WriteShortcuts(string account, params (string Name, string Program)[] shortcuts)
    {
        var writer = new KeyValuesWriter().Object("shortcuts");
        foreach (var (index, (name, program)) in shortcuts.Index())
        {
            writer.Object(index.ToString(CultureInfo.InvariantCulture)).Text("AppName", name).Text("Exe", program).End();
        }

        File.WriteAllBytes(ShortcutsFile(account), writer.End().End().ToArray());
    }

    private string ShortcutsFile(string account) =>
        Path.Combine(Root, "userdata", account == FirstAccount ? "39734273" : "39734274", "config", "shortcuts.vdf");

    // A wall clock that the system may set back, while its timers run on.
    private sealed class SteppedClock(FakeTimeProvider timers) : TimeProvider
    {
        public TimeSpan Back { get; set; }

        public override DateTimeOffset GetUtcNow() => timers.GetUtcNow() - Back;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            timers.CreateTimer(callback, state, dueTime, period);
    }
}
