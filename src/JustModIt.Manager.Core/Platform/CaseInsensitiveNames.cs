using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// Finds names in folders ignoring letter case, as Windows and Wine find a game's files, also on a file system that tells
/// case apart; there a folder it has to list is listed once and its listing kept, so an instance serves one look at a tree.
/// </summary>
/// <param name="fileSystem">Reads the folders.</param>
public sealed class CaseInsensitiveNames(IFileSystem fileSystem)
{
    private static readonly bool IgnoresCase = PathComparison.Comparison == StringComparison.OrdinalIgnoreCase;

    private readonly Dictionary<string, ILookup<string, FileEntry>> _folders = new(PathComparison.Comparer);

    /// <summary>The entry a name finds in a folder: the one spelled exactly, else the only one spelled otherwise.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="name">The entry's name in any letter case.</param>
    /// <returns>Null when there is none, or several and none spelled exactly.</returns>
    public FileEntry? Find(string folder, string name)
    {
        var exact = fileSystem.GetEntry(Path.Join(folder, name));
        if (exact is not null || IgnoresCase)
        {
            return exact;
        }

        var matches = Matches(folder, name).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Whether a folder holds several entries whose names differ from this one only in letter case.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="name">The name in any letter case.</param>
    public bool HasTwins(string folder, string name) => !IgnoresCase && Matches(folder, name).Skip(1).Any();

    /// <summary>
    /// The full path a game-relative path leads to, each segment spelled as <see cref="Find"/> finds it, or as written where
    /// it finds nothing.
    /// </summary>
    /// <param name="root">The folder the path is relative to.</param>
    /// <param name="relativePath">A path with '/' separators.</param>
    public string Resolve(string root, string relativePath)
    {
        if (IgnoresCase)
        {
            return PackagePath.ToFullPath(root, relativePath);
        }

        var path = root;
        foreach (var name in relativePath.Split(PackagePath.Separator))
        {
            path = Find(path, name)?.FullPath ?? Path.Join(path, name);
        }

        return path;
    }

    private IEnumerable<FileEntry> Matches(string folder, string name)
    {
        if (!_folders.TryGetValue(folder, out var names))
        {
            names = fileSystem.TryList(folder, "*", FileListing.Everything).ToLookup(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
            _folders[folder] = names;
        }

        return names[name];
    }
}
