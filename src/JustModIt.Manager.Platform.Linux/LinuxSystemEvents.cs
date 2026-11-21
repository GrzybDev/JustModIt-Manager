using JustModIt.Manager.Core.Library.Watching;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Logind = JustModIt.Manager.Platform.Linux.DBus.Login.Manager;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// Resume from sleep and newly mounted drives, from logind's sleep signal and the mounts of the disks service (UDisks2) on
/// the system bus; a drive mounted without that service, such as one fstab names, is found by the periodic full scan.
/// </summary>
public sealed partial class LinuxSystemEvents : ISystemEvents, IDisposable
{
    private readonly DBusConnection _connection;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Starts listening off the calling thread.</summary>
    /// <param name="connection">The system bus.</param>
    /// <param name="logger">Hears of a system without a bus and of a listener that failed.</param>
    public LinuxSystemEvents(DBusConnection connection, ILogger<LinuxSystemEvents> logger)
    {
        _connection = connection;
        _logger = logger;
        _ = Task.Run(ListenAsync);
    }

    public event EventHandler? Resumed;

    public event EventHandler? VolumeArrived;

    public void Dispose() => _stop.Cancel();

    // The signal carries the filesystem's interface and its changed properties; an unmount leaves its mount points empty.
    private static bool Mounted(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        reader.ReadString();
        return reader.ReadDictionaryOfStringToVariantValue().TryGetValue("MountPoints", out var points) && points.Count > 0;
    }

    private async Task ListenAsync()
    {
        try
        {
            var logind = new Logind(_connection, "org.freedesktop.login1", "/org/freedesktop/login1");
            using var sleep = await logind.WatchPrepareForSleepAsync(sleeping => Raise(sleeping ? null : Resumed), emitOnCapturedContext: false)
                .ConfigureAwait(false);
            // Every drive is an object of its own, whose changes no proxy of one object hears. Only drives have the filesystem's
            // interface, so the rule names no path, which Tmds.DBus.Protocol 0.94.1 would spell in a way the bus refuses.
            using var mounts = await _connection.AddMatchAsync(new MatchRule
                {
                    Type = MessageType.Signal,
                    Sender = "org.freedesktop.UDisks2",
                    Interface = "org.freedesktop.DBus.Properties",
                    Member = "PropertiesChanged",
                    Arg0 = "org.freedesktop.UDisks2.Filesystem",
                },
                Mounted, (Notification<bool> change) => Raise(change is { HasValue: true, Value: true } ? VolumeArrived : null),
                emitOnCapturedContext: false).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, _stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        // Any failure, such as a system without a bus, leaves the periodic full scan to find what changed.
        catch (Exception exception)
        {
            LogNotListening(exception);
        }
    }

    // A listener that threw would end the connection that the app's other services share.
    private void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogListenerFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The system bus tells no resume and no new drive, so the periodic scan finds them")]
    private partial void LogNotListening(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of a system event failed")]
    private partial void LogListenerFailed(Exception exception);
}
