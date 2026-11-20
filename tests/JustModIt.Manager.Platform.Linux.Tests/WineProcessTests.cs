using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux.Tests;

// A /proc laid out as a Proton launch leaves it, since a test cannot run Wine.
[SupportedOSPlatform("linux")]
public sealed class WineProcessTests : IDisposable
{
    private const int User = 1000;
    private const string Proton = "/opt/steam/Proton/files/lib/wine";

    private readonly string _proc = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    public WineProcessTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The laid-out /proc links to Unix paths, as only Linux makes them.");
        WriteStatus("self", "tests", User);
    }

    [Fact]
    public void A_windows_program_wine_runs_is_listed_by_its_own_file_and_the_steam_game_its_launch_names()
    {
        AddProcess(100, "Example Game.exe", User, $"{Proton}/x86_64-unix/wine64-preloader",
            maps: [$"{Proton}/x86_64-unix/wine64-preloader", "/games/Example Game/Example Game.exe", $"{Proton}/x86_64-windows/ntdll.dll"],
            environment: ["HOME=/home/player", "SteamAppId=4242", "STEAM_COMPAT_APP_ID=4242"]);
        AddProcess(101, "winedevice.exe", User, $"{Proton}/x86_64-unix/wine64-preloader",
            maps: [$"{Proton}/x86_64-windows/ntoskrnl.exe", $"{Proton}/x86_64-windows/winedevice.exe"],
            environment: ["STEAM_COMPAT_APP_ID=4242"]);
        AddProcess(102, "Example-Win64-S", User, $"{Proton}/x86_64-unix/wine64-preloader",
            maps: [$"{Proton}/x86_64-windows/kernel32.dll", "/games/Example/Binaries/Win64/Example-Win64-Shipping.exe"]);
        AddProcess(103, "GameThread", User, $"{Proton}/x86_64-unix/wine64-preloader", maps: ["/games/Example/Example.exe"]);
        AddProcess(104, "root-tool", 0, image: null, environment: ["SteamAppId=4242"]);
        AddProcess(105, "Example.exe", User, $"{Proton}/x86_64-unix/wine64-preloader", state: "Z (zombie)");
        AddProcess(106, "reaper", User, "/opt/steam/ubuntu12_32/reaper", environment: ["SteamAppId=0", "STEAM_COMPAT_APP_ID=2147483648"]);

        var processes = new LinuxProcessSource(_proc).Snapshot();

        processes.OrderBy(process => process.Id).Should().Equal(
            new ProcessEntry(100, "Example Game", User, "/games/Example Game/Example Game.exe", new GameKey(StoreId.Steam, "4242")),
            new ProcessEntry(101, "winedevice", User, $"{Proton}/x86_64-windows/winedevice.exe", new GameKey(StoreId.Steam, "4242")),
            new ProcessEntry(102, "Example-Win64-Shipping", User, "/games/Example/Binaries/Win64/Example-Win64-Shipping.exe"),
            new ProcessEntry(103, "Example", User, "/games/Example/Example.exe"),
            new ProcessEntry(104, "root-tool", 0, ImagePath: null),
            new ProcessEntry(106, "reaper", User, "/opt/steam/ubuntu12_32/reaper"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_proc))
        {
            Directory.Delete(_proc, recursive: true);
        }
    }

    private void AddProcess(int id, string name, int user, string? image, string state = "S (sleeping)", string[]? maps = null,
        string[]? environment = null)
    {
        var folder = WriteStatus(id.ToString(System.Globalization.CultureInfo.InvariantCulture), name, user, state);
        if (image is not null)
        {
            File.CreateSymbolicLink(Path.Join(folder, "exe"), image);
        }

        // The kernel pads a mapped file's path to a column after its inode.
        File.WriteAllLines(Path.Join(folder, "maps"), (maps ?? []).Select((path, index) =>
            $"7f00{index:x4}0000-7f00{index:x4}1000 r--p 00000000 103:08 {1000 + index}                    {path}").Append("7ffd0000-7ffd1000 rw-p 00000000 00:00 0 "));
        File.WriteAllText(Path.Join(folder, "environ"), string.Join('\0', environment ?? []) + '\0');
    }

    private string WriteStatus(string process, string name, int user, string state = "S (sleeping)")
    {
        var folder = Directory.CreateDirectory(Path.Join(_proc, process)).FullName;
        File.WriteAllText(Path.Join(folder, "status"),
            $"Name:\t{name}\nUmask:\t0022\nState:\t{state}\nTgid:\t1\nPid:\t1\nUid:\t{user}\t{user}\t{user}\t{user}\nGid:\t{user}\t{user}\t{user}\t{user}\n");
        return folder;
    }
}
