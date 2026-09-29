using System.ComponentModel;
using System.Diagnostics;
using JustModIt.Manager.Core.Games;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Shell;

/// <summary>Opens folders and addresses with what the system uses for them.</summary>
public interface IDesktopShell
{
    /// <summary>Opens a folder or an address with what the system uses for it.</summary>
    /// <param name="target">A folder's path, or an address such as a store's link.</param>
    /// <returns>False when the system could not open it.</returns>
    bool Open(string target);
}

/// <summary>Opens and starts through the Windows shell.</summary>
/// <param name="logger">Receives failures, without the target.</param>
internal sealed partial class DesktopShell(ILogger<DesktopShell> logger) : IDesktopShell
{
    public bool Open(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        // The target is left out of the log, since folders often lie under the user's profile.
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The system could not open a folder or address ({Error})")]
    private partial void LogOpenFailed(string error);
}

/// <summary>The addresses through which a store starts one of its games; GOG Galaxy's opens the game's page to start it from.</summary>
public static class StoreLaunchLinks
{
    /// <returns>Null for a store the app cannot start games with.</returns>
    public static string? For(GameKey game)
    {
        // Ids go into a link the system hands to another program, so only the digits the stores use are let through.
        if (!game.StoreGameId.All(char.IsAsciiDigit))
        {
            return null;
        }

        return game.Store == StoreId.Steam ? $"steam://rungameid/{game.StoreGameId}"
            : game.Store == StoreId.Gog ? $"goggalaxy://openGameView/{game.StoreGameId}"
            : null;
    }
}
