using System.Collections.Concurrent;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The desktop's notifications through the session's notification server, which a session may lack and which reports a
/// click only where it offers actions, as Steam's server in Game Mode does not.
/// </summary>
public sealed partial class DesktopNotifications : INotificationHost, IDisposable
{
    private const string Server = "org.freedesktop.Notifications";
    private const string DefaultAction = "default";

    private readonly string _appName;
    private readonly string _desktopId;
    private readonly string _openLabel;
    private readonly string _icon;
    private readonly DBusConnection _connection;
    private readonly Notifications _server;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<uint, string?> _tags = new();

    // What the server that owns the name now offers; null while no server does.
    private volatile IReadOnlySet<string>? _capabilities;

    /// <summary>Starts following the session's notification server off the calling thread; until it answers, none is shown.</summary>
    /// <param name="appName">The app's name, which the server shows with each notification.</param>
    /// <param name="desktopId">The app's desktop entry, by which the server knows the app.</param>
    /// <param name="openLabel">The words of the action a click runs, which some servers show as a button.</param>
    /// <param name="icon">The app's icon file; null for none.</param>
    /// <param name="connection">The session bus.</param>
    /// <param name="logger">Hears of a session without a bus and of notifications the server refused.</param>
    public DesktopNotifications(string appName, string desktopId, string openLabel, string? icon, DBusConnection connection,
        ILogger<DesktopNotifications> logger)
    {
        _appName = appName;
        _desktopId = desktopId;
        _openLabel = openLabel;
        _icon = icon is null ? "" : new Uri(icon).AbsoluteUri;
        _connection = connection;
        _server = new Notifications(connection, Server, "/org/freedesktop/Notifications");
        _logger = logger;
        _ = Task.Run(FollowAsync);
    }

    public event EventHandler<string?>? Clicked;

    public bool IsClickable => _capabilities?.Contains("actions") == true;

    public bool Notify(string title, string message, string? tag)
    {
        if (_capabilities is not { } capabilities)
        {
            return false;
        }

        _ = SendAsync(capabilities, title, message, tag);
        return true;
    }

    public void Dispose() => _stop.Cancel();

    // A server that reads markup in the text would drop or misread an ampersand or angle bracket in a game's name.
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private async Task SendAsync(IReadOnlySet<string> capabilities, string title, string message, string? tag)
    {
        var clickable = capabilities.Contains("actions");
        try
        {
            var id = await _server.NotifyAsync(_appName, 0, _icon, title,
                    capabilities.Contains("body-markup") ? Escape(message) : message,
                    clickable ? [DefaultAction, _openLabel] : [],
                    new Dictionary<string, VariantValue> { ["desktop-entry"] = _desktopId }, expireTimeout: -1)
                .ConfigureAwait(false);
            if (clickable)
            {
                _tags[id] = tag;
            }
        }
        catch (DBusExceptionBase exception)
        {
            LogNotShown(exception);
        }
    }

    private async Task FollowAsync()
    {
        try
        {
            // Watched before a notification can be shown, so no click on one is missed.
            using var clicks = await _server.WatchActionInvokedAsync(OnActionInvoked, emitOnCapturedContext: false).ConfigureAwait(false);
            using var closes = await _server.WatchNotificationClosedAsync(closed => _tags.TryRemove(closed.Id, out _),
                emitOnCapturedContext: false).ConfigureAwait(false);
            using var owner = await _connection.WatchNameOwnerAsync(Server).ConfigureAwait(false);
            while (true)
            {
                var current = await owner.WaitForOwnerAsync(_stop.Token).ConfigureAwait(false);
                _capabilities = await CapabilitiesAsync().ConfigureAwait(false);
                using var gone = CancellationTokenSource.CreateLinkedTokenSource(owner.GetOwnerChangedCancellationToken(current), _stop.Token);
                await Task.Delay(Timeout.Infinite, gone.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                // A new server numbers its notifications anew.
                _capabilities = null;
                _tags.Clear();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        // Any other failure, such as a session without a bus, means no server will answer, so none is shown.
        catch (Exception exception)
        {
            _capabilities = null;
            LogNoServer(exception);
        }
    }

    // A server that cannot tell what it offers still shows text.
    private async Task<IReadOnlySet<string>> CapabilitiesAsync()
    {
        try
        {
            return (await _server.GetCapabilitiesAsync().ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        }
        catch (DBusErrorReplyException)
        {
            return new HashSet<string>();
        }
    }

    private void OnActionInvoked((uint Id, string ActionKey) action)
    {
        if (action.ActionKey == DefaultAction && _tags.TryGetValue(action.Id, out var tag))
        {
            Clicked?.Invoke(this, tag);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The session has no notification server to ask, so no notification is shown")]
    private partial void LogNoServer(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification server did not show a notification")]
    private partial void LogNotShown(Exception exception);
}
