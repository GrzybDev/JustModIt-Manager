using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class GamescopeTests
{
    [Fact]
    public void A_gamescope_session_among_the_installed_sessions()
    {
        var files = new InMemoryFileSystem();
        string[] folders = [TestPaths.Rooted('H', "usr", "local", "share"), TestPaths.Rooted('H', "usr", "share")];
        files.AddFile(Path.Join(folders[1], "wayland-sessions", "plasma.desktop"), "");
        Gamescope.IsInstalled(files, folders).Should().BeFalse("a desktop session alone opens no Game Mode");

        files.AddFile(Path.Join(folders[0], "wayland-sessions", "gamescope-session-steam.desktop"), "");
        Gamescope.IsInstalled(files, folders).Should().BeTrue("a handheld image may install its session under the local folder");
        Gamescope.IsInstalled(files, folders[1..]).Should().BeFalse();

        files.AddFile(Path.Join(folders[1], "wayland-sessions", "gamescope-wayland.desktop"), "");
        Gamescope.IsInstalled(files, folders[1..]).Should().BeTrue("SteamOS installs its session with the system");
        Gamescope.IsInstalled(files, []).Should().BeFalse();
    }
}
