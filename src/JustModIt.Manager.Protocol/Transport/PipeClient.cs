using System.Diagnostics;
using System.IO.Pipes;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Protocol.Transport;

public static class PipeClient
{
    /// <summary>Connects to a pipe and completes the handshake.</summary>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="hello">This process's hello.</param>
    /// <param name="timeout">
    /// How long to retry with backoff while the pipe is missing or busy; <see cref="TimeSpan.Zero"/> makes a single
    /// attempt.
    /// </param>
    /// <param name="handler">Answers the peer's requests; without one every request is answered as unsupported.</param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    /// <param name="logger">Null logs nothing.</param>
    /// <param name="cancellationToken">Gives up connecting.</param>
    /// <exception cref="TimeoutException">Nothing accepted the connection, or the handshake did not finish, in time.</exception>
    /// <exception cref="ProtocolMismatchException">The server speaks another protocol version.</exception>
    /// <exception cref="UnauthorizedAccessException">The pipe belongs to another user.</exception>
    public static async Task<PipeConnection> ConnectAsync(
        string pipeName,
        Hello hello,
        TimeSpan timeout,
        IConnectionHandler? handler = null,
        IpcOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(hello);

        options ??= IpcOptions.Default;
        var stream = await OpenAsync(pipeName, timeout, options, cancellationToken).ConfigureAwait(false);
        try
        {
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

    private static async Task<NamedPipeClientStream> OpenAsync(
        string pipeName,
        TimeSpan timeout,
        IpcOptions options,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var backoff = new Backoff(options.RetryDelay, options.MaxRetryDelay);
        while (true)
        {
            var stream = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                // One attempt per call: the runtime waits for a missing pipe by polling it in a loop, this backs off instead.
                await stream.ConnectAsync(0, cancellationToken).ConfigureAwait(false);
                return stream;
            }
            catch (TimeoutException)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                var remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    throw;
                }

                var delay = backoff.Next();
                await Task.Delay(delay < remaining ? delay : remaining, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
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
