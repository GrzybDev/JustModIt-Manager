using System.Collections.Concurrent;
using JustModIt.Manager.Platform.Linux.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class DesktopNotificationsTests
{
    private const string DesktopId = "com.example.ExampleApp";
    private const string Icon = "/home/player/.cache/ExampleApp/icons/hicolor/256x256/apps/com.example.ExampleApp.png";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Notifications_show_through_the_sessions_server_and_their_clicks_reach_the_app_where_the_server_offers_actions()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        using var notifications = new DesktopNotifications("Example App", DesktopId, "Open Example App", Icon, bus.Shared(),
            NullLogger<DesktopNotifications>.Instance);
        var clicks = new ConcurrentQueue<string?>();
        notifications.Clicked += (_, tag) => clicks.Enqueue(tag);

        notifications.Notify("Title", "No server is on the bus.", tag: null).Should().BeFalse();

        var desktop = await FakeNotificationServer.StartAsync(bus, "actions", "body-markup", "persistence");
        await Eventually.HoldsAsync(() => notifications.IsClickable);
        notifications.Notify("Installed for Lantern & Key", "Lantern & Key <3> is ready.", "example-app://game/steam/10").Should().BeTrue();
        var installed = await desktop.Shown.Reader.ReadAsync(Token);
        installed.Should().BeEquivalentTo(new ShownNotification(installed.Id, "Example App", "file://" + Icon, "Installed for Lantern & Key",
                "Lantern &amp; Key &lt;3&gt; is ready.", ["default", "Open Example App"], DesktopId),
            "a server that reads markup in the text gets it escaped, and the title is never markup");
        notifications.Notify("Example App keeps running", "Its window opens from the tray.", "example-app://open").Should().BeTrue();
        var closed = await desktop.Shown.Reader.ReadAsync(Token);

        desktop.Click(installed.Id);
        desktop.Close(closed.Id);
        desktop.Click(closed.Id);
        desktop.Click(installed.Id);
        await Eventually.HoldsAsync(() => clicks.Count == 2);
        clicks.Should().Equal(["example-app://game/steam/10", "example-app://game/steam/10"], "a closed notification is forgotten");

        await desktop.LeaveAsync();
        await Eventually.HoldsAsync(() => !notifications.IsClickable);
        notifications.Notify("Title", "The server left the bus.", tag: null).Should().BeFalse();

        // Steam's server in Game Mode offers nothing, so it takes the text as it is and no click reaches the app.
        var steam = await FakeNotificationServer.StartAsync(bus);
        await Eventually.HoldsAsync(() => notifications.Notify("Updated Lantern & Key", "Version 2 <beta>", tag: null));
        var updated = await steam.Shown.Reader.ReadAsync(Token);
        updated.Body.Should().Be("Version 2 <beta>");
        updated.Actions.Should().BeEmpty();
        notifications.IsClickable.Should().BeFalse();
    }
}
