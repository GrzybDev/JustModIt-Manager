using System.Text.Json.Serialization;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>One message on the pipe, as a request, a response or an event.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RequestFrame), "request")]
[JsonDerivedType(typeof(ResponseFrame), "response")]
[JsonDerivedType(typeof(EventFrame), "event")]
internal abstract record Frame;

/// <summary>A request, which the peer answers with a <see cref="ResponseFrame"/>.</summary>
/// <param name="Id">The id the answer repeats.</param>
/// <param name="Payload">The request.</param>
internal sealed record RequestFrame(long Id, Message Payload) : Frame;

/// <summary>The answer to a request: a success with an optional payload, or a failure with only the error.</summary>
/// <param name="Id">The id of the request it answers.</param>
/// <param name="Payload">What a success answers with; null for an empty answer.</param>
/// <param name="Error">Why the request failed; null on success.</param>
internal sealed record ResponseFrame(long Id, Message? Payload = null, RemoteError? Error = null) : Frame;

/// <summary>An event, which the peer does not answer.</summary>
/// <param name="Payload">The event.</param>
internal sealed record EventFrame(Message Payload) : Frame;

/// <summary>A typed error a peer answers with.</summary>
/// <param name="Code">What went wrong.</param>
/// <param name="Detail">English text, only for the log.</param>
internal sealed record RemoteError(ErrorCode Code, string? Detail = null);

public enum ErrorCode
{
    /// <summary>The peer does not handle this request.</summary>
    Unsupported,
    InvalidRequest,
    Failed,

    /// <summary>The peer is exiting; a launcher waits for it to end and takes over.</summary>
    ShuttingDown,
}
