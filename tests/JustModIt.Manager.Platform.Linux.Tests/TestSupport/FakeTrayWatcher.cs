using System.Threading.Channels;
using JustModIt.Manager.Platform.Linux.Tests.DBus;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>
/// The desktop's tray watcher on a private bus, recording the icons that register with it, with a host that draws them
/// until a test takes it away.
/// </summary>
internal sealed class FakeTrayWatcher : DBusHandler, IStatusNotifierWatcherHandler, IStatusNotifierWatcherProperties
{
    private const string Name = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";

    private FakeTrayWatcher(DBusConnection connection)
        : base(connection, WatcherPath, handlesChildPaths: false, handleOnCapturedContext: false)
    {
    }

    /// <summary>The bus names of the icons, in the order they registered.</summary>
    public Channel<string> Registered { get; } = Channel.CreateUnbounded<string>();

    public bool IsStatusNotifierHostRegistered { get; private set; } = true;

    /// <summary>Fails a read of the watcher's properties.</summary>
    public ReadRefusal Reads { get; } = new();

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

    public ValueTask HandleGetPropertyAsync(IStatusNotifierWatcherHandler.GetPropertyContext context)
    {
        Reads.ThrowIfRefused();
        return context.Handle(this);
    }

    public ValueTask HandleGetAllPropertiesAsync(IStatusNotifierWatcherHandler.GetAllPropertiesContext context) => context.Handle(this);

    /// <summary>Gives the watcher a host or takes it away, as a panel's tray does when it is added or removed, and tells so.</summary>
    /// <param name="registered">Whether a host draws the icons.</param>
    public void SetHost(bool registered)
    {
        IsStatusNotifierHostRegistered = registered;
        if (registered)
        {
            Connection.EmitStatusNotifierHostRegistered(WatcherPath);
        }
        else
        {
            Connection.EmitStatusNotifierHostUnregistered(WatcherPath);
        }
    }

    /// <summary>Leaves the bus, as the watcher does when the desktop's shell restarts.</summary>
    public async Task LeaveAsync()
    {
        await Connection.ReleaseNameAsync(Name);
        await PrivateBus.LeaveReaderAsync();
    }
}
