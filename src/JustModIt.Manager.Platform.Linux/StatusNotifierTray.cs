using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The tray icon of Linux desktops: a StatusNotifierItem with a dbusmenu menu, served on a connection of its own and
/// registered with the desktop's tray watcher whenever one appears, so a session without one, such as Game Mode, shows no
/// tray; events arrive on the connection's reading thread.
/// </summary>
public sealed partial class StatusNotifierTray : ITrayHost
{
    private const string Watcher = "org.kde.StatusNotifierWatcher";

    private readonly string _name = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
    private readonly DBusConnection _connection;
    private readonly Item _item;
    private readonly TrayMenu _menu;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _registered;

    /// <summary>Connects to the session's bus off the calling thread; the icon appears with the first <see cref="Show"/>.</summary>
    /// <param name="address">The session bus's address; null where the session has no bus.</param>
    /// <param name="desktopId">The app's desktop entry, by which the desktop knows the app.</param>
    /// <param name="title">The app's name.</param>
    /// <param name="iconThemes">The folder of icon themes that holds the app's icon by its desktop entry's name; null for no icon.</param>
    /// <param name="logger">Hears of a session without a bus and of a watcher that refused the icon.</param>
    public StatusNotifierTray(string? address, string desktopId, string title, string? iconThemes, ILogger<StatusNotifierTray> logger)
    {
        // Without an address the connection fails to connect, which the tray takes for a session without a bus.
        _connection = new DBusConnection(address ?? "");
        _item = new Item(this, desktopId, title, iconThemes);
        _menu = new TrayMenu(_connection, command => CommandInvoked?.Invoke(this, command));
        _logger = logger;
        _ = Task.Run(FollowAsync);
    }

    public event EventHandler? Activated;

    public event EventHandler<string>? CommandInvoked;

    // systemd stops the agent with SIGTERM when the session ends, which the agent hears itself.
    public event EventHandler? SessionEnding
    {
        add { }
        remove { }
    }

    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
        var tooltipChanged = _item.SetToolTip(tooltip);
        var revision = _menu.Update(menu);
        _shown.TrySetResult();
        // A desktop that has not read the icon yet reads all of it once it is registered.
        if (_registered)
        {
            if (tooltipChanged)
            {
                _connection.EmitNewToolTip(Item.ObjectPath);
            }

            if (revision is { } changed)
            {
                _connection.EmitLayoutUpdated(TrayMenu.ObjectPath, changed, parent: 0);
            }
        }
    }

    // Closing the connection takes the icon off every desktop that shows it.
    public void Dispose()
    {
        _stop.Cancel();
        _connection.Dispose();
    }

    private async Task FollowAsync()
    {
        try
        {
            await _connection.ConnectAsync().ConfigureAwait(false);
            _connection.AddMethodHandlers([_item, _menu]);
            await _connection.RequestNameAsync(_name).ConfigureAwait(false);
            await _shown.Task.WaitAsync(_stop.Token).ConfigureAwait(false);
            var watcher = new StatusNotifierWatcher(_connection, Watcher, "/StatusNotifierWatcher");
            using var owner = await _connection.WatchNameOwnerAsync(Watcher).ConfigureAwait(false);
            while (true)
            {
                var current = await owner.WaitForOwnerAsync(_stop.Token).ConfigureAwait(false);
                await RegisterAsync(watcher).ConfigureAwait(false);
                // A watcher that comes back, as when the desktop's shell restarts, knows no icon until each registers again.
                using var gone = CancellationTokenSource.CreateLinkedTokenSource(owner.GetOwnerChangedCancellationToken(current), _stop.Token);
                await Task.Delay(Timeout.Infinite, gone.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                _registered = false;
            }
        }
        // Closing the tray ends its connection and every wait on it.
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        // Any other failure, such as a session without a bus, means no desktop will show the icon.
        catch (Exception exception)
        {
            LogNoTray(exception);
        }
    }

    private async Task RegisterAsync(StatusNotifierWatcher watcher)
    {
        // Set before the call, since the desktop reads the icon before the watcher replies, and a change in between must
        // reach it; a change signalled to nobody does no harm.
        _registered = true;
        try
        {
            await watcher.RegisterStatusNotifierItemAsync(_name).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException exception)
        {
            _registered = false;
            LogNotRegistered(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The session has no bus to show the tray icon on")]
    private partial void LogNoTray(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The desktop's tray watcher did not take the tray icon")]
    private partial void LogNotRegistered(Exception exception);

    /// <summary>The icon as StatusNotifierItem hosts read it.</summary>
    private sealed class Item : DBusHandler, IStatusNotifierItemHandler, IStatusNotifierItemProperties
    {
        public const string ObjectPath = "/StatusNotifierItem";

        private readonly StatusNotifierTray _tray;
        private volatile string _toolTip = "";

        public Item(StatusNotifierTray tray, string desktopId, string title, string? iconThemes)
            : base(tray._connection, ObjectPath, handlesChildPaths: false, handleOnCapturedContext: false)
        {
            _tray = tray;
            Id = desktopId;
            Title = title;
            IconName = iconThemes is null ? "" : desktopId;
            IconThemePath = iconThemes ?? "";
        }

        public string Category => "ApplicationStatus";

        public string Id { get; }

        public string Title { get; }

        public string Status => "Active";

        public int WindowId => 0;

        public string IconThemePath { get; }

        public string IconName { get; }

        public (string, (int, int, byte[])[], string, string) ToolTip => (IconName, [], _toolTip, "");

        // A click on the icon opens the window and the menu offers the rest, as on Windows.
        public bool ItemIsMenu => false;

        public ObjectPath Menu => TrayMenu.ObjectPath;

        /// <summary>Takes the tooltip's text.</summary>
        /// <param name="toolTip">The product name and the status.</param>
        /// <returns>Whether it differs from the one before.</returns>
        public bool SetToolTip(string toolTip)
        {
            var changed = toolTip != _toolTip;
            _toolTip = toolTip;
            return changed;
        }

        public ValueTask HandleGetPropertyAsync(IStatusNotifierItemHandler.GetPropertyContext context) => context.Handle(this);

        public ValueTask HandleGetAllPropertiesAsync(IStatusNotifierItemHandler.GetAllPropertiesContext context) => context.Handle(this);

        public ValueTask ActivateAsync(int x, int y)
        {
            _tray.Activated?.Invoke(_tray, EventArgs.Empty);
            return default;
        }

        // The menu is served apart, which hosts open themselves.
        public ValueTask ContextMenuAsync(int x, int y) => default;

        public ValueTask SecondaryActivateAsync(int x, int y) => default;

        public ValueTask ScrollAsync(int delta, string orientation) => default;

        public ValueTask ProvideXdgActivationTokenAsync(string token) => default;
    }
}
