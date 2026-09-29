using System.Globalization;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// The parts of <c>appmanifest_&lt;appid&gt;.acf</c> the app uses, from a file that lags behind Steam: transient states
/// are not written and the byte counters go stale during a download.
/// </summary>
public sealed record SteamAppManifest
{
    public required uint AppId { get; init; }

    public string? Name { get; init; }

    /// <summary>A single, validated folder name under <c>steamapps/common</c>.</summary>
    public required string InstallDir { get; init; }

    public SteamAppStates StateFlags { get; init; }

    /// <summary>Null when no build is installed yet (Steam writes 0).</summary>
    public string? BuildId { get; init; }

    /// <summary>The build the pending or last update job targets; null when absent or 0.</summary>
    public string? TargetBuildId { get; init; }

    public ulong SizeOnDisk { get; init; }

    public SteamUpdateResult? UpdateResult { get; init; }

    public ulong BytesDownloaded { get; init; }

    public DateTimeOffset? ScheduledAutoUpdate { get; init; }

    /// <summary>The game language chosen in Steam (an API language code such as <c>polish</c>).</summary>
    public string? Language { get; init; }

    public bool HasPendingTargetBuild =>
        TargetBuildId is not null && !string.Equals(TargetBuildId, BuildId, StringComparison.Ordinal);

    /// <summary>Reads the manifest from a parsed <c>.acf</c> file.</summary>
    /// <param name="document">The parsed file.</param>
    /// <exception cref="FormatException">The document is not a usable app manifest.</exception>
    public static SteamAppManifest Parse(VdfNode document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var state = document["AppState"] is { IsObject: true } node
            ? node
            : throw new FormatException("The manifest has no AppState object.");
        var appId = ParseUInt32(state.GetString("appid")) is > 0 and var id
            ? id
            : throw new FormatException("The manifest has no valid appid.");
        var installDir = state.GetString("installdir") is { } dir && IsSafeFolderName(dir)
            ? dir
            : throw new FormatException($"The manifest of app {appId} has a missing or unsafe installdir.");

        return new SteamAppManifest
        {
            AppId = appId,
            Name = NonEmpty(state.GetString("name")) ?? NonEmpty(state["UserConfig"]?.GetString("name")),
            InstallDir = installDir,
            StateFlags = (SteamAppStates)ParseUInt64(state.GetString("StateFlags")),
            BuildId = NonZero(state.GetString("buildid")),
            TargetBuildId = NonZero(state.GetString("TargetBuildID")),
            SizeOnDisk = ParseUInt64(state.GetString("SizeOnDisk")),
            UpdateResult = int.TryParse(state.GetString("UpdateResult"), NumberStyles.None, CultureInfo.InvariantCulture, out var result)
                ? (SteamUpdateResult)result
                : null,
            BytesDownloaded = ParseUInt64(state.GetString("BytesDownloaded")),
            ScheduledAutoUpdate = ParseUnixTime(state.GetString("ScheduledAutoUpdate")),
            Language = NonEmpty(state["UserConfig"]?.GetString("language")) ?? NonEmpty(state["MountedConfig"]?.GetString("language")),
        };
    }

    /// <summary>Tells whether <paramref name="name"/> is one folder name that stays inside <c>steamapps/common</c>.</summary>
    /// <param name="name">The <c>installdir</c> as Steam wrote it.</param>
    internal static bool IsSafeFolderName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        // Windows drops trailing dots and spaces from a path segment, so "..." would resolve to steamapps/common itself.
        && name.TrimEnd('.', ' ').Length > 0
        && !Path.IsPathRooted(name)
        && name.IndexOfAny(['/', '\\']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static uint ParseUInt32(string? text) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static ulong ParseUInt64(string? text) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static DateTimeOffset? ParseUnixTime(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
        && seconds > 0
        && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static string? NonZero(string? text) => NonEmpty(text) is { } value && value != "0" ? value : null;

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
