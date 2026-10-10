using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// The notification area icon, drawn by a hidden window on a thread of its own so the agent needs no UI framework, which
/// is top-level rather than message-only, since only those hear that Explorer restarted and the icon must be added again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsTrayHost : ITrayHost
{
    private const uint IconId = 1;
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint RefreshMessage = PInvoke.WM_APP + 2;
    // shellapi.h's NIN_KEYSELECT, which the metadata leaves out.
    private const uint KeySelect = PInvoke.NIN_SELECT | PInvoke.NINF_KEY;

    // The thread only removes the icon on its way out; one that hangs in a shell call must not hold up the agent's exit.
    private static readonly TimeSpan ThreadExitTimeout = TimeSpan.FromSeconds(5);

    // The shell shows what fits in the structure's buffers, less the terminator.
    private static readonly int MaxTooltipLength = default(NOTIFYICONDATAW).szTip.Length - 1;
    private static readonly int MaxTitleLength = default(NOTIFYICONDATAW).szInfoTitle.Length - 1;
    private static readonly int MaxMessageLength = default(NOTIFYICONDATAW).szInfo.Length - 1;

    private static int _classCount;

    // Windows calls a window procedure only on the thread that made the window, so that thread's host is the window's.
    [ThreadStatic]
    private static WindowsTrayHost? _threadHost;

    private readonly string _iconPath;
    private readonly ILogger _logger;
    private readonly Thread _thread;
    private readonly Lock _gate = new();
    private readonly Queue<(string Title, string Message, string? Tag)> _notifications = new();
    private readonly string _className = $"JustModIt.Manager.Tray.{Environment.ProcessId}.{Interlocked.Increment(ref _classCount)}";
    private string _tooltip = "";
    private IReadOnlyList<TrayMenuItem> _menu = [];
    private bool _shown;
    private bool _disposed;
    private HWND _window;

    // Only the tray thread touches these.
    private bool _added;
    private string? _notificationTag;
    private HICON _icon;
    private bool _ownsIcon;
    private uint _taskbarCreated;

    /// <summary>Starts the tray's thread; the icon appears with the first <see cref="Show"/>.</summary>
    /// <param name="iconPath">The executable or icon file whose first icon the tray shows.</param>
    /// <param name="logger">Logs failures of the tray window.</param>
    public WindowsTrayHost(string iconPath, ILogger<WindowsTrayHost> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        ArgumentNullException.ThrowIfNull(logger);
        _iconPath = iconPath;
        _logger = logger;
        _thread = new Thread(Run) { IsBackground = true, Name = "Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public event EventHandler? Activated;

    public event EventHandler<string>? CommandInvoked;

    public event EventHandler<string?>? NotificationClicked;

    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        ArgumentNullException.ThrowIfNull(menu);
        lock (_gate)
        {
            _tooltip = Clip(tooltip, MaxTooltipLength);
            _menu = [.. menu];
            _shown = true;
        }

        Refresh();
    }

    public void ShowNotification(string title, string message, string? tag)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            _notifications.Enqueue((Clip(title, MaxTitleLength), Clip(message, MaxMessageLength), tag));
        }

        Refresh();
    }

    public void Dispose()
    {
        HWND window;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            window = _window;
        }

        if (!window.IsNull)
        {
            PInvoke.PostMessage(window, PInvoke.WM_CLOSE, default, default);
        }

        _thread.Join(ThreadExitTimeout);
    }

    /// <summary>Cuts a text to what the system shows, ending it with an ellipsis.</summary>
    /// <param name="text">The text to show.</param>
    /// <param name="length">The most characters the system shows.</param>
    internal static string Clip(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        // A cut between the halves of a surrogate pair would leave half a character, such as of an emoji in a game's name.
        var kept = length > 1 && char.IsHighSurrogate(text[length - 2]) ? length - 2 : length - 1;
        return string.Concat(text.AsSpan(0, kept), "…");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (_threadHost is { } host)
        {
            try
            {
                if (host.Handle(window, message, wParam, lParam))
                {
                    return default;
                }
            }
            // An exception unwinding into the window manager would end the process.
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                host.LogHandlerFailed(exception);
            }
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    // Windows calls WindowProc through the class, and only unsafe code can take its address and the class name's.
    private unsafe HWND RegisterAndCreateWindow(HINSTANCE instance)
    {
        fixed (char* className = _className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = instance,
                lpszClassName = className,
            };
            return PInvoke.RegisterClassEx(windowClass) == 0
                ? HWND.Null
                : PInvoke.CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, HWND.Null, HMENU.Null, instance, null);
        }
    }

    private void Run()
    {
        _threadHost = this;
        using var module = PInvoke.GetModuleHandle((string?)null);
        try
        {
            if (!CreateWindow((HINSTANCE)module.DangerousGetHandle()))
            {
                return;
            }

            LoadIcon();
            _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
            ApplyState();
            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }
        finally
        {
            if (_ownsIcon)
            {
                PInvoke.DestroyIcon(_icon);
            }

            PInvoke.UnregisterClass(_className, module);
        }
    }

    private bool CreateWindow(HINSTANCE instance)
    {
        var window = RegisterAndCreateWindow(instance);
        if (window.IsNull)
        {
            LogWindowFailed(Marshal.GetLastPInvokeError());
            return false;
        }

        bool disposed;
        lock (_gate)
        {
            _window = window;
            disposed = _disposed;
        }

        // Disposed before the window existed, so nobody else will close it.
        if (disposed)
        {
            PInvoke.PostMessage(window, PInvoke.WM_CLOSE, default, default);
        }

        return true;
    }

    private void LoadIcon()
    {
        Span<HICON> small = stackalloc HICON[1];
        if (PInvoke.ExtractIconEx(_iconPath, 0, [], small) > 0 && !small[0].IsNull)
        {
            _icon = small[0];
            _ownsIcon = true;
        }
        else
        {
            _icon = PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION);
        }
    }

    private bool Handle(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case CallbackMessage:
                OnIconEvent(window, (uint)(lParam.Value & 0xFFFF), wParam);
                return true;
            case RefreshMessage:
                ApplyState();
                return true;
            case PInvoke.WM_CLOSE:
                if (_added)
                {
                    PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, NewData(window, 0));
                    _added = false;
                }

                PInvoke.DestroyWindow(window);
                return true;
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return true;
            default:
                if (message == _taskbarCreated && _taskbarCreated != 0)
                {
                    _added = false;
                    ApplyState();
                    return true;
                }

                return false;
        }
    }

    private void OnIconEvent(HWND window, uint iconEvent, WPARAM position)
    {
        switch (iconEvent)
        {
            case PInvoke.NIN_SELECT or KeySelect:
                Activated?.Invoke(this, EventArgs.Empty);
                break;
            case PInvoke.WM_CONTEXTMENU:
                if (ShowMenu(window, (short)(position.Value & 0xFFFF), (short)((position.Value >> 16) & 0xFFFF)) is { } command)
                {
                    CommandInvoked?.Invoke(this, command);
                }

                break;
            case PInvoke.NIN_BALLOONUSERCLICK:
                NotificationClicked?.Invoke(this, _notificationTag);
                break;
        }
    }

    private string? ShowMenu(HWND window, int x, int y)
    {
        IReadOnlyList<TrayMenuItem> items;
        lock (_gate)
        {
            items = _menu;
        }

        if (items.Count == 0)
        {
            return null;
        }

        using var menu = new DestroyMenuSafeHandle(PInvoke.CreatePopupMenu());
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = (nuint)(index + 1);
            if (item.IsSeparator)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
                continue;
            }

            // A single ampersand would underline the next letter as the item's access key.
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | (item.IsEnabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED), id,
                item.Text.Replace("&", "&&", StringComparison.Ordinal));
            if (item.IsDefault)
            {
                PInvoke.SetMenuDefaultItem(menu, (uint)id, 0);
            }
        }

        // Without taking the foreground the menu would stay open after a click elsewhere.
        PInvoke.SetForegroundWindow(window);
        var flags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY
            | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN;
        if (PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_MENUDROPALIGNMENT) != 0)
        {
            flags |= TRACK_POPUP_MENU_FLAGS.TPM_RIGHTALIGN;
        }

        int chosen = PInvoke.TrackPopupMenuEx(menu, (uint)flags, x, y, window, null);
        PInvoke.PostMessage(window, PInvoke.WM_NULL, default, default);
        return chosen > 0 && chosen <= items.Count && items[chosen - 1] is { IsEnabled: true } picked ? picked.Command : null;
    }

    private void Refresh()
    {
        HWND window;
        lock (_gate)
        {
            window = _window;
        }

        // Before the window exists the thread applies the state itself once it has created it.
        if (!window.IsNull)
        {
            PInvoke.PostMessage(window, RefreshMessage, default, default);
        }
    }

    private void ApplyState()
    {
        string tooltip;
        bool shown;
        lock (_gate)
        {
            tooltip = _tooltip;
            shown = _shown;
        }

        if (!shown)
        {
            return;
        }

        var data = NewData(_window,
            NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP
            | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = tooltip;
        if (!_added)
        {
            // Fails while Explorer is not running; the icon is added when it says it has started.
            if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, data))
            {
                return;
            }

            _added = true;
            var version = NewData(_window, 0);
            version.uVersion = PInvoke.NOTIFYICON_VERSION_4;
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, version);
        }
        else if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, data))
        {
            _added = false;
            return;
        }

        ShowQueuedNotifications();
    }

    private void ShowQueuedNotifications()
    {
        while (true)
        {
            (string Title, string Message, string? Tag) notification;
            lock (_gate)
            {
                if (!_notifications.TryDequeue(out notification))
                {
                    return;
                }
            }

            var data = NewData(_window, NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
            data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_USER | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_RESPECT_QUIET_TIME;
            data.szInfoTitle = notification.Title;
            data.szInfo = notification.Message;
            if (PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, data))
            {
                _notificationTag = notification.Tag;
            }
        }
    }

    private static NOTIFYICONDATAW NewData(HWND window, NOTIFY_ICON_DATA_FLAGS flags) => new()
    {
        cbSize = (uint)Unsafe.SizeOf<NOTIFYICONDATAW>(),
        hWnd = window,
        uID = IconId,
        uFlags = flags,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tray window could not be created (error {Error})")]
    private partial void LogWindowFailed(int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a tray event failed")]
    private partial void LogHandlerFailed(Exception exception);
}
