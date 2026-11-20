namespace JustModIt.Manager.Core.Tests.TestSupport;

internal static class TestPaths
{
    /// <summary>A fully qualified path on a drive: <c>E:\…</c> on Windows and the drive's mount point <c>/mnt/e/…</c> elsewhere.</summary>
    /// <param name="drive">The drive's letter.</param>
    /// <param name="segments">The folders and file below the drive's root.</param>
    public static string Rooted(char drive, params string[] segments) =>
        Path.Combine([OperatingSystem.IsWindows() ? $@"{char.ToUpperInvariant(drive)}:\" : $"/mnt/{char.ToLowerInvariant(drive)}", .. segments]);

    /// <summary>A path with its backslashes doubled, as JSON and Steam's text files write it inside quotes.</summary>
    /// <param name="path">The path.</param>
    public static string Escaped(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);
}
