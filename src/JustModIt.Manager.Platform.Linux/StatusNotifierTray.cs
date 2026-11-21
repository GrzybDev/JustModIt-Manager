using System.Threading.Channels;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The tray icon of Linux desktops: a StatusNotifierItem with a dbusmenu menu, served on a connection of its own and
/// registered with the desktop's tray watcher whenever one appears, so a session without one, such as Game Mode, shows no
/// tray; events arrive on the connections' reading threads. The window comes back from the icon while the watcher has a
/// host that draws it, from Steam's library in gamescope's session, which logind names as the user's display session, and
/// otherwise from the system's list of apps.
/// </summary>
public sealed partial class StatusNotifierTray : ITrayHost
{
    private const string Watcher = "org.kde.StatusNotifierWatcher";

    private readonly string _name = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
    private readonly DBusConnection _connection;
    private readonly DBusConnection _system;
    private readonly Item _item;
    private readonly TrayMenu _menu;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A signal only asks for another look, which reads the newest state, so the signals that come before it make one look.
    private readonly Channel<bool> _hostsChanged = OneLook();
    private readonly Channel<bool> _sessionsChanged = OneLook();
    private volatile bool _registered;

    // Most desktops have a tray, so the window's texts start with it until the watcher tells.
    private bool _hasHost = true;
    private bool _inGamescope;

    /// <summary>
    /// Reads whether the user's display session is gamescope's and connects to the buses off the calling thread; the icon
    /// appears with the first <see cref="Show"/>.
    /// </summary>
    /// <param name="address">The session bus's address; null where the session has no bus.</param>
    /// <param name="system">The system bus, on which logind tells the user's display session.</param>
    /// <param name="desktopId">The app's desktop entry, by which the desktop knows the app.</param>
    /// <param name="title">The app's name.</param>
    /// <param name="iconThemes">The folder of icon themes that holds the app's icon by its desktop entry's name; null for no icon.</param>
    /// <param name="logger">Hears of a session without a bus or logind and of a watcher that refused the icon.</param>
    public StatusNotifierTray(string? address, DBusConnection system, string desktopId, string title, string? iconThemes,
        ILogger<StatusNotifierTray> logger)
    {
        // Without an address the connection fails to connect, which the tray takes for a session without a bus.
        _connection = new DBusConnection(address ?? "");
        _system = system;
        _item = new Item(this, desktopId, title, iconThemes);
        _menu = new TrayMenu(_connection, command => CommandInvoked?.Invoke(this, command));
        _logger = logger;
        // Known before the agent serves its first launch, which in Game Mode must not start a window that gamescope never shows.
        _inGamescope = DisplaySession.IsGamescope(system, DisplaySession.TimeLimit);
        _ = Task.Run(FollowAsync);
        _ = Task.Run(FollowDisplayAsync);
    }

    public event EventHandler? Activated;

    public event EventHandler<string>? CommandInvoked;

    // systemd stops the agent with SIGTERM when the session ends, which the agent hears itself.
    public event EventHandler? SessionEnding
    {
        add { }
        remove { }
    }

    public event EventHandler? WindowReturnChanged;

    public WindowReturn WindowReturn => Volatile.Read(ref _inGamescope) ? WindowReturn.StoreLibrary
        : Volatile.Read(ref _hasHost) ? WindowReturn.Tray
        : WindowReturn.AppMenu;

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

    private static Channel<bool> OneLook() =>
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private async Task FollowAsync()
    {
        try
        {
            await _connection.ConnectAsync().ConfigureAwait(false);
            _connection.AddMethodHandlers([_item, _menu]);
            await _connection.RequestNameAsync(_name).ConfigureAwait(false);
            var watcher = new StatusNotifierWatcher(_connection, Watcher, "/StatusNotifierWatcher");
            using var owner = await _connection.WatchNameOwnerAsync(Watcher).ConfigureAwait(false);
            while (true)
            {
                if (owner.GetCurrentOwner() is null)
                {
                    Change(ref _hasHost, false);
                }

                var current = await owner.WaitForOwnerAsync(_stop.Token).ConfigureAwait(false);
                // A watcher that comes back, as when the desktop's shell restarts, knows no icon until each registers again.
                using var gone = CancellationTokenSource.CreateLinkedTokenSource(owner.GetOwnerChangedCancellationToken(current), _stop.Token);
                var registering = RegisterAsync(watcher, gone.Token);
                await FollowHostsAsync(watcher, gone.Token).ConfigureAwait(false);
                await registering.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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
            Change(ref _hasHost, false);
            LogNoTray(exception);
        }
    }

    private async Task RegisterAsync(StatusNotifierWatcher watcher, CancellationToken watching)
    {
        await _shown.Task.WaitAsync(watching).ConfigureAwait(false);
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

    // A watcher may run while no host draws its icons, as on a desktop whose panel lost its tray.
    private async Task FollowHostsAsync(StatusNotifierWatcher watcher, CancellationToken watching)
    {
        using var registered = await watcher.WatchStatusNotifierHostRegisteredAsync(() => _hostsChanged.Writer.TryWrite(true),
            emitOnCapturedContext: false).ConfigureAwait(false);
        using var unregistered = await watcher.WatchStatusNotifierHostUnregisteredAsync(() => _hostsChanged.Writer.TryWrite(true),
            emitOnCapturedContext: false).ConfigureAwait(false);
        try
        {
            while (true)
            {
                try
                {
                    Change(ref _hasHost, await watcher.GetIsStatusNotifierHostRegisteredAsync().ConfigureAwait(false));
                }
                // A watcher that leaves while it is read, as when Game Mode stops the desktop, answers nothing; its owner's
                // change ends this look, and a watcher that stays is read again at its next signal.
                catch (DBusErrorReplyException)
                {
                }

                await _hostsChanged.Reader.ReadAsync(watching).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (watching.IsCancellationRequested)
        {
        }
    }

    // A switch between the desktop and Game Mode ends one session and starts another, which becomes the user's display.
    private async Task FollowDisplayAsync()
    {
        try
        {
            var logind = DisplaySession.Manager(_system);
            using var added = await logind.WatchSessionNewAsync(_ => _sessionsChanged.Writer.TryWrite(true), emitOnCapturedContext: false)
                .ConfigureAwait(false);
            using var removed = await logind.WatchSessionRemovedAsync(_ => _sessionsChanged.Writer.TryWrite(true), emitOnCapturedContext: false)
                .ConfigureAwait(false);
            while (true)
            {
                Change(ref _inGamescope, Gamescope.IsSession(await DisplaySession.DesktopAsync(_system).ConfigureAwait(false)));
                await _sessionsChanged.Reader.ReadAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        // Any other failure, such as a system without logind, leaves the way back to the tray's watcher alone.
        catch (Exception exception)
        {
            LogNoDisplaySession(exception);
        }
    }

    // Each fact has one writer, which tells a change after it took effect, so the last change is always told.
    private void Change(ref bool fact, bool value)
    {
        var before = WindowReturn;
        Volatile.Write(ref fact, value);
        if (WindowReturn != before)
        {
            WindowReturnChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The session has no bus to show the tray icon on")]
    private partial void LogNoTray(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The desktop's tray watcher did not take the tray icon")]
    private partial void LogNotRegistered(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "logind tells no display session, so no store's game session is told apart")]
    private partial void LogNoDisplaySession(Exception exception);

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
