using System.Buffers.Binary;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class DesktopEntryTests : IDisposable
{
    private const string DesktopId = "com.example.ExampleApp";

    private static readonly byte[] Small = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private static readonly byte[] Large = [0x89, 0x50, 0x4E, 0x47, 4, 5, 6, 7];

    private readonly string _home = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(_home, ".local", "share");

    private string Config => Path.Combine(_home, ".config");

    private string Defaults => Path.Combine(Config, "mimeapps.list");

    [Fact]
    public void The_entry_runs_the_appimage_with_the_link_or_to_exit_or_uninstall_the_users_defaults_give_it_the_scheme_and_both_go_with_every_other_line_kept()
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Defaults, """
            [Added Associations]
            text/plain=org.example.Editor.desktop;

            [Default Applications]
            text/html=org.example.Browser.desktop
            x-scheme-handler/example-app=org.example.Older.desktop

            [Removed Associations]
            image/png=org.example.Viewer.desktop;

            """);
        var entry = new DesktopEntry(DesktopId, "Game;Utility;", [new("exit", "Exit", "--exit"), new("uninstall", "Uninstall", "--uninstall")],
            () => Ico((16, Small), (256, Large)), Data, Config);
        var entryFile = Path.Combine(Data, "applications", DesktopId + ".desktop");
        var smallIcon = IconFile("16x16");
        var largeIcon = IconFile("256x256");
        var otherIcon = Path.Combine(Data, "icons", "hicolor", "16x16", "apps", "org.example.Editor.png");
        Directory.CreateDirectory(Path.GetDirectoryName(otherIcon)!);
        File.WriteAllBytes(otherIcon, Small);
        // A size whose folder holds no app icons, which the removal passes by.
        Directory.CreateDirectory(Path.Combine(Data, "icons", "hicolor", "48x48", "mimetypes"));

        entry.Register("example-app", "Example App", "/home/player/Applications/Example \"App\" $HOME 100%.AppImage");

        File.ReadAllText(entryFile).Should().Be("""
            [Desktop Entry]
            Type=Application
            Name=Example App
            Exec="/home/player/Applications/Example \\"App\\" \\$HOME 100%%.AppImage" %u
            Icon=com.example.ExampleApp
            Terminal=false
            Categories=Game;Utility;
            MimeType=x-scheme-handler/example-app;
            StartupWMClass=com.example.ExampleApp
            StartupNotify=true
            Actions=exit;uninstall;

            [Desktop Action exit]
            Name=Exit
            Exec="/home/player/Applications/Example \\"App\\" \\$HOME 100%%.AppImage" --exit

            [Desktop Action uninstall]
            Name=Uninstall
            Exec="/home/player/Applications/Example \\"App\\" \\$HOME 100%%.AppImage" --uninstall

            """, "the desktop unquotes the path as one argument and puts the link after it");
        File.ReadAllBytes(smallIcon).Should().Equal(Small, "each frame of the icon goes to its size");
        File.ReadAllBytes(largeIcon).Should().Equal(Large);
        File.ReadAllText(Defaults).Should().Be("""
            [Added Associations]
            text/plain=org.example.Editor.desktop;

            [Default Applications]
            text/html=org.example.Browser.desktop
            x-scheme-handler/example-app=com.example.ExampleApp.desktop

            [Removed Associations]
            image/png=org.example.Viewer.desktop;

            """);

        var written = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var file in new[] { entryFile, smallIcon, largeIcon, Defaults })
        {
            File.SetLastWriteTimeUtc(file, written);
        }

        entry.Register("example-app", "Example App", "/home/player/Applications/Example \"App\" $HOME 100%.AppImage");
        new[] { entryFile, smallIcon, largeIcon, Defaults }.Select(File.GetLastWriteTimeUtc).Should().AllBeEquivalentTo(written,
            "a start that finds the files as they are writes nothing");

        entry.Unregister("example-app");
        (File.Exists(entryFile), File.Exists(smallIcon), File.Exists(largeIcon)).Should().Be((false, false, false));
        File.Exists(otherIcon).Should().BeTrue("another app's icon stays");
        File.ReadAllText(Defaults).Should().Be("""
            [Added Associations]
            text/plain=org.example.Editor.desktop;

            [Default Applications]
            text/html=org.example.Browser.desktop

            [Removed Associations]
            image/png=org.example.Viewer.desktop;

            """);
    }

    [Fact]
    public void The_scheme_joins_the_end_of_the_users_defaults_or_starts_them()
    {
        const string Handler = "com.example.ExampleApp.desktop";

        DesktopEntry.WithDefault(null, "x-scheme-handler/example-app", Handler)
            .Should().Be("[Default Applications]\nx-scheme-handler/example-app=com.example.ExampleApp.desktop\n");
        DesktopEntry.WithDefault("[Added Associations]\ntext/plain=org.example.Editor.desktop;\n", "x-scheme-handler/example-app", Handler)
            .Should().Be("[Added Associations]\ntext/plain=org.example.Editor.desktop;\n\n"
                + "[Default Applications]\nx-scheme-handler/example-app=com.example.ExampleApp.desktop\n");
        DesktopEntry.WithDefault("[Default Applications]\ntext/html=org.example.Browser.desktop\n\n[Added Associations]\n",
                "x-scheme-handler/example-app", Handler)
            .Should().Be("[Default Applications]\ntext/html=org.example.Browser.desktop\n"
                + "x-scheme-handler/example-app=com.example.ExampleApp.desktop\n\n[Added Associations]\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    // An .ico of PNG frames, as the app embeds: the count, then each frame's width, length and offset, then the frames.
    private static byte[] Ico(params (int Size, byte[] Png)[] frames)
    {
        var ico = new byte[6 + (16 * frames.Length) + frames.Sum(frame => frame.Png.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), (ushort)frames.Length);
        var offset = 6 + (16 * frames.Length);
        for (var index = 0; index < frames.Length; index++)
        {
            var (size, png) = frames[index];
            var entry = ico.AsSpan(6 + (16 * index), 16);
            entry[0] = entry[1] = (byte)(size % 256);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], png.Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);
            png.CopyTo(ico, offset);
            offset += png.Length;
        }

        return ico;
    }

    private string IconFile(string size) => Path.Combine(Data, "icons", "hicolor", size, "apps", DesktopId + ".png");
}
