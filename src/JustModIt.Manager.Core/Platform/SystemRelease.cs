namespace JustModIt.Manager.Core.Platform;

/// <summary>The operating system as logs and reports name it, e.g. <c>SteamOS 3.8.28</c>, with nothing that names the computer.</summary>
/// <param name="Name">The system's name, on Linux the distribution's.</param>
/// <param name="Version">The system's version; null for a distribution that names none, such as a rolling one.</param>
/// <param name="Kernel">The Linux kernel's version; null on Windows.</param>
public sealed record SystemRelease(string Name, string? Version, string? Kernel)
{
    // The os-release specification: a distribution's own file is in /usr/lib, and one in /etc takes its place.
    private static readonly string[] OsReleaseFiles = ["/etc/os-release", "/usr/lib/os-release"];

    /// <summary>Names the system this process runs on.</summary>
    public static SystemRelease Current()
    {
        // On Linux the runtime takes a fourth number from the kernel's vendor suffix, as in 6.11.11-valve24.
        var version = Environment.OSVersion.Version.ToString(3);
        return OperatingSystem.IsWindows() ? new SystemRelease("Windows", version, Kernel: null) : Parse(ReadOsRelease(), version);
    }

    /// <summary>Names a Linux system after its os-release file.</summary>
    /// <param name="text">The file's text; empty when the system has none.</param>
    /// <param name="kernel">The kernel's version.</param>
    internal static SystemRelease Parse(string text, string kernel)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('#') && line.Split('=', 2) is [var key, var value])
            {
                values[key] = value is [var quote and ('"' or '\''), .., var end] && end == quote ? value[1..^1] : value;
            }
        }

        return new SystemRelease(values.GetValueOrDefault("NAME") ?? values.GetValueOrDefault("PRETTY_NAME") ?? "Linux",
            values.GetValueOrDefault("VERSION_ID"), kernel);
    }

    public override string ToString() => Version is null ? Name : $"{Name} {Version}";

    private static string ReadOsRelease()
    {
        foreach (var path in OsReleaseFiles)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return "";
    }
}
