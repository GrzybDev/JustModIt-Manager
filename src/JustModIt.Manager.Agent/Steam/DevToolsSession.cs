using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>A connection that carries whole text messages, such as a web socket.</summary>
internal interface IDevToolsChannel : IAsyncDisposable
{
    /// <summary>Sends one message.</summary>
    /// <param name="message">The message as UTF-8.</param>
    /// <param name="cancellationToken">Stops sending.</param>
    Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken);

    /// <summary>The next message as UTF-8; null once the connection is closed.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>Something a page of Steam's interface reported.</summary>
/// <param name="Method">What happened, such as <c>Page.loadEventFired</c>.</param>
/// <param name="Params">What the report carries.</param>
internal sealed record DevToolsEvent(string Method, JsonElement Params);

/// <summary>A developer tools protocol command that the page refused, or a script that threw.</summary>
/// <param name="message">What went wrong, in the page's words.</param>
internal sealed class DevToolsException(string message) : Exception(message);

/// <summary>
/// A session of the developer tools protocol with one page of Steam's interface: commands matched to their answers by id,
/// and the page's reports in the order they came.
/// </summary>
internal sealed class DevToolsSession : IAsyncDisposable
{
    private static readonly TimeSpan CallTimeLimit = TimeSpan.FromSeconds(30);

    private readonly IDevToolsChannel _channel;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _calls = new();
    private readonly Channel<DevToolsEvent> _events = Channel.CreateUnbounded<DevToolsEvent>(new UnboundedChannelOptions { SingleWriter = true });
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _receiving;
    private long _lastId;
    private volatile bool _isClosed;

    /// <summary>Starts a session over an open connection.</summary>
    /// <param name="channel">The connection, which the session closes when it ends.</param>
    /// <param name="time">The clock that bounds each command.</param>
    public DevToolsSession(IDevToolsChannel channel, TimeProvider time)
    {
        _channel = channel;
        _time = time;
        _receiving = Task.Run(ReceiveAsync);
    }

    /// <summary>The page's reports, completed once the connection closes.</summary>
    public ChannelReader<DevToolsEvent> Events => _events.Reader;

    /// <summary>Runs a command and gives its result.</summary>
    /// <param name="method">The command, such as <c>Page.enable</c>.</param>
    /// <param name="parameters">Writes the command's parameters into an object; null for none.</param>
    /// <param name="cancellationToken">Stops waiting for the answer.</param>
    /// <exception cref="DevToolsException">The page refused the command.</exception>
    /// <exception cref="IOException">The connection closed first.</exception>
    public async Task<JsonElement> CallAsync(string method, Action<Utf8JsonWriter>? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _lastId);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _calls[id] = answer;
        try
        {
            // Either this sees the connection closed, or the end of the connection sees this command.
            if (_isClosed)
            {
                throw new IOException("Steam's interface closed the connection.");
            }

            using var limit = new CancellationTokenSource(CallTimeLimit, _time);
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token, _closing.Token);
            await SendAsync(Command(id, method, parameters), waiting.Token).ConfigureAwait(false);
            return await answer.Task.WaitAsync(waiting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"Steam's interface did not answer {method}.");
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
        {
            throw new IOException("Steam's interface closed the connection.", exception);
        }
        finally
        {
            _calls.TryRemove(id, out _);
        }
    }

    /// <summary>Runs a script in the page, waiting for the promise it may give, and gives its value.</summary>
    /// <param name="expression">The script, an expression.</param>
    /// <param name="cancellationToken">Stops waiting for the value.</param>
    /// <returns>The value as JSON; an undefined value gives an element of no kind.</returns>
    /// <exception cref="DevToolsException">The script threw.</exception>
    /// <exception cref="IOException">The connection closed first.</exception>
    public async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var result = await CallAsync("Runtime.evaluate", parameters =>
        {
            parameters.WriteString("expression", expression);
            parameters.WriteBoolean("awaitPromise", true);
            parameters.WriteBoolean("returnByValue", true);
        }, cancellationToken).ConfigureAwait(false);
        if (result.GetOrNull("exceptionDetails", JsonValueKind.Object) is { } thrown)
        {
            throw new DevToolsException(thrown.GetOrNull("exception", JsonValueKind.Object)?.GetTextOrNull("description")
                ?? thrown.GetTextOrNull("text") ?? "The script threw.");
        }

        return result.GetOrNull("result", JsonValueKind.Object) is { } value && value.TryGetProperty("value", out var given) ? given : default;
    }

    public async ValueTask DisposeAsync()
    {
        await _closing.CancelAsync().ConfigureAwait(false);
        await _channel.DisposeAsync().ConfigureAwait(false);
        // The rest stays usable, so a command that comes later fails as one on a closed connection does.
        await _receiving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private static byte[] Command(long id, string method, Action<Utf8JsonWriter>? parameters)
    {
        var command = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(command))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WriteStartObject("params");
            parameters?.Invoke(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return command.WrittenSpan.ToArray();
    }

    // The connection allows one message on its way at a time.
    private async Task SendAsync(byte[] message, CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            while (await _channel.ReceiveAsync(_closing.Token).ConfigureAwait(false) is { } message)
            {
                Dispatch(message);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or WebSocketException)
        {
        }
        finally
        {
            _isClosed = true;
            _events.Writer.TryComplete();
            foreach (var call in _calls.Values)
            {
                call.TrySetException(new IOException("Steam's interface closed the connection."));
            }
        }
    }

    // A message that is not the protocol's is left alone; the page has nothing else to say.
    private void Dispatch(byte[] message)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(message);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.GetInt64OrNull("id") is { } id)
        {
            if (_calls.TryGetValue(id, out var call))
            {
                if (root.GetOrNull("error", JsonValueKind.Object) is { } error)
                {
                    call.TrySetException(new DevToolsException(error.GetTextOrNull("message") ?? "The command was refused."));
                }
                else
                {
                    call.TrySetResult(root.GetOrNull("result", JsonValueKind.Object) ?? default);
                }
            }
        }
        else if (root.GetTextOrNull("method") is { } method)
        {
            _events.Writer.TryWrite(new DevToolsEvent(method, root.GetOrNull("params", JsonValueKind.Object) ?? default));
        }
    }
}
