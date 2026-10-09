using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// The notification area icon, drawn by a hidden window on a thread of its own so the agent needs no UI framework, which
/// is top-level rather than message-only, since only those hear that Explorer restarted and the icon must be added again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsTrayHost : ITrayHost
{
    internal const int MaxTooltipLength = 127;
    internal const int MaxTitleLength = 63;
    internal const int MaxMessageLength = 255;

    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 1;
    private const uint RefreshMessage = 0x8000 + 2;
    private const uint WmNull = 0x0000;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmContextMenu = 0x007B;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;
    private const uint NinBalloonUserClick = 0x0405;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NifShowTip = 0x80;
    private const uint NiifUser = 0x4;
    private const uint NiifRespectQuietTime = 0x80;
    private const uint MfString = 0x0;
    private const uint MfGrayed = 0x1;
    private const uint MfSeparator = 0x800;
    private const uint TpmRightAlign = 0x8;
    private const uint TpmBottomAlign = 0x20;
    private const uint TpmNoNotify = 0x80;
    private const uint TpmReturnCmd = 0x100;
    private const uint TpmRightButton = 0x2;
    private const int SmMenuDropAlignment = 40;
    private const int GwlpUserData = -21;
    private const nint IdiApplication = 32512;

    private static int _classCount;

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
    private nint _window;

    // Only the tray thread touches these.
    private bool _added;
    private string? _notificationTag;
    private nint _icon;
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
        nint window;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            window = _window;
        }

        if (window != 0)
        {
            PostMessageW(window, WmClose, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(5));
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
    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        var self = GetWindowLongPtrW(window, GwlpUserData);
        if (self != 0 && GCHandle.FromIntPtr(self).Target is WindowsTrayHost host)
        {
            try
            {
                if (host.Handle(window, message, wParam, lParam))
                {
                    return 0;
                }
            }
            // An exception unwinding into the window manager would end the process.
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                host.LogHandlerFailed(exception);
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void Run()
    {
        var instance = GetModuleHandleW(null);
        var self = GCHandle.Alloc(this);
        try
        {
            if (!CreateWindow(instance, self))
            {
                return;
            }

            LoadIcon();
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            ApplyState();
            Message message;
            while (GetMessageW(&message, 0, 0, 0) > 0)
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        finally
        {
            if (_ownsIcon)
            {
                DestroyIcon(_icon);
            }

            UnregisterClassW(_className, instance);
            self.Free();
        }
    }

    private bool CreateWindow(nint instance, GCHandle self)
    {
        fixed (char* className = _className)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProc = &WindowProc,
                Instance = instance,
                ClassName = className,
            };
            if (RegisterClassExW(&windowClass) == 0)
            {
                LogWindowFailed(Marshal.GetLastPInvokeError());
                return false;
            }
        }

        var window = CreateWindowExW(0, _className, _className, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (window == 0)
        {
            LogWindowFailed(Marshal.GetLastPInvokeError());
            return false;
        }

        SetWindowLongPtrW(window, GwlpUserData, GCHandle.ToIntPtr(self));
        bool disposed;
        lock (_gate)
        {
            _window = window;
            disposed = _disposed;
        }

        // Disposed before the window existed, so nobody else will close it.
        if (disposed)
        {
            PostMessageW(window, WmClose, 0, 0);
        }

        return true;
    }

    private void LoadIcon()
    {
        nint small = 0;
        if (ExtractIconExW(_iconPath, 0, null, &small, 1) > 0 && small != 0)
        {
            _icon = small;
            _ownsIcon = true;
        }
        else
        {
            _icon = LoadIconW(0, IdiApplication);
        }
    }

    private bool Handle(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case CallbackMessage:
                OnIconEvent(window, (uint)(lParam & 0xFFFF), wParam);
                return true;
            case RefreshMessage:
                ApplyState();
                return true;
            case WmClose:
                if (_added)
                {
                    var data = NewData(window, 0);
                    Shell_NotifyIconW(NimDelete, &data);
                    _added = false;
                }

                DestroyWindow(window);
                return true;
            case WmDestroy:
                PostQuitMessage(0);
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

    private void OnIconEvent(nint window, uint iconEvent, nint position)
    {
        switch (iconEvent)
        {
            case NinSelect or NinKeySelect:
                Activated?.Invoke(this, EventArgs.Empty);
                break;
            case WmContextMenu:
                if (ShowMenu(window, (short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF)) is { } command)
                {
                    CommandInvoked?.Invoke(this, command);
                }

                break;
            case NinBalloonUserClick:
                NotificationClicked?.Invoke(this, _notificationTag);
                break;
        }
    }

    private string? ShowMenu(nint window, int x, int y)
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

        var menu = CreatePopupMenu();
        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var id = (nuint)(index + 1);
                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MfSeparator, 0, null);
                    continue;
                }

                // A single ampersand would underline the next letter as the item's access key.
                AppendMenuW(menu, MfString | (item.IsEnabled ? 0 : MfGrayed), id, item.Text.Replace("&", "&&", StringComparison.Ordinal));
                if (item.IsDefault)
                {
                    SetMenuDefaultItem(menu, (uint)id, 0);
                }
            }

            // Without taking the foreground the menu would stay open after a click elsewhere.
            SetForegroundWindow(window);
            var alignment = GetSystemMetrics(SmMenuDropAlignment) != 0 ? TpmRightAlign : 0;
            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmNoNotify | TpmRightButton | TpmBottomAlign | alignment, x, y,
                window, 0);
            PostMessageW(window, WmNull, 0, 0);
            return chosen > 0 && chosen <= items.Count && items[chosen - 1] is { IsEnabled: true } picked ? picked.Command : null;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void Refresh()
    {
        nint window;
        lock (_gate)
        {
            window = _window;
        }

        // Before the window exists the thread applies the state itself once it has created it.
        if (window != 0)
        {
            PostMessageW(window, RefreshMessage, 0, 0);
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

        var data = NewData(_window, NifMessage | NifIcon | NifTip | NifShowTip);
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        Copy(tooltip, data.Tip, MaxTooltipLength + 1);
        if (!_added)
        {
            // Fails while Explorer is not running; the icon is added when it says it has started.
            if (!Shell_NotifyIconW(NimAdd, &data))
            {
                return;
            }

            _added = true;
            var version = NewData(_window, 0);
            version.TimeoutOrVersion = NotifyIconVersion4;
            Shell_NotifyIconW(NimSetVersion, &version);
        }
        else if (!Shell_NotifyIconW(NimModify, &data))
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

            var data = NewData(_window, NifInfo);
            data.InfoFlags = NiifUser | NiifRespectQuietTime;
            Copy(notification.Title, data.InfoTitle, MaxTitleLength + 1);
            Copy(notification.Message, data.Info, MaxMessageLength + 1);
            if (Shell_NotifyIconW(NimModify, &data))
            {
                _notificationTag = notification.Tag;
            }
        }
    }

    private static NotifyIconData NewData(nint window, uint flags) => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = window,
        Id = IconId,
        Flags = flags,
    };

    private static void Copy(string text, char* destination, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tray window could not be created (error {Error})")]
    private partial void LogWindowFailed(int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a tray event failed")]
    private partial void LogHandlerFailed(Exception exception);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(string className, nint instance);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Message* message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Message* message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Message* message);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint window, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint window, int index);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial nint LoadIconW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string file, int index, nint* largeIcons, nint* smallIcons, uint count);
}
