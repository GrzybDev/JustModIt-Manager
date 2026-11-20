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

    /// <summary>
    /// Finds the <c>steamapps</c> folder in any letter case, since very old Linux clients created <c>SteamApps</c>; null
    /// when there is none.
    /// </summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="directory">A Steam root or library folder.</param>
    public static string? FindSteamApps(IFileSystem fileSystem, string directory) =>
        FindChildDirectory(fileSystem, directory, SteamApps);

    /// <summary>
    /// Finds the existing subfolder with this name in any letter case, the exact spelling first, since on case-sensitive file
    /// systems the name may differ in case from what Steam recorded (e.g. an installdir written on Windows); null when none
    /// exists or several differ only in case.
    /// </summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="directory">The folder to look in.</param>
    /// <param name="name">The subfolder's name as Steam recorded it.</param>
    public static string? FindChildDirectory(IFileSystem fileSystem, string directory, string name) =>
        new CaseInsensitiveNames(fileSystem).Find(directory, name) is { IsDirectory: true } entry ? entry.FullPath : null;
}
