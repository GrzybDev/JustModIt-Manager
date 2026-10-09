namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>One library listed in <c>libraryfolders.vdf</c>.</summary>
/// <param name="Path">The library's folder as Steam wrote it.</param>
/// <param name="IsMounted">Null unless a transitional client wrote a <c>mounted</c> flag.</param>
internal sealed record SteamLibraryFolder(string Path, bool? IsMounted = null);

/// <summary>
/// Reads <c>libraryfolders.vdf</c> in the current format (an object per library) and the pre-2021 one (a plain path per
/// library, with the Steam root left implicit).
/// </summary>
internal static class SteamLibraryFolders
{
    /// <summary>Reads the libraries listed in a parsed <c>libraryfolders.vdf</c>.</summary>
    /// <param name="document">The parsed file.</param>
    /// <exception cref="FormatException">The document has no <c>libraryfolders</c> object.</exception>
    public static IReadOnlyList<SteamLibraryFolder> Parse(VdfNode document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document["libraryfolders"] is { IsObject: true } node
            ? node
            : throw new FormatException("The file has no libraryfolders object.");

        var folders = new List<SteamLibraryFolder>();
        foreach (var (key, entry) in root.Children)
        {
            // Only numbered keys are libraries; the rest are statistics such as ContentStatsID.
            if (key.Length == 0 || !key.All(char.IsAsciiDigit))
            {
                continue;
            }

            var folder = entry.IsObject
                ? new SteamLibraryFolder(entry.GetString("path") ?? string.Empty, ParseMounted(entry.GetString("mounted")))
                : new SteamLibraryFolder(entry.Value ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(folder.Path))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    private static bool? ParseMounted(string? value) => value switch
    {
        "0" => false,
        "1" => true,
        _ => null,
    };
}
