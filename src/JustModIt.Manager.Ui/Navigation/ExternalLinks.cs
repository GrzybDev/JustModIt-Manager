using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>Opens addresses outside the window: the site's pages in the browser and a store's page in its client.</summary>
public interface IExternalLinks
{
    /// <summary>Opens an address with the program the system has for it; anything but https and a store's link is ignored.</summary>
    /// <param name="address">The address to open.</param>
    void Open(Uri address);
}

/// <summary>Hands addresses to the system's shell on the thread pool, since starting a program may take a while.</summary>
/// <param name="logger">Where an address that could not be opened is logged.</param>
public sealed partial class ShellLinks(ILogger<ShellLinks> logger) : IExternalLinks
{
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsAllowed(address))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                LogFailed(logger, address, exception);
            }
        });
    }

    /// <summary>Tells whether an address is one the window opens: https, or a store client's own link.</summary>
    /// <param name="address">The address.</param>
    public static bool IsAllowed(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsAbsoluteUri && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == PageLinks.SteamScheme);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The address {Address} could not be opened")]
    private static partial void LogFailed(ILogger logger, Uri address, Exception exception);
}

/// <summary>The site's pages and the stores' pages that the window links to.</summary>
public static class PageLinks
{
    /// <summary>The scheme of the Steam client's links.</summary>
    public const string SteamScheme = "steam";

    /// <summary>The game's page in its store's client; null for a store the window has no link for.</summary>
    /// <param name="game">The game.</param>
    public static Uri? StorePage(Core.Games.GameKey game) =>
        game.Store == Core.Games.StoreId.Steam && int.TryParse(game.StoreGameId, out var id)
            ? new Uri(FormattableString.Invariant($"{SteamScheme}://nav/games/details/{id}"))
            : null;

    /// <summary>A project's page on the site; null when the app knows no site address.</summary>
    /// <param name="site">The site's address.</param>
    /// <param name="gameSlug">The game's slug.</param>
    /// <param name="teamSlug">The team's slug.</param>
    public static Uri? ProjectPage(Uri? site, string gameSlug, string teamSlug) =>
        site is null || gameSlug.Length == 0 || teamSlug.Length == 0
            ? null
            : new Uri(site, $"games/{Uri.EscapeDataString(gameSlug)}/{Uri.EscapeDataString(teamSlug)}");

    /// <summary>A team's page on the site; null when the app knows no site address.</summary>
    /// <param name="site">The site's address.</param>
    /// <param name="teamSlug">The team's slug.</param>
    public static Uri? TeamPage(Uri? site, string teamSlug) =>
        site is null || teamSlug.Length == 0 ? null : new Uri(site, $"teams/{Uri.EscapeDataString(teamSlug)}");

    /// <summary>The site's help; with a section, that part of it.</summary>
    /// <param name="site">The site's address.</param>
    /// <param name="section">The anchor of a part of the help, e.g. the projects' statuses; null for the top.</param>
    public static Uri? Help(Uri? site, string? section = null) =>
        site is null ? null : new Uri(site, section is null ? "help" : "help#" + section);
}
