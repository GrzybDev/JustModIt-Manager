using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core;

/// <summary>An operating system the app runs on.</summary>
public enum AppPlatform
{
    Windows,
    Linux,
    MacOS,
}

public static class AppPlatforms
{
    public static AppPlatform Current { get; } =
        OperatingSystem.IsWindows() ? AppPlatform.Windows
        : OperatingSystem.IsMacOS() ? AppPlatform.MacOS
        : AppPlatform.Linux;

    /// <summary>The same system as one a game build is made for.</summary>
    /// <param name="platform">The system the app runs on.</param>
    public static OperatingSystemKind AsOperatingSystem(this AppPlatform platform) => platform switch
    {
        AppPlatform.Windows => OperatingSystemKind.Windows,
        AppPlatform.MacOS => OperatingSystemKind.MacOS,
        AppPlatform.Linux => OperatingSystemKind.Linux,
        _ => OperatingSystemKind.Unknown,
    };
}
