using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Packages;

/// <summary>Checks the files a manifest declares against its operations.</summary>
internal static class RuntimeDeclarations
{
    /// <summary>The files a manifest declares as they lie on a game, each once and none of them patched.</summary>
    /// <param name="manifest">The manifest as read.</param>
    /// <param name="game">The game it opens for; null for the manifest as written.</param>
    /// <exception cref="InvalidPackageException">A file is declared twice or patched.</exception>
    public static IReadOnlyList<RuntimeFile> From(PackageManifest manifest, GameKey? game)
    {
        var folders = manifest.FoldersOn(game);
        List<RuntimeFile> files = [.. (manifest.RuntimeFiles ?? []).Select(file => file with { Path = FolderRename.Map(folders, file.Path) })];
        // A patch needs the store's content, which the software replaces.
        var patched = manifest.Operations.OfType<PatchOperation>().Select(patch => FolderRename.Map(folders, patch.Path))
            .ToHashSet(PackagePath.Comparer);
        var declared = new HashSet<string>(PackagePath.Comparer);
        foreach (var file in files)
        {
            if (!declared.Add(file.Path) || patched.Contains(file.Path))
            {
                throw new InvalidPackageException(PackageError.InvalidManifest, file.Path);
            }
        }

        return files;
    }
}
