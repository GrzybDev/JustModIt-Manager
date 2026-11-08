namespace JustModIt.Manager.Core.Packages;

/// <summary>A folder that lies elsewhere on one game than in the package's layout.</summary>
/// <param name="Package">The folder in the package's layout.</param>
/// <param name="Game">Where it lies on the game.</param>
public sealed record FolderRename(string Package, string Game)
{
    /// <summary>
    /// A package path as it lies on a game: the longest rule that is whole leading segments of the path, the whole path
    /// included and letter case ignored, is replaced once and never again; of rules of one length, the first.
    /// </summary>
    /// <param name="folders">The rules of the game's target; null for none.</param>
    /// <param name="path">A path in the package's layout, with '/' separators.</param>
    public static string Map(IReadOnlyList<FolderRename>? folders, string path)
    {
        var rule = folders?
            .Where(folder => path.Length >= folder.Package.Length
                && (path.Length == folder.Package.Length || path[folder.Package.Length] == PackagePath.Separator)
                && PackagePath.Comparer.Equals(path[..folder.Package.Length], folder.Package))
            .MaxBy(folder => folder.Package.Length);
        return rule is null ? path : rule.Game + path[rule.Package.Length..];
    }
}
