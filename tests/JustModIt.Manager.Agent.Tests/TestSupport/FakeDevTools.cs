using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>Steam's debug port, answering as the test says; while connected, each connection is a new <see cref="FakeSteamPage"/>.</summary>
internal sealed class FakeDevTools : ISteamDevTools
{
    private int _connections;

    public DevToolsAnswer Answer { get; set; } = DevToolsAnswer.Connected;

    public int Connections => Volatile.Read(ref _connections);

    public FakeSteamPage? Page { get; private set; }

    /// <summary>The player's launch options by app id, which every page shares, as Steam does.</summary>
    public ConcurrentDictionary<string, string> LaunchOptions { get; } = new();

    public Task<DevToolsConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _connections);
        if (Answer != DevToolsAnswer.Connected)
        {
            return Task.FromResult(new DevToolsConnection(Answer));
        }

        Page = new FakeSteamPage(LaunchOptions);
        return Task.FromResult(new DevToolsConnection(DevToolsAnswer.Connected, new DevToolsSession(Page, TimeProvider.System)));
    }
}

/// <summary>
/// Steam's shared script context as far as the app talks to it: it answers the protocol's commands and runs what the app
/// evaluates the way the scripts would.
/// </summary>
internal sealed class FakeSteamPage(ConcurrentDictionary<string, string> launchOptions) : IDevToolsChannel
{
    private readonly Channel<byte[]> _toAgent = Channel.CreateUnbounded<byte[]>();

    public ConcurrentQueue<string> Evaluated { get; } = new();

    public string? Binding { get; private set; }

    /// <summary>The texts and the language the app handed the hook last, as the call's arguments.</summary>
    public string? Texts { get; private set; }

    public int HookRuns => Evaluated.Count(expression => expression == SteamScripts.Hook);

    public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        using var command = JsonDocument.Parse(message);
        var id = command.RootElement.GetProperty("id").GetInt64();
        var parameters = command.RootElement.GetProperty("params");
        switch (command.RootElement.GetProperty("method").GetString())
        {
            case "Runtime.addBinding":
                Binding = parameters.GetProperty("name").GetString();
                Answer(id, null);
                break;
            case "Runtime.evaluate":
                var expression = parameters.GetProperty("expression").GetString()!;
                Evaluated.Enqueue(expression);
                Answer(id, Evaluate(expression));
                break;
            default:
                Answer(id, null);
                break;
        }

        return Task.CompletedTask;
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken) =>
        await _toAgent.Reader.WaitToReadAsync(cancellationToken) ? await _toAgent.Reader.ReadAsync(cancellationToken) : null;

    public ValueTask DisposeAsync()
    {
        _toAgent.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    /// <summary>The player changes a game's options in Steam, which the launch options script reports.</summary>
    public void Edit(string app, string options)
    {
        launchOptions[app] = options;
        Report("Runtime.bindingCalled", writer =>
        {
            writer.WriteString("name", Binding);
            writer.WriteString("payload", JsonSerializer.Serialize(new Dictionary<string, string> { ["app"] = app, ["options"] = options }));
        });
    }

    /// <summary>Steam's interface loads again, which takes away what was placed in it.</summary>
    public void Reload() => Report("Page.loadEventFired", writer => writer.WriteNumber("timestamp", 1));

    public void Close() => _toAgent.Writer.TryComplete();

    private string? Evaluate(string expression)
    {
        if (expression == SteamScripts.Hook || expression.StartsWith("window.__achievementTexts.install(", StringComparison.Ordinal))
        {
            return """{"achievements": true, "notifications": true}""";
        }

        if (expression.StartsWith("window.__achievementTexts.setTexts(", StringComparison.Ordinal))
        {
            Texts = Arguments(expression).GetRawText();
            return null;
        }

        if (expression.StartsWith("window.__launchOptions.read(", StringComparison.Ordinal))
        {
            var read = Arguments(expression)[0].EnumerateArray().Select(app => app.GetString()!).Where(launchOptions.ContainsKey)
                .ToDictionary(app => app, app => launchOptions[app]);
            return JsonSerializer.Serialize(read);
        }

        if (expression.StartsWith("window.__launchOptions.change(", StringComparison.Ordinal))
        {
            var arguments = Arguments(expression);
            var app = arguments[0].GetString()!;
            if (!launchOptions.TryGetValue(app, out var options))
            {
                return "null";
            }

            launchOptions[app] = SteamLaunchOptions.Changed(options, [.. arguments[1].EnumerateArray().Select(option => option.GetString()!)],
                [.. arguments[2].EnumerateArray().Select(option => option.GetString()!)]);
            return JsonSerializer.Serialize(launchOptions[app]);
        }

        return expression == SteamScripts.LaunchOptions ? "{}" : null;
    }

    // The app spreads its arguments from one JSON array into each call.
    private static JsonElement Arguments(string expression)
    {
        var start = expression.IndexOf("(...", StringComparison.Ordinal) + 4;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(expression[start..]));
        return JsonElement.ParseValue(ref reader);
    }

    private void Answer(long id, string? value) => Send(writer =>
    {
        writer.WriteNumber("id", id);
        writer.WriteStartObject("result");
        if (value is not null)
        {
            writer.WriteStartObject("result");
            writer.WritePropertyName("value");
            writer.WriteRawValue(value);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    });

    private void Report(string method, Action<Utf8JsonWriter> parameters) => Send(writer =>
    {
        writer.WriteString("method", method);
        writer.WriteStartObject("params");
        parameters(writer);
        writer.WriteEndObject();
    });

    private void Send(Action<Utf8JsonWriter> write)
    {
        var message = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(message))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        _toAgent.Writer.TryWrite(message.WrittenSpan.ToArray());
    }
}
