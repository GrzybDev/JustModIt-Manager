using System.Security.Cryptography;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Replaces one of the app's own data files in one step, so a crash, a power cut or a concurrent reader never sees half
/// of it and a failed write leaves the previous file as it was.
/// </summary>
public static class AtomicFile
{
    private const int BufferSize = 16 * 1024;

    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/>.</summary>
    /// <param name="path">The file; its folder is created when missing.</param>
    /// <param name="content">The new content.</param>
    /// <param name="cancellationToken">Cancels the write and keeps the previous file.</param>
    /// <param name="time">Paces the retries of the final rename; the system clock when null.</param>
    public static Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken,
        TimeProvider? time = null) =>
        WriteAsync(path, (stream, token) => stream.WriteAsync(content, token), cancellationToken, time);

    /// <summary>Replaces the file at <paramref name="path"/> with what <paramref name="write"/> writes.</summary>
    /// <param name="path">The file; its folder is created when missing.</param>
    /// <param name="write">Writes the new content into the stream it is given.</param>
    /// <param name="cancellationToken">Cancels the write and keeps the previous file.</param>
    /// <param name="time">Paces the retries of the final rename; the system clock when null.</param>
    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, ValueTask> write,
        CancellationToken cancellationToken, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        time ??= TimeProvider.System;

        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = $"{target}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.tmp";
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                // Without this the rename can reach the disk before the data and leave an empty file after a power cut.
                stream.Flush(flushToDisk: true);
            }

            await TransientFileRetry.RunAsync(() => File.Move(temporary, target, overwrite: true), time, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
