using System.Collections.Concurrent;
using System.Threading.Channels;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>
/// An established connection, on which either side sends requests and events and responses are matched to requests by id.
/// </summary>
public sealed partial class PipeConnection : IAsyncDisposable
{
    private static readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(2);

    private readonly Stream _stream;
    private readonly FrameReader _reader;
    private readonly FrameWriter _writer;
    private readonly IConnectionHandler _handler;
    private readonly IpcOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<byte[]> _outgoing;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<Message?>> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _readLoop = Task.CompletedTask;
    private Task _writeLoop = Task.CompletedTask;
    private long _lastRequestId;
    private volatile bool _sendingCompleted;
    private int _isClosed;

    private PipeConnection(
        Stream stream,
        FrameReader reader,
        FrameWriter writer,
        Hello peer,
        IConnectionHandler handler,
        IpcOptions options,
        ILogger logger)
    {
        _stream = stream;
        _reader = reader;
        _writer = writer;
        Peer = peer;
        _handler = handler;
        _options = options;
        _logger = logger;
        _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.SendQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public Hello Peer { get; }

    /// <summary>Completes, and never faults, once the connection is closed by either side.</summary>
    public Task Completion => _closed.Task;

    /// <summary>Sends a request and waits for its response.</summary>
    /// <param name="request">The request's payload.</param>
    /// <param name="timeout">How long to wait for the response; null for <see cref="IpcOptions.RequestTimeout"/>.</param>
    /// <param name="cancellationToken">Stops waiting; the peer may still act on a request already sent.</param>
    /// <returns>The response's payload; null for an empty response.</returns>
    /// <exception cref="TimeoutException">No response arrived in time; a late one is ignored.</exception>
    /// <exception cref="RequestFailedException">The peer answered with an error.</exception>
    /// <exception cref="ConnectionClosedException">The connection closed before the response arrived.</exception>
    /// <exception cref="InvalidOperationException">The request is over the frame limit.</exception>
    public async Task<Message?> RequestAsync(
        Message request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = Interlocked.Increment(ref _lastRequestId);
        var response = new TaskCompletionSource<Message?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = response;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? _options.RequestTimeout);
        try
        {
            await EnqueueAsync(new RequestFrame(id, request), deadline.Token).ConfigureAwait(false);
            return await response.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The peer did not answer the {request.GetType().Name} request in time.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends an event, waiting while the send queue is full.</summary>
    /// <param name="message">The event's payload.</param>
    /// <param name="cancellationToken">Stops waiting for room in the queue.</param>
    public ValueTask SendEventAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return EnqueueAsync(new EventFrame(message), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // What is already queued goes out first, so a last answer or event is not lost.
        if (CompleteSending())
        {
            try
            {
                await _writeLoop.WaitAsync(_drainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }

        Close();
        await Task.WhenAll(_readLoop, _writeLoop).ConfigureAwait(false);
    }

    /// <summary>Exchanges hellos; the returned connection does nothing until <see cref="Start"/>.</summary>
    /// <param name="stream">The connected pipe, which the returned connection owns.</param>
    /// <param name="local">This process's hello.</param>
    /// <param name="handler">Receives the peer's requests and events once started.</param>
    /// <param name="options">The transport's settings.</param>
    /// <param name="logger">Receives the connection's log.</param>
    /// <param name="cancellationToken">Gives up the handshake.</param>
    /// <exception cref="TimeoutException">The peer did not complete the handshake in time.</exception>
    /// <exception cref="ProtocolMismatchException">The peer speaks another protocol version.</exception>
    internal static async Task<PipeConnection> HandshakeAsync(
        Stream stream,
        Hello local,
        IConnectionHandler handler,
        IpcOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var reader = new FrameReader(stream, options.MaxFrameLength);
        var writer = new FrameWriter(stream);

        // The deadline is the peer's: the first serialization in a JIT-compiled process can alone outlast a short one,
        // and the hello would then never be sent.
        var localHello = FrameCodec.Serialize(new EventFrame(local), options.MaxFrameLength);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.HandshakeTimeout);
        Hello peer;
        try
        {
            // Both sides send first, and a pipe write may not finish until the peer reads, so read while writing.
            var sending = writer.WriteAsync(localHello, deadline.Token).AsTask();
            Frame? frame;
            try
            {
                frame = await reader.ReadAsync(deadline.Token).ConfigureAwait(false);
            }
            catch
            {
                await deadline.CancelAsync().ConfigureAwait(false);
                await sending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw;
            }

            await sending.ConfigureAwait(false);
            peer = frame switch
            {
                EventFrame { Payload: Hello hello } => hello,
                null => throw new EndOfStreamException("The peer closed the connection during the handshake."),
                _ => throw new InvalidDataException("The peer did not start with a hello."),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The peer did not complete the handshake in time.");
        }

        if (peer.ProtocolVersion != local.ProtocolVersion)
        {
            throw new ProtocolMismatchException(peer);
        }

        return new PipeConnection(stream, reader, writer, peer, handler, options, logger);
    }

    /// <summary>Tells the handler that the connection is up, then starts reading and writing.</summary>
    internal void Start()
    {
        try
        {
            _handler.OnConnected(this);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex, nameof(IConnectionHandler.OnConnected));
        }

        _writeLoop = Task.Run(WriteLoopAsync);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Queues an already serialized event without waiting; a peer that is not reading is disconnected.</summary>
    /// <param name="eventPayload">A serialized event frame, shared by every connection it goes to.</param>
    internal void Post(byte[] eventPayload)
    {
        if (!_outgoing.Writer.TryWrite(eventPayload) && !_sendingCompleted)
        {
            LogPeerNotReading(Peer.Role, Peer.ProcessId);
            Close();
        }
    }

    private async ValueTask EnqueueAsync(Frame frame, CancellationToken cancellationToken)
    {
        var payload = FrameCodec.Serialize(frame, _options.MaxFrameLength);
        try
        {
            await _outgoing.Writer.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new ConnectionClosedException();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _reader.ReadAsync(_closing.Token).ConfigureAwait(false) is { } frame)
            {
                Dispatch(frame);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (InvalidDataException ex)
        {
            LogInvalidFrame(ex, Peer.Role, Peer.ProcessId);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            LogReadEnded(ex, Peer.Role, Peer.ProcessId);
        }
        catch (Exception ex)
        {
            LogLoopFailed(ex, nameof(ReadLoopAsync));
        }
        finally
        {
            Close();
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var payload in _outgoing.Reader.ReadAllAsync(_closing.Token).ConfigureAwait(false))
            {
                await _writer.WriteAsync(payload, _closing.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            LogWriteEnded(ex, Peer.Role, Peer.ProcessId);
        }
        catch (Exception ex)
        {
            LogLoopFailed(ex, nameof(WriteLoopAsync));
        }
        finally
        {
            Close();
        }
    }

    private void Dispatch(Frame frame)
    {
        switch (frame)
        {
            case ResponseFrame response when _pending.TryRemove(response.Id, out var pending):
                if (response.Error is { } error)
                {
                    pending.TrySetException(new RequestFailedException(error.Code, error.Detail));
                }
                else
                {
                    pending.TrySetResult(response.Payload);
                }

                break;
            case RequestFrame request:
                _ = Task.Run(() => AnswerAsync(request));
                break;
            case EventFrame { Payload: var message }:
                try
                {
                    _handler.OnEvent(this, message);
                }
                catch (Exception ex)
                {
                    LogHandlerFailed(ex, message.GetType().Name);
                }

                break;
        }
    }

    private async Task AnswerAsync(RequestFrame request)
    {
        ResponseFrame response;
        try
        {
            var result = request.Payload is Ping
                ? null
                : await _handler.HandleRequestAsync(this, request.Payload, _closing.Token).ConfigureAwait(false);
            response = new ResponseFrame(request.Id, result);
        }
        catch (RequestFailedException ex)
        {
            response = new ResponseFrame(request.Id, Error: new RemoteError(ex.Code, ex.Detail));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex, request.Payload.GetType().Name);
            response = new ResponseFrame(request.Id, Error: new RemoteError(ErrorCode.Failed));
        }

        try
        {
            await EnqueueAsync(response, _closing.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            LogHandlerFailed(ex, request.Payload.GetType().Name);
            await TryEnqueueAsync(new ResponseFrame(request.Id, Error: new RemoteError(ErrorCode.Failed))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConnectionClosedException or OperationCanceledException)
        {
        }
    }

    private async Task TryEnqueueAsync(Frame frame)
    {
        try
        {
            await EnqueueAsync(frame, _closing.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConnectionClosedException or OperationCanceledException)
        {
        }
    }

    // False when sending had already been completed.
    private bool CompleteSending()
    {
        // Set first, so a failed post can tell a completed queue from a full one.
        _sendingCompleted = true;
        return _outgoing.Writer.TryComplete();
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
        {
            return;
        }

        CompleteSending();
        _closing.Cancel();
        _stream.Dispose();
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetException(new ConnectionClosedException());
            }
        }

        try
        {
            _handler.OnDisconnected(this);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex, nameof(IConnectionHandler.OnDisconnected));
        }

        _closed.TrySetResult();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing the IPC connection to {Role} {ProcessId} after an invalid frame")]
    private partial void LogInvalidFrame(Exception exception, ProcessRole role, int processId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stopped reading from {Role} {ProcessId}")]
    private partial void LogReadEnded(Exception exception, ProcessRole role, int processId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stopped writing to {Role} {ProcessId}")]
    private partial void LogWriteEnded(Exception exception, ProcessRole role, int processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing the IPC connection to {Role} {ProcessId}, which stopped reading")]
    private partial void LogPeerNotReading(ProcessRole role, int processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The IPC handler failed on {Subject}")]
    private partial void LogHandlerFailed(Exception exception, string subject);

    [LoggerMessage(Level = LogLevel.Error, Message = "The IPC {Loop} failed")]
    private partial void LogLoopFailed(Exception exception, string loop);
}
