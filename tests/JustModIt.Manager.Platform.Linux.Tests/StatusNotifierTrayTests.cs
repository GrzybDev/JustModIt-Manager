using System.Collections.Concurrent;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.Tests.DBus;
using JustModIt.Manager.Platform.Linux.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class StatusNotifierTrayTests
{
    private const string DesktopId = "com.example.ExampleApp";
    private const string IconThemes = "/home/player/.cache/ExampleApp/icons";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_icon_registers_with_each_watcher_that_comes_and_serves_its_tooltip_menu_and_clicks()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        using var tray = new StatusNotifierTray(bus.Address, bus.Shared(), DesktopId, "Example App", IconThemes,
            NullLogger<StatusNotifierTray>.Instance);
        var activations = 0;
        var commands = new ConcurrentQueue<string>();
        tray.Activated += (_, _) => Interlocked.Increment(ref activations);
        tray.CommandInvoked += (_, command) => commands.Enqueue(command);
        var watcher = await FakeTrayWatcher.StartAsync(bus);

        tray.Show("Example App – Everything is up to date", [
            new TrayMenuItem("open", "Open Example App") { IsDefault = true },
            new TrayMenuItem("status", "Looking for_updates") { IsEnabled = false },
            TrayMenuItem.Separator,
            new TrayMenuItem("check", "Check now"),
            TrayMenuItem.Separator,
            new TrayMenuItem("exit", "Exit"),
        ]);

        var service = await watcher.Registered.Reader.ReadAsync(Token);
        var desktop = await bus.ConnectAsync();
        var item = new StatusNotifierItem(desktop, service, "/StatusNotifierItem");
        var properties = await item.GetPropertiesAsync();
        (properties.Id, properties.Title, properties.Category, properties.Status, properties.IconName, properties.IconThemePath,
                properties.ToolTip.Item3, properties.ItemIsMenu)
            .Should().Be((DesktopId, "Example App", "ApplicationStatus", "Active", DesktopId, IconThemes,
                "Example App – Everything is up to date", false));
        var menu = new dbusmenu(desktop, service, properties.Menu);
        var shown = Entries((await menu.GetLayoutAsync(0, -1, [])).Layout.Item3);
        shown.Select(entry => entry.Text).Should().Equal("Open Example App", "Looking for__updates (disabled)", "separator", "Check now",
            "separator", "Exit");

        await menu.EventAsync(shown[1].Id, "clicked", 0, 0);
        await menu.EventAsync(shown[3].Id, "clicked", 0, 0);
        await item.ActivateAsync(0, 0);
        await Eventually.HoldsAsync(() => activations == 1);
        commands.Should().Equal(["check"], "a disabled entry does nothing");

        var relaid = new TaskCompletionSource();
        using var layouts = await menu.WatchLayoutUpdatedAsync(_ => relaid.TrySetResult(), emitOnCapturedContext: false);
        tray.Show("Example App – An update is ready", [
            new TrayMenuItem("open", "Open Example App") { IsDefault = true },
            new TrayMenuItem("status", "An update is ready") { IsEnabled = false },
            TrayMenuItem.Separator,
            new TrayMenuItem("check", "Check now"),
            new TrayMenuItem("restart", "Restart to update"),
            TrayMenuItem.Separator,
            new TrayMenuItem("exit", "Exit"),
        ]);
        await relaid.Task.WaitAsync(Token);
        await PrivateBus.LeaveReaderAsync();
        var updated = Entries((await menu.GetLayoutAsync(0, -1, [])).Layout.Item3);
        updated.Select(entry => entry.Id).Should().ContainInOrder(shown[3].Id, shown[5].Id);
        (await item.GetToolTipAsync()).Item3.Should().Be("Example App – An update is ready");
        await menu.EventAsync(shown[5].Id, "clicked", 0, 0);
        await Eventually.HoldsAsync(() => commands.Count == 2);
        commands.Should().Equal(["check", "exit"], "a click on the menu drawn before the change picks the entry the player saw");

        await watcher.LeaveAsync();
        var restarted = await FakeTrayWatcher.StartAsync(bus);
        (await restarted.Registered.Reader.ReadAsync(Token)).Should().Be(service);
    }

    [Fact]
    public async Task The_window_comes_back_from_the_tray_while_a_host_draws_it_from_the_app_list_without_one_and_from_Steam_in_gamescope()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        var watcher = await FakeTrayWatcher.StartAsync(bus);
        var logind = await FakeLogind.StartAsync(bus, ("2", "KDE"), ("3", "gamescope"));
        // The first reads fail, as reads do while a mode switch stops the desktop's processes.
        var refused = Task.WhenAll(watcher.Reads.RefuseNextAsync(), logind.DisplayReads.RefuseNextAsync());
        using var tray = new StatusNotifierTray(bus.Address, bus.Shared(), DesktopId, "Example App", IconThemes,
            NullLogger<StatusNotifierTray>.Instance);
        var told = new ConcurrentQueue<WindowReturn>();
        tray.WindowReturnChanged += (_, _) => told.Enqueue(tray.WindowReturn);
        await refused.WaitAsync(Token);
        tray.WindowReturn.Should().Be(WindowReturn.Tray, "a read that failed keeps what was known");

        watcher.SetHost(registered: false);
        await Eventually.HoldsAsync(() => tray.WindowReturn == WindowReturn.AppMenu);
        logind.SwitchTo("3");
        await Eventually.HoldsAsync(() => tray.WindowReturn == WindowReturn.StoreLibrary);
        logind.SwitchTo("2");
        await Eventually.HoldsAsync(() => tray.WindowReturn == WindowReturn.AppMenu);
        watcher.SetHost(registered: true);
        await Eventually.HoldsAsync(() => tray.WindowReturn == WindowReturn.Tray);
        await watcher.LeaveAsync();
        await Eventually.HoldsAsync(() => tray.WindowReturn == WindowReturn.AppMenu);

        told.Should().Equal(WindowReturn.AppMenu, WindowReturn.StoreLibrary, WindowReturn.AppMenu, WindowReturn.Tray, WindowReturn.AppMenu);
    }

    [Fact]
    public async Task A_tray_started_in_gamescope_brings_the_window_back_from_Steam_from_its_start()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        await FakeLogind.StartAsync(bus, ("3", "gamescope"));

        using var tray = new StatusNotifierTray(bus.Address, bus.Shared(), DesktopId, "Example App", IconThemes,
            NullLogger<StatusNotifierTray>.Instance);

        tray.WindowReturn.Should().Be(WindowReturn.StoreLibrary, "the agent serves launches as soon as it starts, and Game Mode shows no window it starts");
    }

    [Fact]
    public async Task A_session_without_a_bus_shows_no_icon_and_brings_the_window_back_from_the_app_list()
    {
        // As the shared system bus, which connects on its first call, does where there is none.
        using var system = new DBusConnection(new DBusConnectionOptions("unix:path=/nonexistent") { AutoConnect = true });
        using var tray = new StatusNotifierTray(address: null, system, DesktopId, "Example App", IconThemes,
            NullLogger<StatusNotifierTray>.Instance);

        tray.Invoking(icon => icon.Show("Example App", [new TrayMenuItem("open", "Open Example App")])).Should().NotThrow();
        await Eventually.HoldsAsync(() => tray.WindowReturn == WindowReturn.AppMenu);
    }

    private static (int Id, string Text)[] Entries(VariantValue[] children) =>
    [
        .. children.Select(child => (child.GetItem(0).GetInt32(), Describe(child.GetItem(1).GetDictionary<string, VariantValue>()))),
    ];

    private static string Describe(Dictionary<string, VariantValue> entry) => entry.TryGetValue("type", out var type)
        ? type.GetString()
        : entry["label"].GetString() + (entry["enabled"].GetBool() ? "" : " (disabled)");
}
