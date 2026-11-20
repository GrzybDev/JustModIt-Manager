using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>The user's folders that stores are found relative to, laid out as each system lays them out.</summary>
internal static class TestEnvironments
{
    public static LibraryEnvironment Windows { get; } = new()
    {
        Platform = AppPlatform.Windows,
        HomeDirectory = TestPaths.Rooted('C', "Users", "Player"),
        ProgramFilesX86 = TestPaths.Rooted('C', "Program Files (x86)"),
        ApplicationData = TestPaths.Rooted('C', "Users", "Player", "AppData", "Roaming"),
        ProgramData = TestPaths.Rooted('C', "ProgramData"),
    };

    /// <summary>A Linux home without XDG variables, so the stores' default folders apply.</summary>
    public static LibraryEnvironment Linux { get; } = new()
    {
        Platform = AppPlatform.Linux,
        HomeDirectory = TestPaths.Rooted('H', "player"),
    };

    /// <summary>The layout of the system the tests run on: <see cref="Windows"/> there and <see cref="Linux"/> elsewhere.</summary>
    public static LibraryEnvironment Host => OperatingSystem.IsWindows() ? Windows : Linux;

    /// <summary>The system of games that stores report without saying, which the app takes as its own.</summary>
    public static OperatingSystemKind HostSystem => OperatingSystem.IsWindows() ? OperatingSystemKind.Windows : OperatingSystemKind.Linux;
}
