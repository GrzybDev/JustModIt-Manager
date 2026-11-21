using System.Threading.Channels;
using JustModIt.Manager.Platform.Linux.Tests.DBus;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>The desktop's tray watcher on a private bus, recording the icons that register with it.</summary>
internal sealed class FakeTrayWatcher : DBusHandler, IStatusNotifierWatcherHandler
{
    private const string Name = "org.kde.StatusNotifierWatcher";

    private FakeTrayWatcher(DBusConnection connection)
        : base(connection, "/StatusNotifierWatcher", handlesChildPaths: false, handleOnCapturedContext: false)
    {
    }

    /// <summary>The bus names of the icons, in the order they registered.</summary>
    public Channel<string> Registered { get; } = Channel.CreateUnbounded<string>();

    /// <summary>Takes the watcher's name on the bus and answers on it.</summary>
    /// <param name="bus">The test's bus.</param>
    public static async Task<FakeTrayWatcher> StartAsync(PrivateBus bus)
    {
        var connection = await bus.ConnectAsync();
        var watcher = new FakeTrayWatcher(connection);
        connection.AddMethodHandler(watcher);
        await connection.RequestNameAsync(Name);
        await PrivateBus.LeaveReaderAsync();
        return watcher;
    }

    public ValueTask RegisterStatusNotifierItemAsync(string service)
    {
        Registered.Writer.TryWrite(service);
        return default;
    }

    /// <summary>Leaves the bus, as the watcher does when the desktop's shell restarts.</summary>
    public async Task LeaveAsync()
    {
        await Connection.ReleaseNameAsync(Name);
        await PrivateBus.LeaveReaderAsync();
    }
}
