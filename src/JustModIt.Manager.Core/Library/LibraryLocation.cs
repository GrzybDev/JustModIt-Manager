namespace JustModIt.Manager.Core.Library;

public enum LibraryState
{
    Online,

    /// <summary>The drive or folder is unavailable; its games are kept as offline rather than treated as uninstalled.</summary>
    Offline,

    /// <summary>The library's records could not be read this time; its games are kept from the last good scan.</summary>
    Unreadable,
}

/// <summary>
/// A place a store keeps records of installed games: a Steam library folder, the GOG entries in the registry, a
/// launcher's list of installs or a folder the player added.
/// </summary>
/// <param name="Id">Stable across scans: the canonical folder or file, or a name for a registry source.</param>
/// <param name="Source">Which kind of library this is, as the provider names it (for example <c>steam-library</c>).</param>
/// <param name="Path">The folder or file to show the player; null for the registry.</param>
/// <param name="State">Whether the library could be reached and read this time.</param>
/// <param name="IsManual">A folder the player added (see <see cref="Settings.LibraryFolder"/>).</param>
/// <param name="UnreadableEntries">Records in the library that could not be read, such as damaged manifests.</param>
public sealed record LibraryLocation(
    string Id,
    string Source,
    string? Path,
    LibraryState State,
    bool IsManual = false,
    int UnreadableEntries = 0)
{
    /// <summary>Online, with every record in it read.</summary>
    public bool IsFullyRead => State == LibraryState.Online && UnreadableEntries == 0;
}
