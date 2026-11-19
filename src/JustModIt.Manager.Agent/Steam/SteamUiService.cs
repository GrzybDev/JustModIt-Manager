using System.Collections.Immutable;
using System.Threading.Channels;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The app's way into Steam's interface. It follows Steam's starts and exits. With Millennium it works through the app's
/// plugin (<see cref="MillenniumDelivery"/>); without it, in the classic mode, it puts the switch for Steam's debug port in
/// place and works through that port while Steam runs.
/// </summary>
internal sealed partial class SteamUiService : ISteamUi, IAgentService, IDisposable
{
    private const string ExitLink = "steam://exit";
    private const string StartLink = "steam://open/main";

    private static readonly TimeSpan ExitTimeLimit = TimeSpan.FromMinutes(1);

    private readonly ISteamClient _steam;
    private readonly ISteamDevTools _devTools;
    private readonly ISettingsService _settings;
    private readonly AgentOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SteamUiService> _logger;
    private readonly MillenniumDelivery _millennium;

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    // One change of Steam's folder or interface at a time, each made from the newest of everything.
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly Lock _gate = new();
    private SteamUiStatus _status = SteamUiStatus.None;
    private SteamUiDemand? _demand;
    private CancellationToken _stopping;

    private int? _process;
    private SteamRun? _run;
    private int _exitedProcess;
    private int _staleProcess;
    private int _restarting;

    private ClassicConnection? _classic;

    /// <summary>Follows Steam with nothing changed in its folder until the first demand.</summary>
    /// <param name="steam">The Steam client on this computer.</param>
    /// <param name="devTools">Reaches Steam's debug port in the classic mode.</param>
    /// <param name="folders">Reports the plugin's reports.</param>
    /// <param name="settings">Whether the first-run setup is done and which ways into Steam the player allows.</param>
    /// <param name="options">The app's names and version, which name the plugin and the classic mode's switch.</param>
    /// <param name="time">The clock that bounds waits for Steam.</param>
    /// <param name="logger">Receives what could not be done.</param>
    public SteamUiService(ISteamClient steam, ISteamDevTools devTools, IFolderWatcher folders, ISettingsService settings,
        AgentOptions options, TimeProvider time, ILogger<SteamUiService> logger)
    {
        (_steam, _devTools, _settings, _options, _time, _logger) = (steam, devTools, settings, options, time, logger);
        _millennium = new MillenniumDelivery(folders, options, time, Wake, logger);
    }

    public event EventHandler<SteamUiStatus>? Changed;

    public SteamUiStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    // Names in a page's scripts are made of letters, digits and underscores.
    private string Binding => "__launchOptionsOf_"
        + string.Concat(_options.Identity.InstanceName.Select(letter => char.IsAsciiLetterOrDigit(letter) ? letter : '_'));

    private string SwitchOwner => ClassicSwitch.OwnerOf(_options.Identity);

