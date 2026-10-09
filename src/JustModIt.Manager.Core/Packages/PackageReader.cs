using System.IO.Compression;

namespace JustModIt.Manager.Core.Packages;

/// <summary>A package validated in full when it was opened, so its steps can be used as they are.</summary>
public interface IPackage : IAsyncDisposable
{
    /// <summary>The kind the package's content shows, whatever its extension.</summary>
    PackageKind Kind { get; }

    /// <summary>The package file's name, without its folder.</summary>
    string FileName { get; }

    /// <summary>Null for a plain .zip.</summary>
    PackageManifest? Manifest { get; }

    /// <summary>The changes the package makes, in order.</summary>
    IReadOnlyList<PackageStep> Steps { get; }

    /// <summary>Streams exactly the declared size of an entry, or fails with <see cref="InvalidPackageException"/>.</summary>
    /// <param name="entry">The entry's normalised name, as a step names it.</param>
    /// <param name="cancellationToken">Cancels opening it.</param>
    Task<Stream> OpenEntryAsync(string entry, CancellationToken cancellationToken);
}

/// <summary>
/// Opens packages of either kind, telling them apart by content, never by extension, since a manifest package unpacked
/// as a plain archive would spill its manifest and payload folders into the game.
/// </summary>
/// <param name="limits">The limits to enforce; null for the defaults.</param>
public sealed class PackageReader(PackageLimits? limits = null)
{
    private const int BufferSize = 1 << 16;

    private readonly PackageLimits _limits = limits ?? PackageLimits.Default;

    /// <summary>Opens a package file and validates it in full.</summary>
    /// <param name="path">The package's full path.</param>
    /// <param name="cancellationToken">Cancels the validation.</param>
    /// <exception cref="InvalidPackageException">The package is malformed, unsafe or made for a newer app.</exception>
    public async Task<IPackage> OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        ZipArchive archive;
        try
        {
            archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new InvalidPackageException(PackageError.NotAnArchive, Path.GetFileName(path), exception);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            var kind = await DetectKindAsync(archive, cancellationToken).ConfigureAwait(false);
            var files = await ArchiveEntries.ValidateAsync(archive, _limits,
                kind == PackageKind.Manifest ? IsManifestPackageEntry : _ => true, cancellationToken).ConfigureAwait(false);
            var manifest = kind == PackageKind.Manifest ? await ReadManifestAsync(files, cancellationToken).ConfigureAwait(false) : null;
            var steps = manifest is null ? ZipSteps(files) : ManifestSteps.From(manifest, files);
            return new ZipPackage(kind, Path.GetFileName(path), archive, files, manifest, steps);
        }
        catch
        {
            await archive.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsManifestPackageEntry(string path) => path == PackageLayout.ManifestEntry || PackageLayout.IsPayload(path);

    // A root manifest makes a package when everything else sits in the payload folders, or when it declares a format,
    // since a plain archive may ship a game's own manifest.json; payload folders without a manifest lost it.
    private async Task<PackageKind> DetectKindAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        ZipArchiveEntry? manifest = null;
        var onlyPayload = true;
        foreach (var entry in archive.Entries)
        {
            var name = PackagePath.NormalizeEntryName(entry.FullName);
            if (name == PackageLayout.ManifestEntry)
            {
                manifest = entry;
            }
            else if (!PackageLayout.IsPayload(name))
            {
                onlyPayload = false;
            }
        }

        if (manifest is null)
        {
            return onlyPayload && archive.Entries.Count > 0
                ? throw new InvalidPackageException(PackageError.MissingManifest)
                : PackageKind.Zip;
        }

        if (onlyPayload || manifest.Length > _limits.MaxManifestBytes)
        {
            return PackageKind.Manifest;
        }

        try
        {
            var json = await ReadEntryAsync(manifest, cancellationToken).ConfigureAwait(false);
            return ManifestReader.DeclaresFormatVersion(json) ? PackageKind.Manifest : PackageKind.Zip;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or InvalidPackageException)
        {
            return PackageKind.Zip;
        }
    }

    private async Task<PackageManifest> ReadManifestAsync(OrderedDictionary<string, ZipArchiveEntry> files,
        CancellationToken cancellationToken)
    {
        if (!files.TryGetValue(PackageLayout.ManifestEntry, out var entry))
        {
            throw new InvalidPackageException(PackageError.MissingManifest);
        }

        if (entry.Length > _limits.MaxManifestBytes)
        {
            throw new InvalidPackageException(PackageError.TooLarge, entry.FullName);
        }

        return ManifestReader.Parse(await ReadEntryAsync(entry, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var content = new byte[entry.Length];
        var stream = new BoundedReadStream(await entry.OpenAsync(cancellationToken).ConfigureAwait(false), entry.Length,
            entry.FullName);
        await using (stream.ConfigureAwait(false))
        {
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            // Reading past the declared end, which the stream refuses when more comes, proves the entry does not inflate further.
            await stream.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false);
        }

        return content;
    }

    private static List<PackageStep> ZipSteps(OrderedDictionary<string, ZipArchiveEntry> files) =>
        files.Count == 0
            ? throw new InvalidPackageException(PackageError.Empty)
            : [.. files.Select(file => new CopyStep(file.Key, file.Key, file.Value.Length, Sha256: null))];

    private sealed class ZipPackage(
        PackageKind kind,
        string fileName,
        ZipArchive archive,
        OrderedDictionary<string, ZipArchiveEntry> files,
        PackageManifest? manifest,
        IReadOnlyList<PackageStep> steps) : IPackage
    {
        public PackageKind Kind => kind;

        public string FileName => fileName;

        public PackageManifest? Manifest => manifest;

        public IReadOnlyList<PackageStep> Steps => steps;

        public async Task<Stream> OpenEntryAsync(string entry, CancellationToken cancellationToken) =>
            files.TryGetValue(entry, out var found)
                ? new BoundedReadStream(await found.OpenAsync(cancellationToken).ConfigureAwait(false), found.Length, entry)
                : throw new InvalidPackageException(PackageError.MissingPayload, entry);

        public ValueTask DisposeAsync() => archive.DisposeAsync();
    }
}
