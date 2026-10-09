namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Windows extended-length paths, which native calls need to reach paths over 260 characters whatever the manifest and
/// the system's long path policy say, while the managed file APIs add the prefix themselves.
/// </summary>
internal static class LongPath
{
    private const string DevicePrefix = @"\\?\";
    private const string DeviceNamespacePrefix = @"\\.\";
    private const string UncDevicePrefix = @"\\?\UNC\";
    private const string UncPrefix = @"\\";

    /// <summary>
    /// Gives the full path in <c>\\?\C:\…</c> or <c>\\?\UNC\server\share\…</c> form on Windows; unchanged elsewhere.
    /// </summary>
    /// <param name="path">A full or relative path; one already in device form is kept as it is.</param>
    public static string ToExtended(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows()
            || path.StartsWith(DevicePrefix, StringComparison.Ordinal)
            || path.StartsWith(DeviceNamespacePrefix, StringComparison.Ordinal))
        {
            return path;
        }

        var full = Path.GetFullPath(path);
        return full.StartsWith(UncPrefix, StringComparison.Ordinal)
            ? string.Concat(UncDevicePrefix, full.AsSpan(UncPrefix.Length))
            : DevicePrefix + full;
    }

    /// <summary>Turns <c>\\?\C:\…</c> and <c>\\?\UNC\…</c> back into ordinary paths; other forms are unchanged.</summary>
    /// <param name="path">A path in any form.</param>
    public static string StripPrefix(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith(UncDevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(UncPrefix, path.AsSpan(UncDevicePrefix.Length));
        }

        // Volume GUID paths (\\?\Volume{…}\) have no ordinary form.
        var isDrivePath = path.Length > DevicePrefix.Length + 1 && path[DevicePrefix.Length + 1] == ':';
        return path.StartsWith(DevicePrefix, StringComparison.Ordinal) && isDrivePath ? path[DevicePrefix.Length..] : path;
    }
}
