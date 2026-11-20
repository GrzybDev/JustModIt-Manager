using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>The folder Steam is installed in and where Millennium works for it.</summary>
/// <param name="Root">The folder Steam is installed in, which holds the classic mode's switch.</param>
/// <param name="Millennium">Where Millennium keeps its plugins and its settings for this Steam.</param>
internal sealed record SteamFolders(string Root, MillenniumFolders Millennium);

/// <summary>Where Millennium keeps its plugins and its settings, and where it is installed.</summary>
/// <param name="Plugins">The folder of the plugins Millennium loads.</param>
/// <param name="ConfigFile">Millennium's settings, which list the plugins it loads.</param>
/// <param name="Library">Millennium's own files where they lie outside Steam's folder, as on Linux; null where they lie inside.</param>
internal sealed record MillenniumFolders(string Plugins, string ConfigFile, string? Library)
{
    // Millennium's Linux installer puts its files there, and its plugins and settings follow the XDG folders.
    private const string LinuxLibrary = "/usr/lib/millennium";

    /// <summary>
    /// Whether Millennium is installed; on Linux its settings outlive it in the user's folders, so its files must be there too.
    /// </summary>
    public bool IsInstalled => File.Exists(ConfigFile) && (Library is null || Directory.Exists(Library));

    /// <summary>Millennium's folders for a Steam: inside Steam's folder on Windows, in the user's folders on Linux.</summary>
    /// <param name="steamRoot">The folder Steam is installed in.</param>
    /// <param name="environment">The user's folders.</param>
    public static MillenniumFolders For(string steamRoot, LibraryEnvironment environment)
    {
        if (environment.Platform != AppPlatform.Linux)
        {
            return new MillenniumFolders(Path.Join(steamRoot, "millennium", "plugins"),
                Path.Join(steamRoot, "millennium", "config", "config.json"), null);
        }

        return new MillenniumFolders(Path.Join(environment.DataDirectory, "millennium", "plugins"),
            Path.Join(environment.ConfigDirectory, "millennium", "config.json"), LinuxLibrary);
    }
}
