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
}
