using System.IO.Pipes;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Protocol.Transport;

public static class PipeClient
{
    /// <summary>Connects to a listening pipe at once and completes the handshake; callers back off between attempts.</summary>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="hello">This process's hello.</param>
    /// <param name="handler">Answers the peer's requests; without one every request is answered as unsupported.</param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    /// <param name="logger">Null logs nothing.</param>
    /// <param name="cancellationToken">Gives up connecting.</param>
    /// <exception cref="TimeoutException">Nothing listens on the pipe, or the handshake did not finish in time.</exception>
    /// <exception cref="ProtocolMismatchException">The server speaks another protocol version.</exception>
    /// <exception cref="UnauthorizedAccessException">The pipe belongs to another user.</exception>
    public static async Task<PipeConnection> ConnectAsync(
        string pipeName,
        Hello hello,
        IConnectionHandler? handler = null,
        IpcOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(hello);

        options ??= IpcOptions.Default;
        var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            // A zero timeout makes one attempt: for a missing pipe the runtime would poll in a loop instead of backing off.
            await stream.ConnectAsync(0, cancellationToken).ConfigureAwait(false);
            var connection = await PipeConnection
                .HandshakeAsync(stream, hello, handler ?? UnsupportedRequests.Instance, options, logger ?? NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
            connection.Start();
            return connection;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class UnsupportedRequests : IConnectionHandler
    {
        public static UnsupportedRequests Instance { get; } = new();

        public ValueTask<Message?> HandleRequestAsync(
            PipeConnection connection,
            Message request,
            CancellationToken cancellationToken) =>
            throw new RequestFailedException(ErrorCode.Unsupported);
    }
}
