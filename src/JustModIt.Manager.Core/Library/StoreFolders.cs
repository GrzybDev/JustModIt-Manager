using JustModIt.Manager.Core.Library.Epic;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// Folders that a store's launcher keeps at a game's root for itself: a package never writes into one, and nothing in
/// one is a game file.
/// </summary>
public static class StoreFolders
{
    public static IReadOnlyList<string> Names { get; } = [EpicGameFolder.StoreFolderName];

    /// <summary>Tells whether <paramref name="name"/> is one of these folders, ignoring letter case.</summary>
    /// <param name="name">The name of a folder at a game's root.</param>
    public static bool Contains(string name) => Names.Contains(name, StringComparer.OrdinalIgnoreCase);
}
