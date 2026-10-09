using System.Collections.Immutable;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Protocol.Transport;

public enum ConnectionState
{
    Stopped,
    Connecting,
    Connected,

    /// <summary>The peer speaks another protocol version; it is being updated, so reconnecting continues.</summary>
    Incompatible,
}

/// <summary>
/// Keeps a connection to the agent open, reconnecting with backoff whenever it drops; subscriptions outlive reconnects,
/// and requests made while disconnected fail at once so the caller can show that it is connecting.
/// </summary>
public sealed partial class ReconnectingPipeClient : IConnectionHandler, IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Hello _hello;
    private readonly IpcOptions _options;
    private readonly ILogger _logger;
    private readonly Func<Message, CancellationToken, ValueTask<Message?>>? _handleRequest;
    private readonly CancellationTokenSource _stopping = new();
    private ImmutableArray<Subscription> _subscriptions = [];
    private volatile PipeConnection? _connection;
    private volatile ConnectionState _state = ConnectionState.Stopped;
    private Task _loop = Task.CompletedTask;
    private int _started;

    /// <summary>Creates the client, which connects once started.</summary>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="hello">This process's hello.</param>
    /// <param name="logger">Receives the client's log.</param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    /// <param name="handleRequest">
    /// Answers the agent's requests, such as whether the window may close; without it every request is answered as
    /// unsupported.
    /// </param>
    public ReconnectingPipeClient(
        string pipeName,
        Hello hello,
        ILogger logger,
        IpcOptions? options = null,
        Func<Message, CancellationToken, ValueTask<Message?>>? handleRequest = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(logger);

        _pipeName = pipeName;
        _hello = hello;
        _logger = logger;
        _options = options ?? IpcOptions.Default;
        _handleRequest = handleRequest;
    }

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler<ConnectionState>? StateChanged;

    public ConnectionState State => _state;

    public Hello? Peer => _connection?.Peer;

    /// <summary>Starts connecting in the background, and again whenever the connection drops; a client starts only once.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("The client can be started only once.");
        }

        SetState(ConnectionState.Connecting);
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Sends a request on the current connection and waits for its response.</summary>
    /// <param name="request">The request's payload.</param>
    /// <param name="timeout">How long to wait for the response; null for <see cref="IpcOptions.RequestTimeout"/>.</param>
    /// <param name="cancellationToken">Stops waiting; the agent may still act on a request already sent.</param>
    /// <returns>The response's payload; null for an empty response.</returns>
    /// <exception cref="ConnectionClosedException">There is no connection right now, or it closed before the answer.</exception>
    public Task<Message?> RequestAsync(
        Message request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        (_connection ?? throw new ConnectionClosedException()).RequestAsync(request, timeout, cancellationToken);

    /// <summary>Hands the agent's events of one type to a handler, across reconnects, until the result is disposed.</summary>
    /// <param name="handler">Runs on the connection's reader in the order events arrive, so it must not block.</param>
    public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : Message
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new Subscription(this, message =>
        {
            if (message is TMessage typed)
            {
                handler(typed);
            }
        });
        ImmutableInterlocked.Update(ref _subscriptions, static (list, item) => list.Add(item), subscription);
        return subscription;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _started, 2) == 2)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stopping.Dispose();
        SetState(ConnectionState.Stopped);
    }

    ValueTask<Message?> IConnectionHandler.HandleRequestAsync(
        PipeConnection connection,
        Message request,
        CancellationToken cancellationToken) =>
        _handleRequest is null
            ? throw new RequestFailedException(ErrorCode.Unsupported)
            : _handleRequest(request, cancellationToken);

    void IConnectionHandler.OnEvent(PipeConnection connection, Message message)
    {
        foreach (var subscription in _subscriptions)
        {
            try
            {
                subscription.Deliver(message);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, message.GetType().Name);
            }
        }
    }

    private async Task RunAsync()
    {
        var stopping = _stopping.Token;
        var backoff = new Backoff(_options.RetryDelay, _options.MaxRetryDelay);
        while (true)
        {
            try
            {
                await StayConnectedAsync(backoff, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (ProtocolMismatchException ex)
            {
                if (SetState(ConnectionState.Incompatible))
                {
                    LogIncompatible(ex.Peer.Role, ex.Peer.ProcessId, ex.Peer.ProtocolVersion, ex.Peer.AppVersion);
                }
            }
            catch (Exception ex)
            {
                LogConnectFailed(ex);
            }

            try
            {
                await Task.Delay(backoff.Next(), stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task StayConnectedAsync(Backoff backoff, CancellationToken stopping)
    {
        var connection = await PipeClient
            .ConnectAsync(_pipeName, _hello, this, _options, _logger, stopping)
            .ConfigureAwait(false);
        try
        {
            _connection = connection;
            backoff.Reset();
            LogConnected(connection.Peer.Role, connection.Peer.ProcessId, connection.Peer.AppVersion);
            SetState(ConnectionState.Connected);
            await connection.Completion.WaitAsync(stopping).ConfigureAwait(false);
            LogDisconnected(connection.Peer.Role, connection.Peer.ProcessId);
        }
        finally
        {
            _connection = null;
            await connection.DisposeAsync().ConfigureAwait(false);
            if (!stopping.IsCancellationRequested)
            {
                SetState(ConnectionState.Connecting);
            }
        }
    }

    private bool SetState(ConnectionState state)
    {
        if (_state == state)
        {
            return false;
        }

        _state = state;
        try
        {
            StateChanged?.Invoke(this, state);
        }
        catch (Exception ex)
        {
            LogSubscriberFailed(ex, nameof(StateChanged));
        }

        return true;
    }

    private void Unsubscribe(Subscription subscription) =>
        ImmutableInterlocked.Update(ref _subscriptions, static (list, item) => list.Remove(item), subscription);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to {Role} {ProcessId} running {AppVersion}")]
    private partial void LogConnected(ProcessRole role, int processId, string appVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lost the IPC connection to {Role} {ProcessId}")]
    private partial void LogDisconnected(ProcessRole role, int processId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Connecting to the IPC pipe failed")]
    private partial void LogConnectFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Role} {ProcessId} speaks protocol {ProtocolVersion} ({AppVersion}); waiting for it to restart")]
    private partial void LogIncompatible(ProcessRole role, int processId, int protocolVersion, string appVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "An IPC subscriber failed on {Subject}")]
    private partial void LogSubscriberFailed(Exception exception, string subject);

    private sealed class Subscription(ReconnectingPipeClient owner, Action<Message> deliver) : IDisposable
    {
        public void Deliver(Message message) => deliver(message);

        public void Dispose() => owner.Unsubscribe(this);
    }
}
