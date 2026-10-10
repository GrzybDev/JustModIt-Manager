using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Windows.Win32;

namespace JustModIt.Manager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsForegroundHandoff : IForegroundHandoff
{
    public static WindowsForegroundHandoff Instance { get; } = new();

    // Refused unless this process may take the foreground itself; the other window then only flashes in the taskbar.
    public void Allow(int processId) => _ = PInvoke.AllowSetForegroundWindow(unchecked((uint)processId));
}
