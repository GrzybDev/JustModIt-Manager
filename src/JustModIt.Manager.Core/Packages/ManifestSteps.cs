using System.Buffers;
using System.IO.Compression;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Packages;

/// <summary>Turns a manifest's operations into validated package steps.</summary>
internal static class ManifestSteps
{
    private const int Sha256HexLength = 64;

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>The steps of a manifest, checked against the archive.</summary>
    /// <param name="manifest">The manifest as read.</param>
    /// <param name="files">The archive's file entries by normalised name.</param>
    /// <param name="game">
    /// The game it opens for, whose target's rules the game paths follow and whose store limits the operations; null for the
    /// manifest as written.
    /// </param>
    /// <exception cref="InvalidPackageException">An operation is unsafe or does not match the archive, or none runs on the game.</exception>
    public static IReadOnlyList<PackageStep> From(PackageManifest manifest, IReadOnlyDictionary<string, ZipArchiveEntry> files, GameKey? game)
    {
        // The serializer checks the nullability of properties but not of list items.
        if (manifest.Targets.Any(target => target is null || string.IsNullOrWhiteSpace(target.Id) || target.Folders?.All(FolderRename.IsSafe) == false)
            || manifest.Operations.Any(operation => operation.Only is { Count: 0 }))
        {
            throw new InvalidPackageException(PackageError.InvalidManifest, "targets");
        }

        var folders = manifest.FoldersOn(game);
        // An operation for another store is made into a step as well, so a package is as valid on every store.
        List<PackageStep> steps =
        [
            .. manifest.Operations.Select(operation => (operation.Only, Step: ToStep(operation, files, folders)))
                .Where(item => item.Only is null || game is null || item.Only.Contains(game.Value.Store))
                .Select(item => item.Step),
        ];
        // An install without a file would never follow its game to a folder the store moves it to.
        return steps.Count > 0 ? steps : throw new InvalidPackageException(PackageError.Empty);
    }

    private static PackageStep ToStep(PackageOperation? operation, IReadOnlyDictionary<string, ZipArchiveEntry> files,
        IReadOnlyList<FolderRename>? folders) =>
        operation switch
        {
            CopyOperation copy => Copy(copy, files, folders),
            PatchOperation patch => Patch(patch, files, folders),
            RenameOperation rename => new RenameStep(GamePath(rename.From, folders), GamePath(rename.To, folders)),
            DeleteOperation delete => new DeleteStep(GamePath(delete.Path, folders)),
            _ => throw new InvalidPackageException(PackageError.InvalidManifest, "operation"),
        };

    private static CopyStep Copy(CopyOperation copy, IReadOnlyDictionary<string, ZipArchiveEntry> files,
        IReadOnlyList<FolderRename>? folders)
    {
        var entry = Payload(copy.Source, PackageLayout.FilesFolder, files);
        if (entry.Length != copy.Size)
        {
            throw new InvalidPackageException(PackageError.PayloadMismatch, copy.Source);
        }

        return new CopyStep(GamePath(copy.Path, folders), copy.Source, copy.Size, Sha256(copy.Sha256));
    }

    private static PackageStep Patch(PatchOperation patch, IReadOnlyDictionary<string, ZipArchiveEntry> files,
        IReadOnlyList<FolderRename>? folders)
    {
        if (patch.Alternatives is not { } alternatives)
        {
            return patch is
                {
                    Patch: { } entry, SourceSize: { } sourceSize, SourceSha256: { } sourceSha256, TargetSize: { } targetSize,
                    TargetSha256: { } targetSha256,
                }
                ? PatchOf(patch.Path, new PatchAlternative(entry, sourceSize, sourceSha256, targetSize, targetSha256), files, folders)
                : throw new InvalidPackageException(PackageError.InvalidManifest, patch.Path);
        }

        // The serializer checks the nullability of properties but not of list items.
        if (patch is not { Patch: null, SourceSize: null, SourceSha256: null, TargetSize: null, TargetSha256: null }
            || alternatives.Count is < 2 or > PatchOperation.MaxAlternatives || alternatives.Any(alternative => alternative is null))
        {
            throw new InvalidPackageException(PackageError.InvalidManifest, patch.Path);
        }

        List<PatchStep> steps = [.. alternatives.Select(alternative => PatchOf(patch.Path, alternative, files, folders))];
        return steps.DistinctBy(step => (step.SourceSize, step.SourceSha256)).Count() == steps.Count
            ? new PatchChoiceStep(steps[0].Path, steps)
            : throw new InvalidPackageException(PackageError.InvalidManifest, patch.Path);
    }

    private static PatchStep PatchOf(string path, PatchAlternative patch, IReadOnlyDictionary<string, ZipArchiveEntry> files,
        IReadOnlyList<FolderRename>? folders)
    {
        Payload(patch.Patch, PackageLayout.PatchesFolder, files);
        if (patch.SourceSize < 0 || patch.TargetSize < 0)
        {
            throw new InvalidPackageException(PackageError.InvalidManifest, path);
        }

        return new PatchStep(GamePath(path, folders), patch.Patch, patch.SourceSize, Sha256(patch.SourceSha256),
            patch.TargetSize, Sha256(patch.TargetSha256));
    }

    private static ZipArchiveEntry Payload(string name, string folder, IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        if (!name.StartsWith(folder, StringComparison.Ordinal) || !PackagePath.IsValid(name))
        {
            throw new InvalidPackageException(PackageError.InvalidPath, name);
        }

        return files.TryGetValue(name, out var entry) ? entry : throw new InvalidPackageException(PackageError.MissingPayload, name);
    }

    private static string GamePath(string path, IReadOnlyList<FolderRename>? folders)
    {
        var mapped = FolderRename.Map(folders, path);
        return PackagePath.IsValid(mapped) ? mapped : throw new InvalidPackageException(PackageError.InvalidPath, path);
    }

    /// <summary>Whether text is a SHA-256 in hex, in either letter case.</summary>
    /// <param name="hash">The text a manifest gives as a hash.</param>
    public static bool IsSha256(string? hash) =>
        hash is { Length: Sha256HexLength } && !hash.AsSpan().ContainsAnyExcept(HexDigits);

    private static string Sha256(string hash) =>
        IsSha256(hash) ? hash.ToLowerInvariant() : throw new InvalidPackageException(PackageError.InvalidManifest, hash);
}
