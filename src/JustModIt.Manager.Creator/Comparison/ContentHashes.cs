using System.Security.Cryptography;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>SHA-256 of files, each hashed at most once per comparison however many lookups need it.</summary>
/// <param name="fileSystem">Opens the files.</param>
internal sealed class ContentHashes(IFileSystem fileSystem)
{
    // Null marks a file that could not be read.
    private readonly Dictionary<string, string?> _hashes = new(PathComparison.Comparer);

    /// <summary>The file's SHA-256, hashed only the first time it is asked for.</summary>
    /// <param name="fullPath">The file, read with writers locked out.</param>
    /// <param name="progress">Receives the bytes hashed; null for none.</param>
    /// <param name="cancellationToken">Cancels the hashing.</param>
    public async Task<string> HashAsync(string fullPath, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (_hashes.TryGetValue(fullPath, out var known) && known is not null)
        {
            return known;
        }

        // Writers are locked out, so the content cannot change halfway through.
        var stream = fileSystem.OpenRead(fullPath, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            var hash = await FileHash.ComputeAsync(stream, HashAlgorithmName.SHA256, progress, cancellationToken).ConfigureAwait(false);
            _hashes[fullPath] = hash;
            return hash;
        }
    }

    /// <summary>Like <see cref="HashAsync"/>, but null for a file that is locked or unreadable.</summary>
    /// <param name="fullPath">The file, read with writers locked out.</param>
    /// <param name="size">The file's size as listed, counted as done when the file cannot be read.</param>
    /// <param name="progress">Receives the bytes hashed, including the rest of an unreadable file, so the progress reaches its end.</param>
    /// <param name="cancellationToken">Cancels the hashing.</param>
    public async Task<string?> TryHashAsync(string fullPath, long size, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        if (_hashes.TryGetValue(fullPath, out var known))
        {
            return known;
        }

        long done = 0;
        var counting = new Core.InlineProgress<long>(bytes =>
        {
            done += bytes;
            progress?.Report(bytes);
        });
        try
        {
            return await HashAsync(fullPath, counting, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _hashes[fullPath] = null;
            progress?.Report(Math.Max(size - done, 0));
            return null;
        }
    }
}
