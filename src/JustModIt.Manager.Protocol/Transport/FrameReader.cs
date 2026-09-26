using System.Buffers;
using System.Buffers.Binary;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>Reads frames one at a time; not safe for concurrent reads.</summary>
/// <param name="stream">The connected pipe.</param>
/// <param name="maxFrameLength">The longest payload accepted, in bytes.</param>
internal sealed class FrameReader(Stream stream, int maxFrameLength)
{
    private readonly byte[] _header = new byte[FrameCodec.HeaderLength];

    /// <summary>Reads the next frame.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The next frame, or null when the peer closed the stream between frames.</returns>
    /// <exception cref="EndOfStreamException">The stream ended inside a frame.</exception>
    /// <exception cref="InvalidDataException">The frame is too long or does not hold a valid message.</exception>
    public async ValueTask<Frame?> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await stream.ReadAtLeastAsync(_header, _header.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < _header.Length)
        {
            throw new EndOfStreamException("The stream ended inside a frame header.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(_header);
        if (length <= 0 || length > maxFrameLength)
        {
            throw new InvalidDataException($"The frame length {length} is outside 1..{maxFrameLength}.");
        }

        var payload = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await stream.ReadExactlyAsync(payload.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            return FrameCodec.Deserialize(payload.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }
}
