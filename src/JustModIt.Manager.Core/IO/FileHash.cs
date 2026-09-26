using System.Buffers;
using System.Security.Cryptography;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Hashes content in fixed chunks, so any size takes constant memory, giving lower-case hex like the hashes in package
/// manifests and storage ETags.
/// </summary>
public static class FileHash
{
    private const int ChunkSize = 1 << 20;

    /// <summary>Hashes the file at <paramref name="path"/>.</summary>
    /// <param name="path">The file to hash.</param>
    /// <param name="algorithm">The hash to compute.</param>
    /// <param name="progress">Receives the bytes read since the previous report; may be null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<string> ComputeAsync(string path, HashAlgorithmName algorithm, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        // Writers are locked out so the content cannot change halfway through.
        var stream = SharedFileRead.Open(path, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            return await ComputeAsync(stream, algorithm, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Hashes the rest of <paramref name="source"/>.</summary>
    /// <param name="source">The stream, read from its current position to the end.</param>
    /// <param name="algorithm">The hash to compute.</param>
    /// <param name="progress">Receives the bytes read since the previous report; may be null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<string> ComputeAsync(Stream source, HashAlgorithmName algorithm, IProgress<long>? progress,
        CancellationToken cancellationToken) =>
        (await HashAsync(source, destination: null, algorithm, progress, cancellationToken).ConfigureAwait(false)).Hash;

    /// <summary>Copies the rest of <paramref name="source"/> and hashes exactly the bytes written.</summary>
    /// <param name="source">The stream, read from its current position to the end.</param>
    /// <param name="destination">Receives every byte read.</param>
    /// <param name="algorithm">The hash to compute.</param>
    /// <param name="progress">Receives the bytes copied since the previous report; may be null.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    public static Task<(long Length, string Hash)> CopyAndComputeAsync(Stream source, Stream destination,
        HashAlgorithmName algorithm, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return HashAsync(source, destination, algorithm, progress, cancellationToken);
    }

    /// <summary>Tells whether two hex hashes are equal, ignoring letter case.</summary>
    /// <param name="actual">The computed hash.</param>
    /// <param name="expected">The hash to check against.</param>
    public static bool Matches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static async Task<(long Length, string Hash)> HashAsync(Stream source, Stream? destination,
        HashAlgorithmName algorithm, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            long length = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, ChunkSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                if (destination is not null)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                length += read;
                progress?.Report(read);
            }

            return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
