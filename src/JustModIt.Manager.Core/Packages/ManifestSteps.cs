using System.Buffers;
using System.IO.Compression;

namespace JustModIt.Manager.Core.Packages;

/// <summary>Turns a manifest's operations into validated package steps.</summary>
internal static class ManifestSteps
{
    private const int Sha256HexLength = 64;

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>The steps of a manifest, checked against the archive.</summary>
    /// <param name="manifest">The manifest as read.</param>
    /// <param name="files">The archive's file entries by normalised name.</param>
    /// <exception cref="InvalidPackageException">An operation is unsafe or does not match the archive.</exception>
    public static IReadOnlyList<PackageStep> From(PackageManifest manifest, IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        if (manifest.Targets.Any(target => string.IsNullOrWhiteSpace(target.Id)))
        {
            throw new InvalidPackageException(PackageError.InvalidManifest, "targets");
        }

        if (manifest.Operations.Count == 0)
        {
            throw new InvalidPackageException(PackageError.Empty);
        }

        return [.. manifest.Operations.Select(operation => ToStep(operation, files))];
    }

    private static PackageStep ToStep(PackageOperation? operation, IReadOnlyDictionary<string, ZipArchiveEntry> files) =>
        operation switch
        {
            CopyOperation copy => Copy(copy, files),
            PatchOperation patch => Patch(patch, files),
            RenameOperation rename => new RenameStep(GamePath(rename.From), GamePath(rename.To)),
            DeleteOperation delete => new DeleteStep(GamePath(delete.Path)),
            _ => throw new InvalidPackageException(PackageError.InvalidManifest, "operation"),
        };

    private static CopyStep Copy(CopyOperation copy, IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        var entry = Payload(copy.Source, PackageLayout.FilesFolder, files);
        if (entry.Length != copy.Size)
        {
            throw new InvalidPackageException(PackageError.PayloadMismatch, copy.Source);
        }

        return new CopyStep(GamePath(copy.Path), copy.Source, copy.Size, Sha256(copy.Sha256));
    }

    private static PatchStep Patch(PatchOperation patch, IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        Payload(patch.Patch, PackageLayout.PatchesFolder, files);
        if (patch.SourceSize < 0 || patch.TargetSize < 0)
        {
            throw new InvalidPackageException(PackageError.InvalidManifest, patch.Path);
        }

        return new PatchStep(GamePath(patch.Path), patch.Patch, patch.SourceSize, Sha256(patch.SourceSha256),
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

    private static string GamePath(string path) =>
        PackagePath.IsValid(path) ? path : throw new InvalidPackageException(PackageError.InvalidPath, path);

    /// <summary>Whether text is a SHA-256 in hex, in either letter case.</summary>
    /// <param name="hash">The text a manifest gives as a hash.</param>
    public static bool IsSha256(string? hash) =>
        hash is { Length: Sha256HexLength } && !hash.AsSpan().ContainsAnyExcept(HexDigits);

    private static string Sha256(string hash) =>
        IsSha256(hash) ? hash.ToLowerInvariant() : throw new InvalidPackageException(PackageError.InvalidManifest, hash);
}
