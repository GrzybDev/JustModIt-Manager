using System.Collections.Concurrent;
using JustModIt.Manager.Platform.Linux.Tests.DBus;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>The user's systemd manager on a private bus, answering what a test sets and recording what it was asked.</summary>
internal sealed class FakeSystemd : DBusHandler, IManagerHandler
{
    private bool _enabled;

    private FakeSystemd(DBusConnection connection)
        : base(connection, "/org/freedesktop/systemd1", handlesChildPaths: false, handleOnCapturedContext: false)
    {
    }

    /// <summary>The calls in the order they came, each its method and unit, such as "EnableUnitFiles example.service".</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>What <c>GetUnitFileState</c> answers.</summary>
    public string State { get; set; } = "static";

    /// <summary>The error every call answers with; null answers.</summary>
    public string? Error { get; set; }

    /// <summary>Takes systemd's name on the bus and answers on it.</summary>
    /// <param name="bus">The test's bus.</param>
    public static async Task<FakeSystemd> StartAsync(PrivateBus bus)
    {
        var connection = await bus.ConnectAsync();
        var systemd = new FakeSystemd(connection);
        connection.AddMethodHandler(systemd);
        await connection.RequestNameAsync("org.freedesktop.systemd1");
        await PrivateBus.LeaveReaderAsync();
        return systemd;
    }

    public ValueTask ReloadAsync()
    {
        Record("Reload");
        return default;
    }

    public ValueTask<ObjectPath> StartUnitAsync(string name, string mode) => Job($"StartUnit {name} {mode}", name);

    public ValueTask<ObjectPath> RestartUnitAsync(string name, string mode) => Job($"RestartUnit {name} {mode}", name);

    public ValueTask ResetFailedUnitAsync(string name)
    {
        Record($"ResetFailedUnit {name}");
        return default;
    }

    public ValueTask<string> GetUnitFileStateAsync(string file)
    {
        Record($"GetUnitFileState {file}");
        return ValueTask.FromResult(State);
    }

    // systemd reports the links it made, none when the unit was enabled already.
    public ValueTask<(bool CarriesInstallInfo, (string, string, string)[] Changes)> EnableUnitFilesAsync(string[] files, bool runtime,
        bool force)
    {
        Record($"EnableUnitFiles {string.Join(' ', files)}");
        (string, string, string)[] changes = _enabled ? [] : [("symlink", "default.target.wants", files[0])];
        _enabled = true;
        return ValueTask.FromResult((true, changes));
    }

    public ValueTask<(string, string, string)[]> DisableUnitFilesAsync(string[] files, bool runtime)
    {
        Record($"DisableUnitFiles {string.Join(' ', files)}");
        (string, string, string)[] changes = _enabled ? [("unlink", "default.target.wants", files[0])] : [];
        _enabled = false;
        return ValueTask.FromResult(changes);
    }

    private ValueTask<ObjectPath> Job(string call, string name)
    {
        Record(call);
        return State == "masked"
            ? throw new DBusErrorReplyException("org.freedesktop.systemd1.UnitMasked", $"Unit {name} is masked.")
            : ValueTask.FromResult(new ObjectPath("/org/freedesktop/systemd1/job/1"));
    }

    private void Record(string call)
    {
        if (Error is { } error)
        {
            throw new DBusErrorReplyException(error, "The test's systemd refuses.");
        }

        Calls.Enqueue(call);
    }
}
