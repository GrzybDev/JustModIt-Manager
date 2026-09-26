namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Compares paths the way the platform's default file systems do without touching the disk, so links are not resolved;
/// compare canonical paths (<see cref="PathCanonicalizer"/>) when links matter.
/// </summary>
public static class PathComparison
{
    // NTFS and APFS ignore case by default, ext4 and most other Linux file systems do not.
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } =
        Comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Gives the full path without an extended-length prefix and without a trailing separator (except for a root).
    /// </summary>
    /// <param name="path">A path in any form; a relative one resolves against the current folder.</param>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(LongPath.StripPrefix(path)));
    }

    /// <summary>Tells whether two paths name the same place once normalized.</summary>
    /// <param name="first">One path.</param>
    /// <param name="second">The other path.</param>
    public static bool AreSame(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), Comparison);

    /// <summary>True when <paramref name="path"/> is <paramref name="directory"/> itself or lies anywhere below it.</summary>
    /// <param name="directory">The folder.</param>
    /// <param name="path">The path to test.</param>
    public static bool IsWithin(string directory, string path)
    {
        var parent = Normalize(directory);
        var child = Normalize(path);
        return child.StartsWith(parent, Comparison)
            && (child.Length == parent.Length
                || Path.EndsInDirectorySeparator(parent)
                || child[parent.Length] == Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Tells whether either folder lies within the other, since nested folders share files and an operation on either
    /// one touches the other.
    /// </summary>
    /// <param name="first">One folder.</param>
    /// <param name="second">The other folder.</param>
    public static bool Overlaps(string first, string second) => IsWithin(first, second) || IsWithin(second, first);
}
