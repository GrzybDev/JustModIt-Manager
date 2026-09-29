using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsForegroundHandoff : IForegroundHandoff
{
    public static WindowsForegroundHandoff Instance { get; } = new();

    // Refused unless this process may take the foreground itself; the other window then only flashes in the taskbar.
    public void Allow(int processId) => _ = AllowSetForegroundWindow(unchecked((uint)processId));

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
