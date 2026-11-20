using System.Buffers.Binary;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

// Fixtures/appinfo.vdf is an app info cache in the layout of current clients with invented apps: 1000 with depots for
// Windows, Linux and macOS and one that names no system; 2000 for Windows; 3000 with every value type Steam writes;
// 4000 for macOS; 5000 that names no system; and 6000 with a value of a type app info never holds.
public sealed class SteamAppInfoTests
{
    private static readonly byte[] Cache =
        File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Library", "Steam", "Fixtures", "appinfo.vdf"));

    [Fact]
    public void Only_the_asked_for_apps_are_read_and_only_their_own_and_their_depots_systems()
    {
        var systems = Read(Cache, 1000, 3000, 5000, 9999);

        systems.Keys.Should().BeEquivalentTo([1000u, 3000u, 5000u], "the others, 6000 among them, are skipped by their size");
        systems[1000].CommonOsList.Should().Be("windows,linux");
        systems[1000].DepotOsLists.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["1001"] = "windows",
            ["1002"] = "linux",
            ["1004"] = "macos",
        });
        systems[3000].CommonOsList.Should().Be("windows", "lists elsewhere in the app's data are not its own");
        systems[3000].DepotOsLists.Should().BeEquivalentTo(new Dictionary<string, string> { ["3001"] = "linux" });
        systems[5000].CommonOsList.Should().BeNull();
        systems[5000].DepotOsLists.Should().BeEmpty();
    }

    [Theory]
    [InlineData("an older layout")]
    [InlineData("cut inside its header")]
    [InlineData("cut before its string table")]
    [InlineData("cut inside its string table")]
    [InlineData("a string table shorter than its keys")]
    [InlineData("an entry longer than its size")]
    [InlineData("a value of an unknown type")]
    public void A_damaged_cache_or_one_in_another_layout_is_refused(string damage)
    {
        var cache = Cache.ToArray();
        var asked = 1000u;
        var table = (int)BinaryPrimitives.ReadInt64LittleEndian(cache.AsSpan(8));
        switch (damage)
        {
            case "an older layout":
                cache[0] = 0x28;
                break;
            case "cut inside its header":
                cache = cache[..12];
                break;
            case "cut before its string table":
                cache = cache[..(table - 1)];
                break;
            case "cut inside its string table":
                cache = cache[..^3];
                break;
            case "a string table shorter than its keys":
                BinaryPrimitives.WriteUInt32LittleEndian(cache.AsSpan(table), 1);
                break;
            case "an entry longer than its size":
                // The first entry's size follows the 16-byte header and its app id, and counts a 60-byte head.
                BinaryPrimitives.WriteUInt32LittleEndian(cache.AsSpan(20), 60 + 10);
                break;
            default:
                asked = 6000;
                break;
        }

        FluentActions.Invoking(() => Read(cache, asked)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(1000u, "1001@11", OperatingSystemKind.Windows)]
    [InlineData(1000u, "1001@11,1002@12", OperatingSystemKind.Windows)]
    [InlineData(1000u, "1002@12,1003@13", OperatingSystemKind.Linux)]
    [InlineData(1000u, "1003@13,1004@14,1999@19", OperatingSystemKind.Linux)]
    [InlineData(2000u, "2001@21", OperatingSystemKind.Windows)]
    [InlineData(4000u, "4001@41", OperatingSystemKind.Unknown)]
    [InlineData(5000u, "5001@51", OperatingSystemKind.Windows)]
    public void A_depot_for_one_system_tells_the_build_and_otherwise_the_apps_own_list_or_windows(uint appId, string installed,
        OperatingSystemKind build)
    {
        Read(Cache, appId)[appId].BuildOf(installed.Split(',')).Should().Be(build);
    }

    private static Dictionary<uint, SteamAppSystems> Read(byte[] cache, params uint[] appIds)
    {
        using var stream = new MemoryStream(cache, writable: false);
        return SteamAppInfo.Read(stream, appIds.ToHashSet());
    }
}
