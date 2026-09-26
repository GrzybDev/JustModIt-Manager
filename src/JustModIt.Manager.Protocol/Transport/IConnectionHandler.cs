using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>Receives what the peer sends; events arrive in order on the connection's reader, so they must not block.</summary>
public interface IConnectionHandler
{
    /// <summary>Called once the handshake is done, before anything arrives.</summary>
    /// <param name="connection">The new connection.</param>
    void OnConnected(PipeConnection connection)
    {
    }

    /// <summary>
    /// Answers a request other than <see cref="Ping"/>, which the connection answers itself; throw
    /// <see cref="RequestFailedException"/> to answer with a typed error.
    /// </summary>
    /// <param name="connection">The connection the request came on.</param>
    /// <param name="request">The request's payload.</param>
    /// <param name="cancellationToken">Cancelled when the connection closes.</param>
    /// <returns>The answer's payload; null for an empty answer.</returns>
    ValueTask<Message?> HandleRequestAsync(PipeConnection connection, Message request, CancellationToken cancellationToken);

    /// <summary>Receives an event; it must not block, since the next ones wait for it.</summary>
    /// <param name="connection">The connection the event came on.</param>
    /// <param name="message">The event's payload.</param>
    void OnEvent(PipeConnection connection, Message message)
    {
    }

    /// <summary>Called once when the connection closes, from either side.</summary>
    /// <param name="connection">The closed connection.</param>
    void OnDisconnected(PipeConnection connection)
    {
    }
}
