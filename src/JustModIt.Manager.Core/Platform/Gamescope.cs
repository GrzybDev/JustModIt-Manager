namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// gamescope, the compositor of the Steam Deck's Game Mode, which shows each program's window full screen, draws no frame
/// around it and shows only the programs Steam started.
/// </summary>
public static class Gamescope
{
    /// <summary>Tells whether a session is gamescope's, by the desktop names that logind and <c>XDG_CURRENT_DESKTOP</c> give it.</summary>
    /// <param name="desktop">The session's desktop names, separated by colons; null when it names none.</param>
    public static bool IsSession(string? desktop) =>
        desktop is not null && desktop.Split(':').Contains("gamescope", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tells whether the system offers a gamescope session, as SteamOS and the handheld images of other systems do: one of
    /// the desktop sessions it installed is gamescope's.
    /// </summary>
    /// <param name="fileSystem">Lists the sessions.</param>
    /// <param name="dataFolders">The system's data folders, whose <c>wayland-sessions</c> folders list the sessions.</param>
    public static bool IsInstalled(IFileSystem fileSystem, IEnumerable<string> dataFolders) =>
        dataFolders.Select(folder => Path.Join(folder, "wayland-sessions"))
            .Where(fileSystem.DirectoryExists)
            .Any(sessions => fileSystem.Enumerate(sessions, "gamescope*.desktop", new EnumerationOptions()).Any());
}
