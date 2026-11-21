using System.Buffers.Binary;
using System.Text;
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

    private sealed class KeyValuesWriter
    {
        private readonly List<byte> _bytes = [];

        public KeyValuesWriter Object(string key) => Entry(0, key);

        public KeyValuesWriter Text(string key, string value) => Entry(1, key).Terminated(value);

        public KeyValuesWriter Number(string key, int value)
        {
            Entry(2, key);
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _bytes.AddRange(bytes);
            return this;
        }

        public KeyValuesWriter End()
        {
            _bytes.Add(8);
            return this;
        }

        public byte[] ToArray() => [.. _bytes];

        private KeyValuesWriter Entry(byte type, string key)
        {
            _bytes.Add(type);
            return Terminated(key);
        }

        private KeyValuesWriter Terminated(string text)
        {
            _bytes.AddRange(Encoding.UTF8.GetBytes(text));
            _bytes.Add(0);
            return this;
        }
    }
}
