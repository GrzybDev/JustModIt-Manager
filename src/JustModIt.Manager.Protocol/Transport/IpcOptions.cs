namespace JustModIt.Manager.Protocol.Transport;

public sealed record IpcOptions
{
    public static IpcOptions Default { get; } = new();

    /// <summary>The longest payload sent or accepted, in bytes.</summary>
    public int MaxFrameLength { get; init; } = FrameCodec.DefaultMaxFrameLength;

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Frames waiting to be written; a peer that lets the queue fill up is not reading and gets disconnected.</summary>
    public int SendQueueCapacity { get; init; } = 256;

    public int MaxConnections { get; init; } = 16;

    /// <summary>The first delay after a failed listen or connect; each further failure doubles it.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}
