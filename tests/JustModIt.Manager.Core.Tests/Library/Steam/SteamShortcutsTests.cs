using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamShortcutsTests
{
    // Two shortcuts of invented programs, one as current clients write it and one with the keys of earlier clients; the
    // last end closes the file itself.
    private static readonly byte[] ShortcutsFile = new KeyValuesWriter()
        .Object("shortcuts")
            .Object("0")
                .Number("appid", -1_234_567)
                .Text("AppName", "Lantern Café")
                .Text("Exe", "\"/home/player/Applications/Lantern Café.AppImage\"")
                .Text("StartDir", "\"/home/player/Applications/\"")
                .Text("LaunchOptions", "--fullscreen")
                .Number("IsHidden", 0)
                .Object("tags").Text("0", "favorite").End()
            .End()
            .Object("1")
                .Text("appname", "Tide Atlas")
                .Text("exe", "/opt/tide-atlas/atlas")
                .Number("LastPlayTime", 1_700_000_000)
            .End()
        .End()
        .End()
        .ToArray();

    [Fact]
    public void Steams_shortcuts_name_the_programs_they_start()
    {
        SteamShortcuts.Read(ShortcutsFile).Should().Equal(
            new SteamShortcut("Lantern Café", "/home/player/Applications/Lantern Café.AppImage"),
            new SteamShortcut("Tide Atlas", "/opt/tide-atlas/atlas"));
    }

    [Fact]
    public void A_shortcuts_file_cut_short_reads_as_damaged()
    {
        for (var length = 0; length < ShortcutsFile.Length; length++)
        {
            var cut = ShortcutsFile[..length];
            FluentActions.Invoking(() => SteamShortcuts.Read(cut)).Should()
                .Throw<InvalidDataException>($"the file ends after {length} of its {ShortcutsFile.Length} bytes");
        }
    }
}
