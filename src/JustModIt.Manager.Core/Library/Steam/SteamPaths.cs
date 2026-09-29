using System.Security;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

internal static class SteamPaths
{
    public const string SteamApps = "steamapps";
    public const string Common = "common";
    public const string Downloading = "downloading";
    public const string Music = "music";
    public const string Config = "config";
    public const string LibraryFoldersFile = "libraryfolders.vdf";
    public const string ManifestPattern = "appmanifest_*.acf";

    private static readonly EnumerationOptions ChildSearch = new()
    {
        // Linux reports dot folders as hidden, and the default options would skip them.
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
    };

    /// <summary>
    /// Finds the <c>steamapps</c> folder in any letter case, since very old Linux clients created <c>SteamApps</c>; null
    /// when there is none.
    /// </summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="directory">A Steam root or library folder.</param>
    public static string? FindSteamApps(IFileSystem fileSystem, string directory) =>
        FindChildDirectory(fileSystem, directory, SteamApps);

    /// <summary>
    /// Finds the existing subfolder with this name, preferring the exact spelling, since on case-sensitive file systems
    /// the name may differ in case from what Steam recorded (e.g. an installdir written on Windows); null when none exists.
    /// </summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="directory">The folder to look in.</param>
    /// <param name="name">The subfolder's name as Steam recorded it.</param>
    public static string? FindChildDirectory(IFileSystem fileSystem, string directory, string name)
    {
        var exact = Path.Join(directory, name);
        if (fileSystem.DirectoryExists(exact))
        {
            return exact;
        }

        if (PathComparison.Comparison == StringComparison.OrdinalIgnoreCase)
        {
            return null;
        }

        try
        {
            // The name is also a search pattern, so wildcard characters in it are ruled out by the exact comparison.
            foreach (var entry in fileSystem.Enumerate(directory, name, ChildSearch))
            {
                if (entry.IsDirectory && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.FullPath;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
