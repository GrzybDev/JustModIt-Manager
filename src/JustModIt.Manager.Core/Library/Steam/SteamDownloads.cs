using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// Reads <c>steamapps/downloading</c>, which shows a download or update job long before the app manifest does: Steam
/// keeps an <c>&lt;appid&gt;</c> folder and <c>state_&lt;appid&gt;_*.patch</c> files there until the job ends,
/// including while it is suspended.
/// </summary>
internal static partial class SteamDownloads
{
    private static readonly EnumerationOptions Everything = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    public static IReadOnlySet<uint> ActiveAppIds(IFileSystem fileSystem, string steamAppsDirectory)
    {
        var appIds = new HashSet<uint>();
        try
        {
            foreach (var entry in fileSystem.Enumerate(Path.Join(steamAppsDirectory, SteamPaths.Downloading), "*", Everything))
            {
                var id = entry.IsDirectory ? entry.Name : StatePatchPattern().Match(entry.Name).Groups[1].Value;
                if (uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && appId > 0)
                {
                    appIds.Add(appId);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Without a downloading folder there are no jobs.
        }

        return appIds;
    }

    [GeneratedRegex("^state_([0-9]+)_.*\\.patch$", RegexOptions.CultureInvariant)]
    private static partial Regex StatePatchPattern();
}
