using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>The X display the window draws on through Avalonia's X11 backend, also under XWayland.</summary>
[SupportedOSPlatform("linux")]
public static partial class XDisplay
{
    private const string Xlib = "libX11.so.6";

    private static Action? s_lost;

    /// <summary>Whether the display that this process's <c>DISPLAY</c> names answers, so a window can open on it.</summary>
    public static bool IsReachable()
    {
        var display = OpenDisplay(null);
        if (display == 0)
        {
            return false;
        }

        _ = CloseDisplay(display);
        return true;
    }

    /// <summary>
    /// Ends this process at once when its display goes away, as when the desktop's session ends for Game Mode, after
    /// <paramref name="lost"/> ran; Xlib would call <c>exit</c> instead, whose run of the .NET runtime's shutdown hangs, so
    /// the window would stay behind unseen and hold its lock.
    /// </summary>
    /// <param name="lost">Runs first, on the thread whose request met the closed connection.</param>
    public static unsafe void ExitWhenLost(Action lost)
    {
        s_lost = lost;
        // Xlib calls back into the app, which needs the handler's native address.
        SetIOErrorHandler(&OnLost);
    }

    [UnmanagedCallersOnly]
    private static int OnLost(nint display)
    {
        s_lost?.Invoke();
        ExitNow(0);
        return 0;
    }

    [LibraryImport(Xlib, EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint OpenDisplay(string? name);

    [LibraryImport(Xlib, EntryPoint = "XCloseDisplay")]
    private static partial int CloseDisplay(nint display);

    [LibraryImport(Xlib, EntryPoint = "XSetIOErrorHandler")]
    private static unsafe partial nint SetIOErrorHandler(delegate* unmanaged<nint, int> handler);

    // Skips the exit handlers, among them the runtime's, which is what hangs.
    [LibraryImport("libc", EntryPoint = "_exit")]
    private static partial void ExitNow(int status);
}