    /// <summary>Hands over what Steam's interface is to get, which reaches it as soon as it can.</summary>
    /// <param name="demand">The texts to show and the games whose launch options the app manages.</param>
    public void Demand(SteamUiDemand demand)
    {
        Volatile.Write(ref _demand, demand);
        Wake();
    }

    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        _settings.Changed += OnSettingsChanged;
        using var watch = _steam.Watch(Wake);
        try
        {
            while (true)
            {
                await ReconcileInTurnAsync(cancellationToken).ConfigureAwait(false);
                await _wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _settings.Changed -= OnSettingsChanged;
            _millennium.Stop();
            await StopClassicAsync(SteamUiDemand.None, clear: false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public SteamRestartResult RestartSteam()
    {
        if (Status.RunningGame is not null)
        {
            return SteamRestartResult.GameRunning;
        }

        // A request while Steam closes joins that restart, since asking a Steam that is gone to exit would start it.
        if (Interlocked.Exchange(ref _restarting, 1) == 1)
        {
            return SteamRestartResult.Restarting;
        }

        var running = _process;
        if (running is not null && !_steam.Open(ExitLink))
        {
            Volatile.Write(ref _restarting, 0);
            return SteamRestartResult.Failed;
        }

        _ = StartAgainAsync(running);
        return SteamRestartResult.Restarting;
    }

    public async Task<LaunchOptionsResult> ChangeLaunchOptionsAsync(GameKey game, IReadOnlyList<string> add, IReadOnlyList<string> remove,
        CancellationToken cancellationToken)
    {
        await _work.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var status = Status;
            // Before the first demand the plugin's texts file would lose the texts it holds.
            var result = status is { Reachable: true, Delivery: SteamUiDelivery.Classic } && _classic is { } classic
                ? await ChangeThroughPortAsync(classic, game, add, remove, cancellationToken).ConfigureAwait(false)
                : Volatile.Read(ref _demand) is not { } demand ? new LaunchOptionsResult(null, LaunchOptionsFailure.Unreachable)
                : await _millennium.ChangeLaunchOptionsAsync(status, demand, game, add, remove, cancellationToken).ConfigureAwait(false);
            if (result.Options is { } changed)
            {
                Publish(status with { LaunchOptions = status.LaunchOptions.SetItem(game, changed) });
            }

            return result;
        }
        finally
        {
            _work.Release();
        }
    }

    public void Dispose()
    {
        _work.Dispose();
        _millennium.Dispose();
    }

    // The player closed Steam to have it back, so it starts again even when what waits for it to be closed could not go in.
    private async Task StartAgainAsync(int? running)
    {
        try
        {
            if (running is { } process)
            {
                var exited = false;
                await _time.WaitForSignalAsync(async token => exited = await _steam.WaitForExitAsync(process, token).ConfigureAwait(false),
                    ExitTimeLimit, _stopping).ConfigureAwait(false);
                if (!exited)
                {
                    LogSteamDidNotExit();
                    return;
                }

                Volatile.Write(ref _exitedProcess, process);
            }

            // What waits for Steam to be closed goes in before it starts again.
            await ReconcileInTurnAsync(_stopping).ConfigureAwait(false);
            if (!_steam.Open(StartLink))
            {
                LogSteamNotStarted();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _restarting, 0);
        }
    }

    private async Task ReconcileInTurnAsync(CancellationToken cancellationToken)
    {
        await _work.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        }
        // Steam's folder may be locked down or on a drive that went away; the next change tries again.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogNotBroughtUpToDate(exception.GetType().Name);
        }
        finally
        {
            _work.Release();
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var root = _steam.FindRoot();
        var session = _steam.Read();
        var running = Follow(session.ProcessId);
        var current = _settings.Current;
        // Before the first demand nothing in Steam's folder changes, since what is there may be needed.
        var demand = Volatile.Read(ref _demand);
        var needed = current.OnboardingCompleted && demand is { IsNeeded: true };
        var status = new SteamUiStatus
        {
            InterfaceLanguage = session.Language,
            RunningGame = running ? session.RunningGame : null,
            IsNeeded = needed,
        };

        var millennium = root is not null && MillenniumPlugin.IsInstalledIn(root);
        var classic = root is not null && !millennium && current.AchievementClassicMode;
        if (root is not null && !(classic && (needed || demand is null)))
        {
            // The port closes at Steam's next start.
            Switch(() => ClassicSwitch.TurnOff(root, SwitchOwner));
        }

        if (!(classic && needed && running))
        {
            // A Steam that keeps running would go on showing the texts it was given.
            await StopClassicAsync(SteamUiDemand.None, clear: running, cancellationToken).ConfigureAwait(false);
        }

        if (millennium)
        {
            var unused = demand is not null && !current.AchievementTexts && demand.LaunchOptionsGames.Count == 0;
            status = await _millennium.ReconcileAsync(root!, running ? _run : null, needed, unused, demand, status, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _millennium.Stop();
            if (classic)
            {
                status = await ReconcileClassicAsync(root!, needed, demand, status, cancellationToken).ConfigureAwait(false);
            }
        }

        Publish(status);
    }

    private async Task<SteamUiStatus> ReconcileClassicAsync(string root, bool needed, SteamUiDemand? demand, SteamUiStatus status,
        CancellationToken cancellationToken)
    {
        status = status with { Delivery = SteamUiDelivery.Classic };
        var created = false;
        if (needed)
        {
            Switch(() => created = ClassicSwitch.TurnOn(root, SwitchOwner));
        }

        if (!needed || demand is null || _process is not { } process)
        {
            return status;
        }

        if (_classic?.Process != process)
        {
            await StopClassicAsync(demand, clear: false, cancellationToken).ConfigureAwait(false);
            // A Steam that started before the switch was there has no port to wait for.
            _classic = new ClassicConnection(_devTools, process, patient: !created, Binding, _time, Wake, _logger);
        }

        var classic = _classic;
        try
        {
            await classic.UpdateAsync(demand, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or DevToolsException)
        {
            LogNotHandedOver(exception.GetType().Name);
        }

        var reported = classic.LaunchOptions;
        return status with
        {
            Reachable = classic.IsConnected,
            RestartNeeded = classic.GaveUp,
            PortTaken = classic.PortTaken,
            Hooks = classic.Hooks,
            LaunchOptions = new ValueDictionary<GameKey, string>(demand.LaunchOptionsGames.Where(game => reported.ContainsKey(game.StoreGameId))
                .ToImmutableDictionary(game => game, game => reported[game.StoreGameId])),
        };
    }

    private async Task<LaunchOptionsResult> ChangeThroughPortAsync(ClassicConnection classic, GameKey game, IReadOnlyList<string> add,
        IReadOnlyList<string> remove, CancellationToken cancellationToken)
    {
        try
        {
            return await classic.ChangeAsync(game, add, remove, cancellationToken).ConfigureAwait(false) is { } changed
                ? new LaunchOptionsResult(changed)
                : new LaunchOptionsResult(null, LaunchOptionsFailure.Failed);
        }
        catch (Exception exception) when (exception is IOException or DevToolsException)
        {
            LogNotHandedOver(exception.GetType().Name);
            return new LaunchOptionsResult(null, LaunchOptionsFailure.Failed);
        }
    }

    // A connection that is cleared first hands Steam's interface no texts before it goes.
    private async Task StopClassicAsync(SteamUiDemand demand, bool clear, CancellationToken cancellationToken)
    {
        if (_classic is not { } classic)
        {
            return;
        }

        _classic = null;
        if (clear && classic.Process == _process)
        {
            try
            {
                await classic.UpdateAsync(demand, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or DevToolsException)
            {
                LogNotHandedOver(exception.GetType().Name);
            }
        }

        await classic.DisposeAsync().ConfigureAwait(false);
    }

    // The app works without the classic mode, so a folder it may not write to only keeps the mode from working.
    private void Switch(Action change)
    {
        try
        {
            change();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSwitchNotChanged(exception.GetType().Name);
        }
    }

    // Steam records its process until it exits cleanly, so a process that was stopped from outside is still recorded.
    private bool Follow(int? processId)
    {
        if (processId is not { } id || id == Volatile.Read(ref _exitedProcess) || id == _staleProcess)
        {
            _process = null;
            return false;
        }

        if (id == _process)
        {
            return true;
        }

        if (!_steam.IsRunning(id))
        {
            _staleProcess = id;
            _process = null;
            return false;
        }

        _process = id;
        _run = new SteamRun(_steam.StartedAt(id), _time.GetUtcNow());
        _ = WaitForExitAsync(id);
        return true;
    }

    private async Task WaitForExitAsync(int processId)
    {
        try
        {
            if (await _steam.WaitForExitAsync(processId, _stopping).ConfigureAwait(false))
            {
                Volatile.Write(ref _exitedProcess, processId);
                Wake();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Publish(SteamUiStatus status)
    {
        lock (_gate)
        {
            if (status == _status)
            {
                return;
            }

            _status = status;
        }

        EventRaising.RaiseEach(Changed, this, status, LogListenerFailed);
    }

    private void Wake() => _wake.Writer.TryWrite(true);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Wake();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam's interface could not be brought up to date: {Error}")]
    private partial void LogNotBroughtUpToDate(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The switch of Steam's debug port could not be changed: {Error}")]
    private partial void LogSwitchNotChanged(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam's interface did not take what the app handed over: {Error}")]
    private partial void LogNotHandedOver(string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam did not exit in time, so it was not started again")]
    private partial void LogSteamDidNotExit();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam could not be started again")]
    private partial void LogSteamNotStarted();

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of Steam's interface status failed")]
    private partial void LogListenerFailed(Exception exception);
}
