using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>What <c>steamapps/downloading</c> holds for an app.</summary>
public enum SteamDownload
{
    None,

    /// <summary>
    /// Changed within <see cref="SteamDownloads.ActiveWindow"/>, so it may be a job the manifest does not show yet; a change
    /// further in the future than <see cref="SafetyGate.ClockSkewTolerance"/> comes from a wrong clock and does not count.
    /// </summary>
    Active,

    /// <summary>Left behind by a cancelled or abandoned job; Steam may still apply it later.</summary>
    Leftover,
}

/// <summary>
/// Reads <c>steamapps/downloading</c>, where Steam keeps an <c>&lt;appid&gt;</c> folder and
/// <c>state_&lt;appid&gt;_*.patch</c> files from before the manifest shows a job until it ends, suspended or not, and
/// leaves them behind when the job is cancelled or abandoned.
/// </summary>
internal static partial class SteamDownloads
{
    /// <summary>
    /// Steam names a job it starts in the manifest or the registry within seconds, so entries that stayed unchanged for
    /// longer without either are left over; longer than the jobs' quiet period, so no job writes right after Steam touched
    /// them.
    /// </summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);

    private static readonly EnumerationOptions Everything = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    /// <summary>Tells when the entries of each app in <c>steamapps/downloading</c> last changed, by app id.</summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="steamAppsDirectory">The library's <c>steamapps</c> folder.</param>
    public static Dictionary<uint, DateTimeOffset> LastChanges(IFileSystem fileSystem, string steamAppsDirectory)
    {
        var changes = new Dictionary<uint, DateTimeOffset>();
        try
        {
            foreach (var entry in fileSystem.Enumerate(Path.Join(steamAppsDirectory, SteamPaths.Downloading), "*", Everything))
            {
                var id = entry.IsDirectory ? entry.Name : StatePatchPattern().Match(entry.Name).Groups[1].Value;
                if (uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && appId > 0
                    && !(changes.TryGetValue(appId, out var later) && later >= entry.LastWriteTimeUtc))
                {
                    changes[appId] = entry.LastWriteTimeUtc;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Without a downloading folder there are no jobs.
        }

        return changes;
    }

    /// <summary>Classifies an app's entries in <c>steamapps/downloading</c> by when they last changed.</summary>
    /// <param name="changedAt">When the app's entries last changed; null when it has none.</param>
    /// <param name="now">The current time.</param>
    public static SteamDownload Classify(DateTimeOffset? changedAt, DateTimeOffset now) => changedAt switch
    {
        null => SteamDownload.None,
        { } changed when changed + ActiveWindow > now && changed - now <= SafetyGate.ClockSkewTolerance => SteamDownload.Active,
        _ => SteamDownload.Leftover,
    };

    [GeneratedRegex("^state_([0-9]+)_.*\\.patch$", RegexOptions.CultureInvariant)]
    private static partial Regex StatePatchPattern();
}
