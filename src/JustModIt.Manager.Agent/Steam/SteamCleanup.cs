using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>Takes out of Steam's folder what the app put there, as uninstalling the app does.</summary>
public static class SteamCleanup
{
    /// <summary>
    /// Removes the app's Millennium plugin and its name from the classic mode's switch, each on its own, so one that fails
    /// leaves the other done; the launch options the app added stay, since only Steam's interface can change them.
    /// </summary>
    /// <param name="steamRoot">The folder Steam is installed in.</param>
    /// <param name="environment">The user's folders, where Millennium keeps its plugins on Linux.</param>
    /// <param name="identity">The app's names, which name its plugin and its line in the switch.</param>
    public static void Remove(string steamRoot, LibraryEnvironment environment, AppIdentity identity)
    {
        Try(() => ClassicSwitch.TurnOff(steamRoot, ClassicSwitch.OwnerOf(identity)));
        Try(() => new MillenniumPlugin(MillenniumFolders.For(steamRoot, environment), identity.Slug).RemoveAsync(CancellationToken.None)
            .GetAwaiter().GetResult());
    }

    // An uninstall must go on whatever Steam's folder allows.
    private static void Try(Action remove)
    {
        try
        {
            remove();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
    }
}
