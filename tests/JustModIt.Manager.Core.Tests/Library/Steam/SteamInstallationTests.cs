using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamInstallationTests
{
    private static readonly string Home = TestEnvironments.Linux.HomeDirectory;

    private readonly InMemoryFileSystem _files = new();

    [Fact]
    public void On_linux_only_the_client_behind_its_own_folder_counts_since_a_sandboxed_steam_is_out_of_reach()
    {
        _files.AddDirectory(Path.Join(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps"));
        Root().Should().BeNull();

        var native = Path.GetDirectoryName(_files.AddDirectory(Path.Join(Home, ".local", "share", "Steam", "steamapps")))!;
        _files.AddDirectory(Path.Join(Home, ".steam"));
        _files.AddDirectoryLink(Path.Join(Home, ".steam", "root"), native);

        Root().Should().Be(native);
    }

    [Fact]
    public void The_linux_client_s_files_name_its_process_and_its_language_unless_caught_being_written()
    {
        Read().Should().Be(new SteamClientFiles(null, null));

        _files.AddFile(Path.Join(Home, ".steam", SteamInstallation.ProcessFile), "4100\n");
        _files.AddFile(Path.Join(Home, ".steam", SteamInstallation.RegistryFile), """
            "Registry"
            {
                "HKLM" { "Software" { "Valve" { "Steam" { "SteamPID" "4100" "language" "english" } } } }
                "HKCU" { "Software" { "Valve" { "Steam" { "Language" "polish" } } } }
            }
            """);
        Read().Should().Be(new SteamClientFiles(4100, "polish"));

        _files.AddFile(Path.Join(Home, ".steam", SteamInstallation.ProcessFile), "");
        _files.AddFile(Path.Join(Home, ".steam", SteamInstallation.RegistryFile), "\"Registry\"\n{\n\t\"HKCU\"\n");
        Read().Should().Be(new SteamClientFiles(null, null));
    }

    private string? Root() => SteamInstallation.FindRoot(_files, new InMemoryRegistry(), TestEnvironments.Linux);

    private SteamClientFiles Read() => SteamInstallation.ReadClientFiles(_files, TestEnvironments.Linux);
}
