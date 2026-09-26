namespace JustModIt.Manager.Core.IO;

/// <summary>Opens files for one sequential, asynchronous read that other programs may share.</summary>
internal static class SharedFileRead
{
    /// <summary>Delete sharing lets the owner replace the file with a rename while it is being read.</summary>
    public const FileShare Sharing = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>Opens <paramref name="path"/> for one sequential, asynchronous read.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="share">What other programs may do with the file while it is open.</param>
    public static FileStream Open(string path, FileShare share) =>
        new(path, FileMode.Open, FileAccess.Read, share, bufferSize: 0, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Reads the whole file with <see cref="Sharing"/>, so its owner may write or replace it meanwhile.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="maxLength">The longest file accepted, in bytes.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="InvalidDataException">The file is longer than <paramref name="maxLength"/>.</exception>
    public static async Task<byte[]> ReadAllBytesAsync(string path, long maxLength, CancellationToken cancellationToken)
    {
        var stream = Open(path, Sharing);
        await using (stream.ConfigureAwait(false))
        {
            var length = stream.Length;
            if (length > maxLength)
            {
                throw new InvalidDataException($"The file is {length} bytes long, more than the {maxLength} allowed.");
            }

            var content = new byte[length];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            return content;
        }
    }
}
