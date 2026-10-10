using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>Reads Windows' "Animation effects" setting, which the system keeps in memory: no file or registry access.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemMotion : ISystemMotion
{
    // The function writes the setting through an untyped address, since its type depends on the setting asked for.
    public unsafe bool IsReduced()
    {
        BOOL animate;
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &animate, 0) && !animate;
    }
}
