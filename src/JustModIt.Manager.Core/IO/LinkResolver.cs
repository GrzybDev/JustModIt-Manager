namespace JustModIt.Manager.Core.IO;

/// <summary>
/// A component-wise realpath: <see cref="FileSystemInfo.ResolveLinkTarget"/> follows only the last component, but any
/// ancestor may be a link as well.
/// </summary>
internal static class LinkResolver
{
    // Linux's MAXSYMLINKS, so a link cycle ends instead of looping forever.
    private const int MaxLinkHops = 40;

    /// <summary>Resolves every link along <paramref name="fullPath"/>.</summary>
    /// <param name="fullPath">A full path; parts that do not exist are kept as given.</param>
    public static string Resolve(string fullPath)
    {
        var current = Path.GetPathRoot(fullPath) ?? string.Empty;
        var pending = new Stack<string>();
        PushSegments(pending, fullPath, current);
        var hops = 0;
        while (pending.TryPop(out var segment))
        {
            var candidate = Path.Join(current, segment);
            if (TryReadLink(candidate) is not { } target || ++hops > MaxLinkHops)
            {
                current = candidate;
                continue;
            }

            var resolved = Path.GetFullPath(LongPath.StripPrefix(target), current);
            current = Path.GetPathRoot(resolved) ?? string.Empty;
            PushSegments(pending, resolved, current);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static void PushSegments(Stack<string> pending, string path, string root)
    {
        var segments = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            pending.Push(segments[i]);
        }
    }

    private static string? TryReadLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
