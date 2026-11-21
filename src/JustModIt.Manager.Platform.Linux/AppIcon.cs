using System.Buffers.Binary;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>The app's icon as files of an icon theme, where the desktop's tray, notifications and menus look it up by name.</summary>
public static class AppIcon
{
    private const int LogoSize = 256;

    /// <summary>Writes the logo into a folder of icon themes, unless the file holds it already.</summary>
    /// <param name="root">The folder that holds the themes.</param>
    /// <param name="desktopId">The name the icon goes by, which is the app's desktop entry's.</param>
    /// <param name="png">The logo, 256 px square.</param>
    /// <returns>The icon's file.</returns>
    public static string Write(string root, string desktopId, byte[] png)
    {
        var path = PathOf(root, desktopId, LogoSize);
        WriteIfChanged(path, png);
        return path;
    }

    /// <summary>Writes each frame of the app's icon into a folder of icon themes, in the folder of its size.</summary>
    /// <param name="root">The folder that holds the themes.</param>
    /// <param name="desktopId">The name the icon goes by.</param>
    /// <param name="ico">The app's <c>.ico</c>, whose frames are PNG images.</param>
    public static void WriteFrames(string root, string desktopId, byte[] ico)
    {
        // An .ico starts with a count at byte 4, then a 16-byte entry per frame: its width (0 for 256), its length at 8 and
        // its offset at 12; the app's frames are PNG images, which the themes take as they are.
        var frames = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var frame = 0; frame < frames; frame++)
        {
            var entry = ico.AsSpan(6 + (16 * frame), 16);
            var png = ico.AsMemory(BinaryPrimitives.ReadInt32LittleEndian(entry[12..]), BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            WriteIfChanged(PathOf(root, desktopId, entry[0] == 0 ? 256 : entry[0]), png);
        }
    }

    /// <summary>Deletes the icon's file of every size from a folder of icon themes.</summary>
    /// <param name="root">The folder that holds the themes.</param>
    /// <param name="desktopId">The name the icon goes by.</param>
    public static void Delete(string root, string desktopId)
    {
        var theme = Path.Join(root, "hicolor");
        if (!Directory.Exists(theme))
        {
            return;
        }

        foreach (var icon in Directory.EnumerateDirectories(theme).Select(size => Path.Join(size, "apps", desktopId + ".png"))
            .Where(File.Exists))
        {
            File.Delete(icon);
        }
    }

    private static string PathOf(string root, string desktopId, int size) =>
        Path.Join(root, "hicolor", $"{size}x{size}", "apps", desktopId + ".png");

    // The services that show the icon are built synchronously, on a thread of the agent's that may wait.
    private static void WriteIfChanged(string path, ReadOnlyMemory<byte> png) =>
        AtomicFile.WriteIfChangedAsync(path, png, CancellationToken.None).GetAwaiter().GetResult();
}
