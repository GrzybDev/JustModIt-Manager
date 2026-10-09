using System.IO.Pipes;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Protocol.Transport;

/// <summary>
/// The agent's end of the pipe: serves the UI and short-lived launchers at the same time, each on its own connection.
/// </summary>
public sealed partial class PipeServer : IAsyncDisposable
{
    // Without a buffer every write waits for the reader, so each frame would cost a round trip between processes.
    private const int BufferSize = 64 * 1024;

    private readonly string _pipeName;
    private readonly Hello _hello;
    private readonly IConnectionHandler _handler;
    private readonly IpcOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _stopping = new();
    private Task _acceptLoop = Task.CompletedTask;
    private int _state;

    /// <summary>Creates the server, which listens once started.</summary>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="hello">The agent's hello.</param>
    /// <param name="handler">Receives every connection's requests and events.</param>
    /// <param name="logger">Receives the server's and its connections' log.</param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    public PipeServer(string pipeName, Hello hello, IConnectionHandler handler, ILogger logger, IpcOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(logger);

        _pipeName = pipeName;
        _hello = hello;
        _handler = handler;
        _logger = logger;
        _options = options ?? IpcOptions.Default;
        _slots = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);
    }

    /// <summary>Starts accepting connections in the background; a server starts only once.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("The pipe server can be started only once.");
        }

        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state, 2) == 2)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _acceptLoop.ConfigureAwait(false);

        // Every session holds a slot until it has closed its connection, so taking all of them waits for the last one.
        for (var i = 0; i < _options.MaxConnections; i++)
        {
            await _slots.WaitAsync().ConfigureAwait(false);
        }

        _slots.Dispose();
        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        var stopping = _stopping.Token;
        var backoff = new Backoff(_options.RetryDelay, _options.MaxRetryDelay);
        var failures = 0;
        while (true)
        {
            try
            {
                await _slots.WaitAsync(stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            NamedPipeServerStream? stream = null;
            try
            {
                stream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    BufferSize,
                    BufferSize);
                await stream.WaitForConnectionAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (stream is not null)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }

                _slots.Release();
                if (stopping.IsCancellationRequested)
                {
                    return;
                }

                // Another process can hold the pipe name for a long time; retrying at once would burn a whole core.
                var delay = backoff.Next();
                if (++failures == 1)
                {
                    LogListenFailed(ex, _pipeName, delay);
                }
                else
                {
                    LogListenStillFailing(ex, failures, delay);
                }

                try
                {
                    await Task.Delay(delay, stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            if (failures > 0)
            {
                LogListenRecovered(failures);
                failures = 0;
                backoff.Reset();
            }

            _ = ServeAsync(stream);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream stream)
    {
        PipeConnection? connection = null;
        try
        {
            connection = await PipeConnection
                .HandshakeAsync(stream, _hello, _handler, _options, _logger, _stopping.Token)
                .ConfigureAwait(false);
            LogConnected(connection.Peer.Role, connection.Peer.ProcessId, connection.Peer.AppVersion);
            connection.Start();
            await connection.Completion.WaitAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (ProtocolMismatchException ex)
        {
            LogVersionMismatch(ex.Peer.Role, ex.Peer.ProcessId, ex.Peer.ProtocolVersion, ex.Peer.AppVersion);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException)
        {
            LogHandshakeFailed(ex);
        }
        catch (Exception ex)
        {
            LogSessionFailed(ex);
        }
        finally
        {
            if (connection is null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            _slots.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listening on the IPC pipe {PipeName} failed; retrying in {Delay}")]
    private partial void LogListenFailed(Exception exception, string pipeName, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listening on the IPC pipe failed {Failures} times in a row; retrying in {Delay}")]
    private partial void LogListenStillFailing(Exception exception, int failures, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening on the IPC pipe again after {Failures} failures")]
    private partial void LogListenRecovered(int failures);

    [LoggerMessage(Level = LogLevel.Debug, Message = "IPC connection from {Role} {ProcessId} running {AppVersion}")]
    private partial void LogConnected(ProcessRole role, int processId, string appVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused {Role} {ProcessId}: it speaks protocol {ProtocolVersion} ({AppVersion})")]
    private partial void LogVersionMismatch(ProcessRole role, int processId, int protocolVersion, string appVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "An IPC handshake failed")]
    private partial void LogHandshakeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "An IPC session failed")]
    private partial void LogSessionFailed(Exception exception);
}
