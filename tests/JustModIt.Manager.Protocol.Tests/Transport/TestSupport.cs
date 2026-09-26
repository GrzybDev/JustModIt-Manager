using System.Threading.Channels;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Protocol.Tests.Transport;

internal static class TestPipes
{
    public static readonly Hello AgentHello = new(Hello.CurrentProtocolVersion, "1.0.0", ProcessRole.Agent, 1001);
    public static readonly Hello UiHello = new(Hello.CurrentProtocolVersion, "1.0.0", ProcessRole.Ui, 1002);
    public static readonly Hello LauncherHello = new(Hello.CurrentProtocolVersion, "1.0.0", ProcessRole.Launcher, 1003);

    public static readonly IpcOptions Options = new()
    {
        HandshakeTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(10),
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(100),
    };

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public static string NewName() => $"ExampleApp.Tests.{Guid.NewGuid():N}";

    public static PipeServer StartServer(
        string name,
        TestHandler handler,
        IpcOptions? options = null,
        ILogger? logger = null,
        Hello? hello = null)
    {
        var server = new PipeServer(name, hello ?? AgentHello, handler, logger ?? new ListLogger(), options ?? Options);
        server.Start();
        return server;
    }

    public static Task<PipeConnection> ConnectAsync(string name, Hello? hello = null, TestHandler? handler = null) =>
        PipeClient.ConnectAsync(
            name,
            hello ?? UiHello,
            ConnectTimeout,
            handler,
            Options,
            cancellationToken: TestContext.Current.CancellationToken);
}

internal sealed class TestHandler : IConnectionHandler
{
    public Func<PipeConnection, Message, CancellationToken, ValueTask<Message?>> OnRequest { get; init; } =
        (_, _, _) => ValueTask.FromResult<Message?>(null);

    public Channel<PipeConnection> Connected { get; } = Channel.CreateUnbounded<PipeConnection>();

    public Channel<PipeConnection> Disconnected { get; } = Channel.CreateUnbounded<PipeConnection>();

    public Channel<Message> Events { get; } = Channel.CreateUnbounded<Message>();

    public void OnConnected(PipeConnection connection) => Connected.Writer.TryWrite(connection);

    public ValueTask<Message?> HandleRequestAsync(
        PipeConnection connection,
        Message request,
        CancellationToken cancellationToken) =>
        OnRequest(connection, request, cancellationToken);

    public void OnEvent(PipeConnection connection, Message message) => Events.Writer.TryWrite(message);

    public void OnDisconnected(PipeConnection connection) => Disconnected.Writer.TryWrite(connection);
}

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

internal sealed class ListLogger : ILogger
{
    private readonly Lock _lock = new();
    private readonly List<LogEntry> _entries = [];
    private readonly List<(Func<LogEntry, bool> Match, TaskCompletionSource<LogEntry> Found)> _waiters = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public Task<LogEntry> WaitForAsync(Func<LogEntry, bool> match)
    {
        lock (_lock)
        {
            if (_entries.FirstOrDefault(match) is { } logged)
            {
                return Task.FromResult(logged);
            }

            var found = new TaskCompletionSource<LogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((match, found));
            return found.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry(logLevel, formatter(state, exception), exception);
        lock (_lock)
        {
            _entries.Add(entry);
            _waiters.RemoveAll(waiter => waiter.Match(entry) && waiter.Found.TrySetResult(entry));
        }
    }
}

internal static class ChannelExtensions
{
    public static async Task<T> NextAsync<T>(this Channel<T> channel) =>
        await channel.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    public static async Task ExpectAsync<T>(this Channel<T> channel, params T[] expected)
    {
        foreach (var item in expected)
        {
            (await channel.NextAsync()).Should().Be(item);
        }
    }
}
