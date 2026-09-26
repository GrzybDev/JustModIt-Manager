using System.Buffers.Binary;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>Writes serialized frames one at a time; not safe for concurrent writes.</summary>
/// <param name="stream">The connected pipe.</param>
internal sealed class FrameWriter(Stream stream)
{
    private readonly byte[] _header = new byte[FrameCodec.HeaderLength];

    /// <summary>Writes a payload behind its length header.</summary>
    /// <param name="payload">A payload from <see cref="FrameCodec.Serialize"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_header, payload.Length);
        await stream.WriteAsync(_header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }
}
