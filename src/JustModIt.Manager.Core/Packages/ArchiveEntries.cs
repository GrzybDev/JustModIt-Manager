using System.Globalization;
using System.IO.Compression;

namespace JustModIt.Manager.Core.Packages;

/// <summary>
/// Checks what an archive claims to contain before anything reads it, independently of the installer's path containment,
/// which guards where steps write, while this guards the archive itself, and both layers have to hold.
/// </summary>
internal static class ArchiveEntries
{
    // Info-ZIP keeps the Unix st_mode in the high 16 bits of the external attributes.
    private const int UnixModeShift = 16;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    /// <summary>Checks every entry's name, kind, count and size, and that it can be opened.</summary>
    /// <param name="archive">The package, opened for reading.</param>
    /// <param name="limits">The most entries and uncompressed bytes allowed.</param>
    /// <param name="isAllowed">Whether a normalised entry path may be in this kind of package.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>File entries by normalised name, in archive order; directory entries are only validated.</returns>
    /// <exception cref="InvalidPackageException">An entry is unsafe, unexpected, repeated or unreadable, or the entries exceed the limits.</exception>
    public static async Task<OrderedDictionary<string, ZipArchiveEntry>> ValidateAsync(ZipArchive archive, PackageLimits limits,
        Func<string, bool> isAllowed, CancellationToken cancellationToken)
    {
        if (archive.Entries.Count > limits.MaxEntries)
        {
            throw new InvalidPackageException(PackageError.TooManyEntries,
                archive.Entries.Count.ToString(CultureInfo.InvariantCulture));
        }

        var files = new OrderedDictionary<string, ZipArchiveEntry>(PackagePath.Comparer);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = PackagePath.NormalizeEntryName(entry.FullName);
            var isDirectory = name.EndsWith(PackagePath.Separator);
            var path = isDirectory ? name[..^1] : name;
            if (!PackagePath.IsValid(path))
            {
                throw new InvalidPackageException(PackageError.UnsafeEntryName, entry.FullName);
            }

            if (IsSymbolicLink(entry))
            {
                throw new InvalidPackageException(PackageError.SymbolicLink, entry.FullName);
            }

            if (!isAllowed(path))
            {
                throw new InvalidPackageException(PackageError.UnexpectedEntry, entry.FullName);
            }

            if (isDirectory)
            {
                continue;
            }

            if (!files.TryAdd(path, entry))
            {
                throw new InvalidPackageException(PackageError.DuplicateEntry, entry.FullName);
            }

            total += entry.Length;
            if (total > limits.MaxUncompressedBytes)
            {
                throw new InvalidPackageException(PackageError.TooLarge, entry.FullName);
            }

            await EnsureReadableAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        return files;
    }

    // An unsupported compression method, encryption or a broken local header only shows when the entry is opened,
    // which would otherwise happen half-way through an install.
    private static async Task EnsureReadableAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            throw new InvalidPackageException(PackageError.UnreadableEntry, entry.FullName, exception);
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> UnixModeShift) & UnixFileTypeMask) == UnixSymbolicLink;
}
