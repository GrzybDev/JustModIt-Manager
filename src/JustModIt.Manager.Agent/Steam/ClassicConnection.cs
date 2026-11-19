using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Steam;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The classic mode's connection to Steam's interface for one run of Steam: over Steam's debug port it places the hook and
/// the launch options script, hands them what the app wants shown, places them again when the interface reloads, and
/// follows the player's changes of launch options.
/// </summary>
internal sealed partial class ClassicConnection : IAsyncDisposable
{
    // Steam opens its port only once its interface is up, seconds after its process starts, or minutes after it on a slow
    // sign-in or when Steam updates itself first.
    private static readonly TimeSpan[] Tries = [.. new[] { 0, 2, 4, 8, 15, 30, 60, 120 }.Select(seconds => TimeSpan.FromSeconds(seconds))];

    // Steam may load the modules the hook looks for after its interface is up.
    private static readonly TimeSpan HookRetry = TimeSpan.FromSeconds(3);
    private const int HookTries = 10;

    private readonly ISteamDevTools _devTools;
    private readonly string _binding;
    private readonly TimeProvider _time;
    private readonly Action _changed;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private SteamUiDemand _demand = SteamUiDemand.None;
    private DevToolsSession? _session;
    private ImmutableDictionary<string, string> _launchOptions = ImmutableDictionary<string, string>.Empty;
    private SteamUiHooks? _hooks;
    private bool _portTaken;
    private bool _gaveUp;

    /// <summary>Starts connecting.</summary>
    /// <param name="devTools">Reaches Steam's debug port.</param>
    /// <param name="process">The run of Steam this connection is for, by its process id.</param>
    /// <param name="patient">Steam started with the switch in place, so its port may come up a while after its start.</param>
    /// <param name="binding">The name the player's changes of launch options are reported under, the app's own.</param>
    /// <param name="time">Paces the tries.</param>
    /// <param name="changed">Called whenever what the connection tells changed.</param>
    /// <param name="logger">Receives what went wrong.</param>
    public ClassicConnection(ISteamDevTools devTools, int process, bool patient, string binding, TimeProvider time, Action changed,
        ILogger logger)
    {
        _devTools = devTools;
        Process = process;
        _binding = binding;
        _time = time;
        _changed = changed;
        _logger = logger;
        _run = Task.Run(() => RunAsync(patient ? Tries : [TimeSpan.Zero], _stop.Token));
    }

    public int Process { get; }

    /// <summary>The hook and the script run in Steam's interface now.</summary>
    public bool IsConnected => Volatile.Read(ref _session) is not null;

    /// <summary>Another program held the port when Steam started.</summary>
    public bool PortTaken => Volatile.Read(ref _portTaken);

    /// <summary>The port never answered, so this run of Steam did not open it.</summary>
    public bool GaveUp => Volatile.Read(ref _gaveUp);

    /// <summary>What the hook could wrap; null before it ran.</summary>
    public SteamUiHooks? Hooks => Volatile.Read(ref _hooks);

    /// <summary>The player's launch options of the games asked about, by app id.</summary>
    public ImmutableDictionary<string, string> LaunchOptions => Volatile.Read(ref _launchOptions);

