using System.Text.Json;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>A frame on the wire is a 4-byte little-endian payload length followed by that many bytes of UTF-8 JSON.</summary>
internal static class FrameCodec
{
    public const int HeaderLength = sizeof(int);
    public const int DefaultMaxFrameLength = 16 * 1024 * 1024;

    /// <summary>Serializes a frame's payload only; the header is written next to it so the payload needs no copy.</summary>
    /// <param name="frame">The request, response or event to send.</param>
    /// <param name="maxFrameLength">The limit in bytes; a longer payload throws <see cref="InvalidOperationException"/>.</param>
    public static byte[] Serialize(Frame frame, int maxFrameLength)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(frame, ProtocolJsonContext.Default.Frame);
        if (payload.Length > maxFrameLength)
        {
            throw new InvalidOperationException(
                $"The {frame.GetType().Name} is {payload.Length} bytes, over the {maxFrameLength} byte frame limit.");
        }

        return payload;
    }

    /// <summary>Reads a frame, throwing <see cref="InvalidDataException"/> for anything that is not a valid message.</summary>
    /// <param name="payload">The JSON that follows the header.</param>
    public static Frame Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.Frame)
                ?? throw new InvalidDataException("The frame holds no message.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("The frame is not a valid message.", ex);
        }
    }
}
