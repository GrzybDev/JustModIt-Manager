using Microsoft.Win32.SafeHandles;

namespace JustModIt.Manager.Core.Patching;

/// <summary>Drives a native session: feeds input, serves source blocks and writes output, one step at a time.</summary>
internal static class XDelta3Codec
{
    private const int DecodeInputChunk = 1 << 20;
    private const int DecodeSourceBlock = 8 << 20;
    private const int DecodeCacheSlots = 4;

    /// <summary>Decodes a patch into the output stream.</summary>
    /// <param name="source">The original, open for random access; null for a patch made without a source.</param>
    /// <param name="sourceLength">The original's size in bytes.</param>
    /// <param name="patch">The VCDIFF patch, read to its end.</param>
    /// <param name="output">Receives the result.</param>
    /// <param name="maxOutput">
    /// The most bytes to write; a few bytes of patch can declare a 64 MiB window, so a small patch could otherwise fill
    /// the disk.
    /// </param>
    /// <param name="onOutput">Sees each piece of the result before it is written.</param>
    /// <param name="cancellationToken">Stops decoding, also inside a long native call.</param>
    /// <returns>The number of bytes written.</returns>
    public static async Task<long> DecodeAsync(SafeFileHandle? source, long sourceLength, Stream patch, Stream output,
        long maxOutput, Action<ReadOnlySpan<byte>> onOutput, CancellationToken cancellationToken)
    {
        XDelta3Native.EnsureAvailable();
        using var session = XDelta3Session.CreateDecoder(source is null ? 0 : sourceLength, DecodeSourceBlock, DecodeInputChunk);
        using var cache = source is null || sourceLength == 0 ? null : new SourceBlockCache(source, sourceLength, DecodeCacheSlots);
        session.CancelWith(cancellationToken);
        return await RunAsync(session, patch, output, maxOutput, fillWholeWindows: false, onInput: null, onOutput, cache,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes a patch that turns the source into the target.</summary>
    /// <param name="source">The whole source in memory, or 0 when <paramref name="sourceLength"/> is 0.</param>
    /// <param name="sourceLength">The original's size in bytes.</param>
    /// <param name="target">The changed file, read to its end.</param>
    /// <param name="targetLength">The changed file's size in bytes.</param>
    /// <param name="patch">Receives the patch.</param>
    /// <param name="options">The level, window sizes and secondary compression.</param>
    /// <param name="onInput">Sees each piece of the target as it is read.</param>
    /// <param name="cancellationToken">Stops encoding, also inside a long native call.</param>
    /// <returns>The size of the patch.</returns>
    public static async Task<long> EncodeAsync(nint source, long sourceLength, Stream target, long targetLength, Stream patch,
        XDelta3EncodeOptions options, Action<ReadOnlySpan<byte>> onInput, CancellationToken cancellationToken)
    {
        if (options.WindowSize is < XDelta3EncodeOptions.MinWindowSize or > XDelta3EncodeOptions.MaxWindowSize)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.WindowSize, "The window size is out of range.");
        }

        XDelta3Native.EnsureAvailable();
        // Like the xdelta3 CLI, a small target gets no full window and match table.
        var window = (int)Math.Clamp(targetLength, XDelta3EncodeOptions.MinWindowSize, options.WindowSize);
        using var session = XDelta3Session.CreateEncoder(source, sourceLength, options, window);
        session.CancelWith(cancellationToken);
        return await RunAsync(session, target, patch, long.MaxValue, fillWholeWindows: true, onInput, onOutput: null,
            cache: null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> RunAsync(XDelta3Session session, Stream input, Stream output, long maxOutput, bool fillWholeWindows,
        Action<ReadOnlySpan<byte>>? onInput, Action<ReadOnlySpan<byte>>? onOutput, SourceBlockCache? cache,
        CancellationToken cancellationToken)
    {
        var buffer = session.InputBuffer;
        long written = 0;
        while (true)
        {
            var result = session.Step(out var produced, out var length);
            switch (result)
            {
                case XDelta3Native.HaveOutput:
                    if (length > maxOutput - written)
                    {
                        throw new PatchException(PatchError.OutputTooLarge, $"more than {maxOutput} bytes");
                    }

                    using (var memory = new UnmanagedBytes(produced, length))
                    {
                        onOutput?.Invoke(memory.GetSpan());
                        await output.WriteAsync(memory.Memory, cancellationToken).ConfigureAwait(false);
                    }

                    written += length;
                    break;
                case XDelta3Native.NeedInput:
                    cancellationToken.ThrowIfCancellationRequested();
                    // The encoder needs whole windows except for the last one, so short reads must be filled.
                    var read = fillWholeWindows
                        ? await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken)
                            .ConfigureAwait(false)
                        : await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    onInput?.Invoke(buffer.Span[..read]);
                    session.Input(read, fillWholeWindows ? read < buffer.Length : read == 0);
                    break;
                case XDelta3Native.NeedSource:
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cache is null)
                    {
                        throw new PatchException(PatchError.Damaged, "the patch needs a source");
                    }

                    var (block, blockSize) = session.SourceRequest();
                    var (address, blockLength) = await cache.GetAsync(block, blockSize, cancellationToken).ConfigureAwait(false);
                    session.SourceBlock(block, address, blockLength);
                    break;
                case XDelta3Native.Done:
                    return written;
                default:
                    cancellationToken.ThrowIfCancellationRequested();
                    throw session.Error(result);
            }
        }
    }
}
