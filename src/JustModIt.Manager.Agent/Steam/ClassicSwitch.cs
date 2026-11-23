using JustModIt.Manager.Core;
using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The file in Steam's folder whose presence makes Steam open its local debug port at its start, for whichever account
/// starts that Steam. Each copy of the app that turns it on writes its name with its account into it, so a copy takes
/// away only a file the copies made and only once none needs it, and never one the player made, which holds no name.
/// </summary>
internal static class ClassicSwitch
{
    private const string FileName = ".cef-enable-remote-debugging";

    /// <summary>The line a copy of the app keeps in the file: its instance name and the account it runs for.</summary>
    /// <param name="identity">The app's names.</param>
    public static string OwnerOf(AppIdentity identity) => $"{identity.InstanceName}.{InstanceNames.CurrentUserId()}";

    /// <summary>Puts the file in place for a copy of the app.</summary>
    /// <param name="steamRoot">The folder Steam is installed in.</param>
    /// <param name="owner">The copy's line, as <see cref="OwnerOf"/> gives it.</param>
    /// <returns>Whether the file was missing, so a Steam that runs now started without it.</returns>
    public static bool TurnOn(string steamRoot, string owner)
    {
        var path = Path.Join(steamRoot, FileName);
        if (Owners(path) is not { } owners)
        {
            File.WriteAllText(path, owner + "\n");
            return true;
        }

        if (owners.Count > 0 && !owners.Contains(owner))
        {
            File.WriteAllLines(path, [.. owners, owner]);
        }

        return false;
    }

    /// <summary>
    /// Takes a copy of the app off the file, removing the file once no copy needs it, unless Decky Loader keeps it, which
    /// leaves the file empty, as Decky makes it.
    /// </summary>
    /// <param name="steam">The folder Steam is installed in, and whether Decky Loader is installed.</param>
    /// <param name="owner">The copy's line, as <see cref="OwnerOf"/> gives it.</param>
    public static void TurnOff(SteamFolders steam, string owner)
    {
        var path = Path.Join(steam.Root, FileName);
        if (Owners(path) is not { } owners || !owners.Remove(owner))
        {
            return;
        }

        if (owners.Count == 0 && !steam.HasDecky)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllLines(path, owners);
        }
    }

    // Null when there is no file; empty when the player made it.
    private static List<string>? Owners(string path)
    {
        try
        {
            return [.. File.ReadAllLines(path).Where(line => line.Length > 0)];
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
