using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>The descriptors this process got from the one that started it, such as the AppImage runtime's pipe.</summary>
[SupportedOSPlatform("linux")]
public static partial class ProcessDescriptors
{
    // CLOSE_RANGE_CLOEXEC, which Linux takes since 5.11.
    private const uint CloseOnExec = 4;

    /// <summary>
    /// Keeps every descriptor this process holds from the programs it starts, since the AppImage runtime's pipe would keep
    /// the app's mount, and Steam's launch of the window, alive for as long as any of them runs.
    /// </summary>
    /// <returns>Null once done; otherwise the system's error, such as on a kernel before 5.11.</returns>
    public static int? KeepFromChildren() => CloseRange(3, uint.MaxValue, CloseOnExec) == 0 ? null : Marshal.GetLastPInvokeError();

    // .NET opens its own descriptors close-on-exec but has no call that marks those a process inherited, whatever their numbers.
    [LibraryImport("libc", EntryPoint = "close_range", SetLastError = true)]
    private static partial int CloseRange(uint first, uint last, uint flags);
}
