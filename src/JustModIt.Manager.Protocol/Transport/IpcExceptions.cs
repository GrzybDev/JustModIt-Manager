using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Transport;

public sealed class ConnectionClosedException() : IOException("The IPC connection is closed.");

/// <summary>The peer answered a request with an error, or a handler rejects a request with one.</summary>
/// <param name="code">The typed error the peer sees.</param>
/// <param name="detail">English text, only for the log; null for none.</param>
public sealed class RequestFailedException(ErrorCode code, string? detail = null)
    : Exception(detail is null ? $"The request failed with {code}." : $"The request failed with {code}: {detail}")
{
    public ErrorCode Code { get; } = code;

    public string? Detail { get; } = detail;
}

/// <summary>The peer speaks another protocol version, which happens only while the app is being updated.</summary>
/// <param name="peer">The peer's hello.</param>
public sealed class ProtocolMismatchException(Hello peer)
    : Exception($"The peer speaks protocol {peer.ProtocolVersion}, this side speaks {Hello.CurrentProtocolVersion}.")
{
    public Hello Peer { get; } = peer;
}
