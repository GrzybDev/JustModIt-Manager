using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>The app's icon as a file of an icon theme, where the desktop's tray, notifications and menus look it up by name.</summary>
public static class AppIcon
{
    /// <summary>Writes the icon into a folder of icon themes, unless the file holds it already.</summary>
    /// <param name="root">The folder that holds the themes.</param>
    /// <param name="desktopId">The name the icon goes by, which is the app's desktop entry's.</param>
    /// <param name="png">The icon, 256 px square.</param>
    /// <returns>The icon's file.</returns>
    public static string Write(string root, string desktopId, byte[] png)
    {
        var path = Path.Join(root, "hicolor", "256x256", "apps", desktopId + ".png");
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(png))
        {
            // The services that show the icon are built synchronously, on a thread of the agent's that may wait.
            AtomicFile.WriteAllBytesAsync(path, png, CancellationToken.None).GetAwaiter().GetResult();
        }

        return path;
    }
}
