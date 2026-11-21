using System.Threading.Channels;
using JustModIt.Manager.Platform.Linux.Tests.DBus;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>A notification shown by <see cref="FakeNotificationServer"/>, as the app sent it.</summary>
internal sealed record ShownNotification(uint Id, string AppName, string Icon, string Summary, string Body, string[] Actions,
    string DesktopEntry);

/// <summary>The session's notification server on a private bus, offering what a test gives and recording what it was asked to show.</summary>
internal sealed class FakeNotificationServer : DBusHandler, INotificationsHandler
{
    private const string Name = "org.freedesktop.Notifications";
    private const string ServerPath = "/org/freedesktop/Notifications";

    private readonly string[] _capabilities;
    private uint _lastId;

    private FakeNotificationServer(DBusConnection connection, string[] capabilities)
        : base(connection, ServerPath, handlesChildPaths: false, handleOnCapturedContext: false) => _capabilities = capabilities;

    public Channel<ShownNotification> Shown { get; } = Channel.CreateUnbounded<ShownNotification>();

    /// <summary>Takes the server's name on the bus and answers on it.</summary>
    /// <param name="bus">The test's bus.</param>
    /// <param name="capabilities">What the server offers, such as actions.</param>
    public static async Task<FakeNotificationServer> StartAsync(PrivateBus bus, params string[] capabilities)
    {
        var connection = await bus.ConnectAsync();
        var server = new FakeNotificationServer(connection, capabilities);
        connection.AddMethodHandler(server);
        await connection.RequestNameAsync(Name);
        await PrivateBus.LeaveReaderAsync();
        return server;
    }

    public ValueTask<string[]> GetCapabilitiesAsync() => ValueTask.FromResult(_capabilities);

    public ValueTask<uint> NotifyAsync(string appName, uint replacesId, string appIcon, string summary, string body, string[] actions,
        Dictionary<string, VariantValue> hints, int expireTimeout)
    {
        var id = Interlocked.Increment(ref _lastId);
        Shown.Writer.TryWrite(new ShownNotification(id, appName, appIcon, summary, body, actions, hints["desktop-entry"].GetString()));
        return ValueTask.FromResult(id);
    }

    /// <summary>Reports a click on a notification, as the server does once the player clicks it.</summary>
    /// <param name="id">The notification.</param>
    public void Click(uint id) => Connection.EmitActionInvoked(ServerPath, id, "default");

    /// <summary>Reports a notification closed, as the server does once it expired or the player dismissed it.</summary>
    /// <param name="id">The notification.</param>
    public void Close(uint id) => Connection.EmitNotificationClosed(ServerPath, id, reason: 2);

    /// <summary>Leaves the bus, as a server does when its desktop ends.</summary>
    public async Task LeaveAsync()
    {
        await Connection.ReleaseNameAsync(Name);
        await PrivateBus.LeaveReaderAsync();
    }
}
