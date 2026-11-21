using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>The running Steam client that a window in Game Mode hands its links to, since gamescope shows only what Steam opens.</summary>
public interface IRunningSteam
{
    /// <summary>Hands the running client one of its own links, off the UI thread; it never starts Steam.</summary>
    /// <param name="link">The link, such as <c>steam://openurl/https://example.com/</c>.</param>
    void Send(string link);
}

/// <summary>The client behind <c>~/.steam</c>, reached through its own program, which forwards a link to the running client.</summary>
/// <param name="logger">Where a link that could not be handed over is logged.</param>
public sealed class RunningSteam(ILogger<RunningSteam> logger) : IRunningSteam
{
    private readonly Lazy<string?> _program =
        new(() => SteamInstallation.ClientProgram(PhysicalFileSystem.Instance, LibraryEnvironment.ForCurrentUser()));

    public void Send(string link) => ShellLinks.Start(link,
        () => _program.Value is { } program ? ShellLinks.StartOfProgram(program, "-ifrunning", link) : null, logger);
}

/// <summary>
/// Opens addresses in Game Mode through Steam, a page in Steam's browser, which gamescope shows while a browser the system
/// starts would stay unseen, and Steam's own links in Steam.
/// </summary>
/// <param name="steam">The Steam client the window runs beside.</param>
public sealed class SteamLinks(IRunningSteam steam) : IExternalLinks
{
    public void Open(Uri? address)
    {
        if (address is not null && ShellLinks.IsAllowed(address))
        {
            // Steam takes the address after its own link's path as it is, so it is joined as text.
            steam.Send(address.Scheme == PageLinks.SteamScheme
                ? address.AbsoluteUri
                : $"{PageLinks.SteamScheme}://openurl/{address.AbsoluteUri}");
        }
    }

    // Other clients and folders open nothing, since Game Mode hides what asks for them.
    public void Open(ClientLink? client)
    {
        if (client?.Address.StartsWith($"{PageLinks.SteamScheme}:", StringComparison.Ordinal) == true)
        {
            steam.Send(client.Address);
        }
    }

    public void OpenFolder(string folder)
    {
    }
}
