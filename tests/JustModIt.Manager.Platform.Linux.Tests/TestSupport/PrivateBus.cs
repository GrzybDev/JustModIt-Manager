using System.ComponentModel;
using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>A D-Bus daemon of the test's own, on which the app's services talk to fake peers instead of the user's session.</summary>
internal sealed class PrivateBus : IAsyncDisposable
{
    private readonly Process _daemon;
    private readonly string _address;
    private readonly List<DBusConnection> _connections = [];

    private PrivateBus(Process daemon, string address) => (_daemon, _address) = (daemon, address);

    /// <summary>Starts the daemon, or skips the test where there is none to start.</summary>
    /// <param name="cancellationToken">Stops waiting for the daemon's address.</param>
    public static async Task<PrivateBus> StartAsync(CancellationToken cancellationToken)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "D-Bus carries the desktop's services only on Linux.");
        var start = new ProcessStartInfo("dbus-daemon") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("--session");
        start.ArgumentList.Add("--nofork");
        start.ArgumentList.Add("--print-address");
        Process daemon;
        try
        {
            daemon = Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            Assert.Skip("The tests' own bus needs dbus-daemon, which this Linux lacks.");
            throw;
        }

        return new PrivateBus(daemon, (await daemon.StandardOutput.ReadLineAsync(cancellationToken))!);
    }

    /// <summary>Opens a connection of its own to the bus, as another process would.</summary>
    public async Task<DBusConnection> ConnectAsync()
    {
        var connection = new DBusConnection(_address);
        _connections.Add(connection);
        await connection.ConnectAsync();
        await LeaveReaderAsync();
        return connection;
    }

    /// <summary>
    /// Goes on off the thread that reads a connection's messages, on which a call's answer resumes the awaiting code: a
    /// test that then waits for an answer of that connection would block the one thread that could bring it.
    /// </summary>
    public static async Task LeaveReaderAsync() => await Task.Yield();

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }

        _daemon.Kill();
        await _daemon.WaitForExitAsync();
        _daemon.Dispose();
    }
}
