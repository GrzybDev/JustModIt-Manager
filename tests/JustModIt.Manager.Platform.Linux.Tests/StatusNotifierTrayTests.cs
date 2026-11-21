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
        using var tray = new StatusNotifierTray(bus.Address, DesktopId, "Example App", IconThemes, NullLogger<StatusNotifierTray>.Instance);
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
    public void A_session_without_a_bus_shows_no_icon()
    {
        using var tray = new StatusNotifierTray(address: null, DesktopId, "Example App", IconThemes, NullLogger<StatusNotifierTray>.Instance);

        tray.Invoking(icon => icon.Show("Example App", [new TrayMenuItem("open", "Open Example App")])).Should().NotThrow();
    }

    private static (int Id, string Text)[] Entries(VariantValue[] children) =>
    [
        .. children.Select(child => (child.GetItem(0).GetInt32(), Describe(child.GetItem(1).GetDictionary<string, VariantValue>()))),
    ];

    private static string Describe(Dictionary<string, VariantValue> entry) => entry.TryGetValue("type", out var type)
        ? type.GetString()
        : entry["label"].GetString() + (entry["enabled"].GetBool() ? "" : " (disabled)");
}