    /// <summary>Hands over what Steam's interface is to get, which reaches it at once while connected.</summary>
    /// <param name="demand">The texts to show and the games whose launch options to report.</param>
    /// <param name="cancellationToken">Stops handing it over; what is not handed over goes at the next connection.</param>
    public async Task UpdateAsync(SteamUiDemand demand, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _demand, demand) != demand && Volatile.Read(ref _session) is { } session)
        {
            await HandOverAsync(session, demand, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Adds and takes out a game's launch options in Steam.</summary>
    /// <param name="game">The Steam game.</param>
    /// <param name="add">The options to add where they are missing.</param>
    /// <param name="remove">The options to take out where they are present.</param>
    /// <param name="cancellationToken">Stops waiting for Steam.</param>
    /// <returns>The game's options after the change; null when not connected or Steam knows no such game.</returns>
    /// <exception cref="DevToolsException">The script failed in Steam's interface.</exception>
    /// <exception cref="IOException">The connection was lost.</exception>
    public async Task<string?> ChangeAsync(GameKey game, IReadOnlyList<string> add, IReadOnlyList<string> remove,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _session) is not { } session)
        {
            return null;
        }

        var options = await session.EvaluateAsync(Call("window.__launchOptions.change", arguments =>
        {
            arguments.WriteStringValue(game.StoreGameId);
            WriteArray(arguments, add);
            WriteArray(arguments, remove);
        }), cancellationToken).ConfigureAwait(false);
        if (options.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        SetLaunchOptions(game.StoreGameId, options.GetString()!);
        return options.GetString();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private async Task RunAsync(TimeSpan[] tries, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < tries.Length; attempt++)
        {
            await Task.Delay(tries[attempt], _time, cancellationToken).ConfigureAwait(false);
            DevToolsConnection connection;
            try
            {
                connection = await _devTools.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or WebSocketException)
            {
                connection = new DevToolsConnection(DevToolsAnswer.NotListening);
            }

            Volatile.Write(ref _portTaken, connection.Answer == DevToolsAnswer.Taken);
            if (connection.Answer == DevToolsAnswer.Taken)
            {
                LogPortTaken();
                _changed();
                return;
            }

            if (connection.Session is not { } session)
            {
                continue;
            }

            var served = false;
            try
            {
                await ServeAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or DevToolsException or WebSocketException)
            {
                LogLost(exception.GetType().Name);
            }
            finally
            {
                served = Interlocked.Exchange(ref _session, null) is not null;
                await session.DisposeAsync().ConfigureAwait(false);
            }

            _changed();
            // A connection that worked and was lost, as when Steam's interface restarts, gets every try again.
            if (served)
            {
                (tries, attempt) = (Tries, -1);
            }
        }

        Volatile.Write(ref _gaveUp, true);
        _changed();
    }

    private async Task ServeAsync(DevToolsSession session, CancellationToken cancellationToken)
    {
        await session.CallAsync("Page.enable", null, cancellationToken).ConfigureAwait(false);
        await session.CallAsync("Runtime.addBinding", parameters => parameters.WriteString("name", _binding), cancellationToken)
            .ConfigureAwait(false);
        var handed = await PlaceAsync(session, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _session, session);
        // What came while the scripts went in did not reach them.
        if (Volatile.Read(ref _demand) is var demand && demand != handed)
        {
            await HandOverAsync(session, demand, cancellationToken).ConfigureAwait(false);
        }

        _changed();
        _ = PlaceHooksAgainAsync(session, cancellationToken);

        await foreach (var report in session.Events.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (report.Method == "Page.loadEventFired")
            {
                await PlaceAsync(session, cancellationToken).ConfigureAwait(false);
                _ = PlaceHooksAgainAsync(session, cancellationToken);
            }
            else if (report.Method == "Runtime.bindingCalled" && report.Params.GetTextOrNull("name") == _binding)
            {
                OnLaunchOptions(report.Params.GetTextOrNull("payload"));
            }
        }
    }

    // A reload of Steam's interface takes everything away, so everything goes in again; gives what it handed over.
    private async Task<SteamUiDemand> PlaceAsync(DevToolsSession session, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _hooks, ScriptReports.Hooks(await session.EvaluateAsync(SteamScripts.Hook, cancellationToken).ConfigureAwait(false)));
        await session.EvaluateAsync(SteamScripts.LaunchOptions, cancellationToken).ConfigureAwait(false);
        var demand = Volatile.Read(ref _demand);
        await HandOverAsync(session, demand, cancellationToken).ConfigureAwait(false);
        return demand;
    }

    private async Task PlaceHooksAgainAsync(DevToolsSession session, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < HookTries && Hooks is not { Achievements: true, Notifications: true }; attempt++)
            {
                await Task.Delay(HookRetry, _time, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _hooks, ScriptReports.Hooks(await session.EvaluateAsync("window.__achievementTexts.install()", cancellationToken)
                    .ConfigureAwait(false)));
                _changed();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or DevToolsException)
        {
        }
    }

    private async Task HandOverAsync(DevToolsSession session, SteamUiDemand demand, CancellationToken cancellationToken)
    {
        await session.EvaluateAsync(Call("window.__achievementTexts.setTexts", arguments =>
        {
            arguments.WriteRawValue(demand.Texts.Span, skipInputValidation: true);
            arguments.WriteStringValue(demand.Language);
        }), cancellationToken).ConfigureAwait(false);

        List<string> games = [.. demand.LaunchOptionsGames.Select(game => game.StoreGameId)];
        var read = await session.EvaluateAsync(Call("window.__launchOptions.read", arguments => WriteArray(arguments, games)),
            cancellationToken).ConfigureAwait(false);
        if (read.ValueKind == JsonValueKind.Object)
        {
            Volatile.Write(ref _launchOptions, ScriptReports.LaunchOptions(read));
        }

        var binding = JsonEncodedText.Encode(_binding).ToString();
        await session.EvaluateAsync(Call("window.__launchOptions.watch", arguments =>
            {
                arguments.WriteStringValue(_binding);
                WriteArray(arguments, games);
            }, $"(app, options) => window[\"{binding}\"](JSON.stringify({{ app, options }}))"), cancellationToken)
            .ConfigureAwait(false);
        _changed();
    }

    private void OnLaunchOptions(string? payload)
    {
        try
        {
            using var report = JsonDocument.Parse(payload ?? "");
            if (report.RootElement.GetTextOrNull("app") is { } app && report.RootElement.GetTextOrNull("options") is { } options)
            {
                SetLaunchOptions(app, options);
                _changed();
            }
        }
        catch (JsonException)
        {
        }
    }

    private void SetLaunchOptions(string app, string options)
    {
        ImmutableDictionary<string, string> before, after;
        do
        {
            before = Volatile.Read(ref _launchOptions);
            after = before.SetItem(app, options);
        }
        while (Interlocked.CompareExchange(ref _launchOptions, after, before) != before);
    }

    // The arguments go in as one JSON array spread into the call, so no value is ever spliced into the script as text.
    private static string Call(string function, Action<Utf8JsonWriter> arguments, string? lastArgument = null)
    {
        var values = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(values))
        {
            writer.WriteStartArray();
            arguments(writer);
            writer.WriteEndArray();
        }

        var call = new StringBuilder(function).Append("(...").Append(Encoding.UTF8.GetString(values.WrittenSpan));
        return (lastArgument is null ? call : call.Append(", ").Append(lastArgument)).Append(')').ToString();
    }

    private static void WriteArray(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Another program holds the port of Steam's classic mode")]
    private partial void LogPortTaken();

    [LoggerMessage(Level = LogLevel.Information, Message = "The connection to Steam's interface was lost: {Error}")]
    private partial void LogLost(string error);
}
