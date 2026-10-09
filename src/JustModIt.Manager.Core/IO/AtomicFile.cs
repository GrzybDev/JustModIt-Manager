using System.Security.Cryptography;
using System.Text.Json;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Replaces one of the app's own files, such as its data or a package the creator built, in one step, so a crash, a power
/// cut or a concurrent reader never sees half of it and a failed write leaves the previous file as it was.
/// </summary>
public static class AtomicFile
{
    private const int DefaultBufferSize = 16 * 1024;

    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/>.</summary>
    /// <param name="path">The file; its folder is created when missing.</param>
    /// <param name="content">The new content.</param>
    /// <param name="cancellationToken">Cancels the write and keeps the previous file.</param>
    /// <param name="time">Paces the retries of the final rename; the system clock when null.</param>
    public static Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken,
        TimeProvider? time = null) =>
        WriteAsync(path, (stream, token) => stream.WriteAsync(content, token), cancellationToken, time);

    /// <summary>Replaces the file at <paramref name="path"/> with the JSON that <paramref name="write"/> writes.</summary>
    /// <param name="path">The file; its folder is created when missing.</param>
    /// <param name="options">How the JSON is written.</param>
    /// <param name="write">Writes the document with the writer it is given.</param>
    /// <param name="cancellationToken">Cancels the write and keeps the previous file.</param>
    /// <param name="time">Paces the retries of the final rename; the system clock when null.</param>
    public static Task WriteJsonAsync(string path, JsonWriterOptions options, Action<Utf8JsonWriter> write,
        CancellationToken cancellationToken, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        return WriteAsync(path, async (stream, token) =>
        {
            var writer = new Utf8JsonWriter(stream, options);
            await using (writer.ConfigureAwait(false))
            {
                write(writer);
                await writer.FlushAsync(token).ConfigureAwait(false);
            }
        }, cancellationToken, time);
    }

    /// <summary>Replaces the file at <paramref name="path"/> with what <paramref name="write"/> writes.</summary>
    /// <param name="path">The file; its folder is created when missing.</param>
    /// <param name="write">Writes the new content into the stream it is given.</param>
    /// <param name="cancellationToken">Cancels the write and keeps the previous file.</param>
    /// <param name="time">Paces the retries of the final rename; the system clock when null.</param>
    /// <param name="bufferSize">The write buffer's size, larger for large files.</param>
    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, ValueTask> write,
        CancellationToken cancellationToken, TimeProvider? time = null, int bufferSize = DefaultBufferSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        time ??= TimeProvider.System;

        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = $"{target}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.tmp";
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize,
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
