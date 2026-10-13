using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>Measures a window's system frame from its own styles, at the scale of the display it is on.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWindowFrames : IWindowFrames
{
    public int TitleBarHeight(nint window)
    {
        var handle = new HWND(window);
        var style = (WINDOW_STYLE)(uint)PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var extendedStyle = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var frame = default(RECT);
        return PInvoke.AdjustWindowRectExForDpi(ref frame, style, false, extendedStyle, PInvoke.GetDpiForWindow(handle)) ? -frame.top : 0;
    }
}
