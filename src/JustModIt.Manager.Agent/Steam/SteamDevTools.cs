using System.Net.WebSockets;
using System.Text.Json;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>What answered on Steam's debug port.</summary>
internal enum DevToolsAnswer
{
    /// <summary>Steam's interface, now connected.</summary>
    Connected,

    /// <summary>Nothing, or Steam before its interface is up.</summary>
    NotListening,

    /// <summary>Another program, which holds the port Steam would open.</summary>
    Taken,
}

/// <summary>What answered on Steam's debug port, with the session to Steam's interface when it was Steam.</summary>
/// <param name="Answer">What answered.</param>
/// <param name="Session">The session with Steam's shared script context; null unless connected.</param>
internal sealed record DevToolsConnection(DevToolsAnswer Answer, DevToolsSession? Session = null);

/// <summary>Reaches Steam's interface over the debug port Steam opens in the classic mode.</summary>
internal interface ISteamDevTools
{
    /// <summary>Connects to the page of Steam's interface that holds its shared scripts.</summary>
    /// <param name="cancellationToken">Stops connecting.</param>
    Task<DevToolsConnection> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>Steam's debug port on this computer, which Steam always opens at the same address.</summary>
/// <param name="time">The clock that bounds the sessions' commands.</param>
internal sealed class SteamDevTools(TimeProvider time) : ISteamDevTools
{
    private const string Host = "127.0.0.1:8080";

    // Steam serves its own interface from this host, which no other program on the port does.
    private const string InterfaceHost = "steamloopback.host";

    private static readonly Uri Targets = new($"http://{Host}/json/list");

    public async Task<DevToolsConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        byte[] list;
        // The site's clients carry its key and name, so the local port gets a client of its own.
        using (var handler = new SocketsHttpHandler { UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
        using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) })
        {
            try
            {
                list = await http.GetByteArrayAsync(Targets, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConnectionError)
            {
                return new DevToolsConnection(DevToolsAnswer.NotListening);
            }
            // Something holds the port but does not answer as Steam does, or not in time.
            catch (Exception exception) when (exception is HttpRequestException
                || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return new DevToolsConnection(DevToolsAnswer.Taken);
            }
        }

        var (answer, target) = Read(list);
        if (target is null)
        {
            return new DevToolsConnection(answer);
        }

        var socket = new ClientWebSocket();
        // Pings would wake the agent every half minute while it idles in the tray.
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        socket.Options.Proxy = null;
        try
        {
            await socket.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            socket.Dispose();
            return new DevToolsConnection(DevToolsAnswer.NotListening);
        }

        return new DevToolsConnection(DevToolsAnswer.Connected, new DevToolsSession(new WebSocketChannel(socket), time));
    }

    /// <summary>What the port's list of pages tells: the address of Steam's shared script context, or why there is none.</summary>
    /// <param name="list">The list as the port served it.</param>
    internal static (DevToolsAnswer Answer, Uri? Target) Read(byte[] list)
    {
        var pages = Pages(list);
        var target = pages?.Where(page => page.GetTextOrNull("title") == "SharedJSContext")
            .Select(page => Uri.TryCreate(page.GetTextOrNull("webSocketDebuggerUrl"), UriKind.Absolute, out var address) ? address : null)
            .FirstOrDefault(address => address is { Scheme: "ws", Authority: Host });
        // Steam lists no page, or only others, while its interface is not up yet, as at its start or while it starts it again.
        var answer = target is not null ? DevToolsAnswer.Connected
            : pages is { Count: 0 } || pages?.Exists(IsSteams) == true ? DevToolsAnswer.NotListening
            : DevToolsAnswer.Taken;
        return (answer, target);
    }

    private static bool IsSteams(JsonElement page) =>
        Uri.TryCreate(page.GetTextOrNull("url"), UriKind.Absolute, out var address) && address.Host == InterfaceHost;

    // Null when the answer is not a list of pages.
    private static List<JsonElement>? Pages(byte[] list)
    {
        try
        {
            using var document = JsonDocument.Parse(list);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? [.. document.RootElement.EnumerateArray().Where(page => page.ValueKind == JsonValueKind.Object).Select(page => page.Clone())]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A web socket carrying whole text messages.</summary>
    /// <param name="socket">The open socket, closed with the channel.</param>
    private sealed class WebSocketChannel(ClientWebSocket socket) : IDevToolsChannel
    {
        private readonly byte[] _buffer = new byte[16 * 1024];

        public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken) =>
            socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).AsTask();

        public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            while (true)
            {
                var received = await socket.ReceiveAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(_buffer, 0, received.Count);
                if (received.EndOfMessage)
                {
                    return message.ToArray();
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            socket.Abort();
            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
