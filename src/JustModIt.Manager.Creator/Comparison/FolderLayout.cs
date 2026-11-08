using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>Where a project's folders lie on one version of the game; empty while it has the project's layout.</summary>
/// <param name="Renames">The project's folders that lie elsewhere on the version.</param>
public readonly record struct FolderLayout(ValueList<FolderRename> Renames)
{
    private readonly ValueList<FolderRename> _toPackage = [.. Renames.Select(rename => new FolderRename(rename.Game, rename.Package))];

    /// <summary>A project path as it lies on the version.</summary>
    /// <param name="path">In the project's layout, with '/' separators.</param>
    public string ToGame(string path) => FolderRename.Map(Renames, path);

    /// <summary>A path on the version as the project calls it; a path already in the project's layout stays as it is.</summary>
    /// <param name="path">In the version's layout, with '/' separators.</param>
    public string ToPackage(string path) => FolderRename.Map(_toPackage, path);

    /// <summary>
    /// A version's listing keyed by project paths: by its stored rule while the rule still fits (its folders are there and the
    /// project's are not), else by one detected against the reference, so a store update that renames a folder is found again.
    /// </summary>
    /// <param name="stored">The version's stored rule; empty while none is known.</param>
    /// <param name="index">The version's game as listed.</param>
    /// <param name="reference">
    /// The first version on this computer, resolved, so the project's layout holds after the first version left; null for that one.
    /// </param>
    public static GameIndex Resolve(ValueList<FolderRename> stored, GameIndex index, GameIndex? reference) =>
        index.InLayout(reference is null || (stored.Count > 0 && stored.All(rule => index.IsDirectory(rule.Game) && !index.IsDirectory(rule.Package)))
            ? new FolderLayout(stored)
            : Detect(reference, index));

    /// <summary>
    /// Pairs the top-most folders only the project's version has with those only the other has, when they share at least
    /// half of the first folder's files by relative path. A case-only difference is none, and the parents of a pair may
    /// differ, since a rule on the leading segments also expresses a moved folder.
    /// </summary>
    /// <param name="first">The version whose layout is the project's.</param>
    /// <param name="other">The version to lay out.</param>
    internal static FolderLayout Detect(GameIndex first, GameIndex other)
    {
        var theirs = TopOnly(other, first).Select(folder => (Folder: folder, Files: other.FilesBelow(folder).ToList())).ToList();
        var pairs = new List<(string Package, string Game, int Shared)>();
        foreach (var folder in TopOnly(first, other))
        {
            var files = first.FilesBelow(folder).ToHashSet(PackagePath.Comparer);
            pairs.AddRange(theirs.Select(their => (Package: folder, Game: their.Folder, Shared: their.Files.Count(files.Contains)))
                .Where(pair => pair.Shared > 0 && pair.Shared * 2 >= files.Count));
        }

        // With a rival that fits as well, either could be meant, and copies carry no check that would catch a wrong rule.
        return new FolderLayout(
        [
            .. pairs.Where(pair => !pairs.Exists(rival => rival != pair && rival.Shared >= pair.Shared
                    && (PackagePath.Comparer.Equals(rival.Package, pair.Package) || PackagePath.Comparer.Equals(rival.Game, pair.Game))))
                .Select(pair => new FolderRename(pair.Package, pair.Game)),
        ]);
    }

    private static IEnumerable<string> TopOnly(GameIndex index, GameIndex other) =>
        index.Directories.Where(folder => !other.IsDirectory(folder) && other.IsDirectory(GameIndex.DirectoryOf(folder)))
            .Order(StringComparer.Ordinal);
}
