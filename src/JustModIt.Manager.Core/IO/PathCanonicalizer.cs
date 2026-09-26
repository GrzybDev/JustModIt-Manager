using System.Runtime.Versioning;
using JustModIt.Manager.Core.Interop;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Gives the one spelling of a path (links, junctions, SUBST drives and 8.3 names resolved, on Windows in the casing
/// found on disk), which records and snapshots store so the same folder reached two ways compares equal.
/// </summary>
public static class PathCanonicalizer
{
    /// <summary>
    /// Gives the canonical spelling of <paramref name="path"/>; it opens handles, so call it off the UI thread.
    /// </summary>
    /// <param name="path">The path; a missing tail is kept as given after its longest existing ancestor is resolved.</param>
    public static string Canonicalize(string path)
    {
        var full = PathComparison.Normalize(path);
        return OperatingSystem.IsWindows() ? ResolveOnWindows(full) : LinkResolver.Resolve(full);
    }

    [SupportedOSPlatform("windows")]
    private static string ResolveOnWindows(string fullPath)
    {
        var existing = fullPath;
        while (!Path.Exists(existing))
        {
            if (Path.GetDirectoryName(existing) is not { } parent)
            {
                return fullPath;
            }

            existing = parent;
        }

        if (WindowsFileApi.TryGetFinalPath(existing) is not { } resolved)
        {
            return fullPath;
        }

        var tail = fullPath.AsSpan(existing.Length).TrimStart(Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(tail.IsEmpty ? resolved : Path.Join(resolved.AsSpan(), tail));
    }
}
