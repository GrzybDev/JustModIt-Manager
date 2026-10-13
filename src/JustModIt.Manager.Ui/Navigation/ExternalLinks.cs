using System.ComponentModel;
using System.Diagnostics;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>
/// Opens addresses and folders outside the window: the site's pages in the browser, stores' pages and clients, and a
/// folder in the file manager.
/// </summary>
public interface IExternalLinks
{
    /// <summary>Opens an address with the program the system has for it; anything but https and Steam's links is ignored.</summary>
    /// <param name="address">The address to open.</param>
    void Open(Uri address);

    /// <summary>Starts a store client through its own link.</summary>
    /// <param name="client">The client and its link.</param>
    void Open(ClientLink client);

    /// <summary>Shows a folder in the system's file manager; a path that is not fully qualified is ignored.</summary>
    /// <param name="folder">The folder's full path.</param>
    void OpenFolder(string folder);
}

/// <summary>
/// A store client's own link, which only <see cref="PageLinks"/> builds from what detection found, so nothing the site sends
/// can start a program through it.
/// </summary>
public sealed class ClientLink
{
    /// <summary>Makes the link of a client the window found.</summary>
    /// <param name="name">The client's name, as the player knows it.</param>
    /// <param name="address">The link exactly as the client takes it.</param>
    internal ClientLink(string name, string address)
    {
        Name = name;
        Address = address;
    }

    /// <summary>The client's name, as the player knows it.</summary>
    public string Name { get; }

    /// <summary>The link exactly as the client takes it, since a <see cref="Uri"/> would lower-case a command in its host.</summary>
    public string Address { get; }
}

/// <summary>
/// Hands addresses and folders to the system's shell on the thread pool, since starting a program may take a while, and
/// a folder on a sleeping drive longer still.
/// </summary>
/// <param name="logger">Where an address or folder that could not be opened is logged.</param>
public sealed partial class ShellLinks(ILogger<ShellLinks> logger) : IExternalLinks
{
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (IsAllowed(address))
        {
            Start(address.AbsoluteUri);
        }
    }

    public void OpenFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (Path.IsPathFullyQualified(folder))
        {
            Start(folder);
        }
    }

    public void Open(ClientLink client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Start(client.Address);
    }

    /// <summary>
    /// Tells whether an address is one the window opens: https, or a link of Steam's client, which may come from anywhere since
    /// it only shows Steam's pages; other clients open only through a <see cref="ClientLink"/>.
    /// </summary>
    /// <param name="address">The address.</param>
    public static bool IsAllowed(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsAbsoluteUri && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == PageLinks.SteamScheme);
    }

    private void Start(string target) => _ = Task.Run(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogFailed(logger, target, exception);
        }
    });

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Target} could not be opened")]
    private static partial void LogFailed(ILogger logger, string target, Exception exception);
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

    /// <summary>
    /// The client that detection found for a store, opened on its library: the store's own where it is there, otherwise
    /// Heroic; null when it found none.
    /// </summary>
    /// <param name="store">What detection found for the store.</param>
    public static ClientLink? StoreClient(StoreDetectionView store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!store.IsFound)
        {
            return null;
        }

        if (store.Store == Core.Games.StoreId.Steam.Value)
        {
            return new ClientLink(Strings.Library_StoreSteam, $"{SteamScheme}://open/games");
        }

        bool Has(string source) => store.Libraries.Any(library => library.Source == source);
        return Has("gog-galaxy") ? new ClientLink(Strings.Undetected_ClientGalaxy, "goggalaxy://openGalaxyClient")
            : Has("epic-launcher") ? new ClientLink(Strings.Undetected_ClientEpicLauncher, "com.epicgames.launcher://store/library")
            // Heroic comes to the front for any of its links, and a ping only writes to its log.
            : Has("heroic") ? new ClientLink(Strings.Undetected_ClientHeroic, "heroic://ping")
            : null;
    }

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

    /// <summary>The site's dashboard, where teams publish releases; null when the app knows no site address.</summary>
    /// <param name="site">The site's address.</param>
    public static Uri? Dashboard(Uri? site) => site is null ? null : new Uri(site, "dashboard");

    /// <summary>The site's help; with a section, that part of it.</summary>
    /// <param name="site">The site's address.</param>
    /// <param name="section">The anchor of a part of the help, e.g. the projects' statuses; null for the top.</param>
    public static Uri? Help(Uri? site, string? section = null) =>
        site is null ? null : new Uri(site, section is null ? "help" : "help#" + section);
}
