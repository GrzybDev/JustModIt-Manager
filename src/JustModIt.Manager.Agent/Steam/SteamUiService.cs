using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The app's way into Steam's interface. It follows Steam's starts and exits, installs the app's Millennium plugin while
/// Steam is closed, writes the plugin the achievement texts to show and the launch options to change, and reads back what
/// the plugin reports.
/// </summary>
/// <param name="steam">The Steam client on this computer.</param>
/// <param name="folders">Reports the plugin's reports.</param>
/// <param name="settings">Whether the first-run setup is done and the classic mode is on.</param>
/// <param name="options">The app's name and version, which name the plugin and fill its panel.</param>
/// <param name="time">The clock that bounds waits for Steam.</param>
/// <param name="logger">Receives what could not be done.</param>
internal sealed partial class SteamUiService(ISteamClient steam, IFolderWatcher folders, ISettingsService settings, AgentOptions options,
    TimeProvider time, ILogger<SteamUiService> logger) : ISteamUi, IAgentService, IDisposable
{
    private const string ExitLink = "steam://exit";
    private const string StartLink = "steam://open/main";

    private static readonly TimeSpan ExitTimeLimit = TimeSpan.FromMinutes(1);

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    // One change of Steam's folder or interface at a time, each made from the newest of everything.
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly Lock _gate = new();
    private SteamUiStatus _status = SteamUiStatus.None;
    private SteamUiDemand _demand = SteamUiDemand.None;
    private CancellationToken _stopping;

    private int? _process;
    private int _exitedProcess;
    private int _staleProcess;
    private int _restarting;

    private IReadOnlyDictionary<string, byte[]>? _pluginFiles;
    private MillenniumPlugin? _plugin;
    private PluginReport _report = PluginReport.None;
    private List<PendingLaunchOptions>? _pending;
    private long _lastChange;
    private byte[]? _writtenTexts;
    private string? _watchedFolder;
    private IDisposable? _statusWatch;

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

    private IReadOnlyDictionary<string, byte[]> PluginFiles => _pluginFiles ??= MillenniumPlugin.Files(options.Identity.Slug,
        options.Version, Text.Format(Strings.SteamPlugin_Name, ("product", options.ProductName)),
        Text.Format(Strings.SteamPlugin_Description, ("product", options.ProductName)));

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
        settings.Changed += OnSettingsChanged;
        using var watch = steam.Watch(Wake);
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
            settings.Changed -= OnSettingsChanged;
            WatchStatus(null);
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
        if (running is not null && !steam.Open(ExitLink))
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
            if (!status.Reachable || _plugin is not { } plugin || _pending is not { } pending)
            {
                return new LaunchOptionsResult(null, LaunchOptionsFailure.Unreachable);
            }

            // The plugin makes the change the next time it reads its texts file, at the latest before a game starts.
            pending.Add(new PendingLaunchOptions(++_lastChange, game, [.. add], [.. remove]));
            if (!await WriteTextsAsync(plugin, Volatile.Read(ref _demand), cancellationToken).ConfigureAwait(false))
            {
                pending.RemoveAt(pending.Count - 1);
                return new LaunchOptionsResult(null, LaunchOptionsFailure.Failed);
            }

            var changed = LaunchOptionsText.Changed(status.LaunchOptions.GetValueOrDefault(game, ""), add, remove);
            Publish(status with { LaunchOptions = status.LaunchOptions.SetItem(game, changed) });
            return new LaunchOptionsResult(changed);
        }
        finally
        {
            _work.Release();
        }
    }

    public void Dispose()
    {
        _work.Dispose();
        _statusWatch?.Dispose();
    }

    // The player closed Steam to have it back, so it starts again even when what waits for it to be closed could not go in.
    private async Task StartAgainAsync(int? running)
    {
        try
        {
            if (running is { } process)
            {
                var exited = false;
                await time.WaitForSignalAsync(async token => exited = await steam.WaitForExitAsync(process, token).ConfigureAwait(false),
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
            if (!steam.Open(StartLink))
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
        var root = steam.FindRoot();
        var session = steam.Read();
        var running = Follow(session.ProcessId);
        var current = settings.Current;
        var demand = Volatile.Read(ref _demand);
        var needed = current.OnboardingCompleted && demand.IsNeeded;
        var status = new SteamUiStatus
        {
            InterfaceLanguage = session.Language,
            RunningGame = running ? session.RunningGame : null,
        };

        if (root is not null && MillenniumPlugin.IsInstalledIn(root))
        {
            status = await ReconcileMillenniumAsync(Plugin(root), running, needed, demand, status, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            WatchStatus(null);
        }

        Publish(status);
    }

    private async Task<SteamUiStatus> ReconcileMillenniumAsync(MillenniumPlugin plugin, bool running, bool needed, SteamUiDemand demand,
        SteamUiStatus status, CancellationToken cancellationToken)
    {
        var loaded = plugin.IsEnabled() ? plugin.InstalledVersion() : null;
        if (!running && (needed || loaded is not null) && !plugin.IsCurrent(PluginFiles))
        {
            try
            {
                await plugin.InstallAsync(PluginFiles, cancellationToken).ConfigureAwait(false);
                loaded = options.Version;
                LogPluginInstalled(loaded);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                LogPluginNotInstalled(exception.GetType().Name);
            }
        }

        WatchStatus(plugin);
        ReadReport(plugin);
        if (Directory.Exists(plugin.Directory))
        {
            await WriteTextsAsync(plugin, demand, cancellationToken).ConfigureAwait(false);
        }

        // A plugin in place while Steam runs was loaded at its start, since it is written only while Steam is closed.
        var reachable = running && loaded is not null;
        return status with
        {
            Delivery = SteamUiDelivery.Millennium,
            Reachable = reachable,
            RestartNeeded = running && needed && loaded is null,
            Hooks = _report.Hooks,
            LaunchOptions = LaunchOptionsOf(demand.LaunchOptionsGames, reachable),
            MillenniumVersion = _report.Millennium,
            PluginVersion = loaded ?? "",
        };
    }

    private MillenniumPlugin Plugin(string root)
    {
        var plugin = new MillenniumPlugin(root, options.Identity.Slug);
        if (_plugin?.Directory != plugin.Directory)
        {
            _plugin = plugin;
            _report = PluginReport.None;
            _pending = null;
            _writtenTexts = null;
        }

        return _plugin;
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

        if (!steam.IsRunning(id))
        {
            _staleProcess = id;
            _process = null;
            return false;
        }

        _process = id;
        _ = WaitForExitAsync(id);
        return true;
    }

    private async Task WaitForExitAsync(int processId)
    {
        try
        {
            if (await steam.WaitForExitAsync(processId, _stopping).ConfigureAwait(false))
            {
                Volatile.Write(ref _exitedProcess, processId);
                Wake();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReadReport(MillenniumPlugin plugin)
    {
        using var report = plugin.ReadStatus();
        if (report?.RootElement is { ValueKind: JsonValueKind.Object } root)
        {
            _report = PluginReport.Read(root);
        }
        // A report half written keeps the one before it.
        else if (!File.Exists(Path.Join(plugin.Directory, MillenniumPlugin.StatusFileName)))
        {
            _report = PluginReport.None;
        }

        _pending ??= plugin.ReadTexts() is { } written ? [.. PluginTexts.ReadChanges(written)] : [];
        _pending.RemoveAll(change => change.Id <= _report.Applied);
        _lastChange = Math.Max(_report.Applied, _pending.Select(change => change.Id).DefaultIfEmpty(_lastChange).Max());
    }

    private async Task<bool> WriteTextsAsync(MillenniumPlugin plugin, SteamUiDemand demand, CancellationToken cancellationToken)
    {
        var content = PluginTexts.Write(demand, _pending ?? [], options, time.LocalTimeZone);
        _writtenTexts ??= plugin.ReadTexts();
        if (_writtenTexts is not null && _writtenTexts.AsSpan().SequenceEqual(content))
        {
            return true;
        }

        try
        {
            await plugin.WriteTextsAsync(content, cancellationToken).ConfigureAwait(false);
            _writtenTexts = content;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogTextsNotWritten(exception.GetType().Name);
            return false;
        }
    }

    // The player's options as the plugin reported them, with the changes it has not made yet.
    private ValueDictionary<GameKey, string> LaunchOptionsOf(ValueList<GameKey> games, bool reachable)
    {
        var result = ImmutableDictionary<GameKey, string>.Empty;
        foreach (var game in games)
        {
            // A game the plugin never reported is taken as having none while the plugin runs, so what it needs gets queued.
            if ((_report.LaunchOptions.GetValueOrDefault(game.StoreGameId) ?? (reachable ? "" : null)) is not { } reported)
            {
                continue;
            }

            result = result.Add(game, (_pending ?? []).Where(change => change.Game == game)
                .Aggregate(reported, (current, change) => LaunchOptionsText.Changed(current, change.Add, change.Remove)));
        }

        return new ValueDictionary<GameKey, string>(result);
    }

    private void WatchStatus(MillenniumPlugin? plugin)
    {
        var folder = plugin is not null && Directory.Exists(plugin.Directory) ? plugin.Directory : null;
        if (folder == _watchedFolder)
        {
            return;
        }

        _statusWatch?.Dispose();
        _statusWatch = null;
        _watchedFolder = folder;
        if (folder is null)
        {
            return;
        }

        try
        {
            _statusWatch = folders.Watch(new WatchedFolder(folder, [MillenniumPlugin.StatusFileName]), _ => Wake());
        }
        // Reports are then read at the next change of anything else.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogStatusNotWatched(exception.GetType().Name);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "The Millennium plugin {Version} was installed")]
    private partial void LogPluginInstalled(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Millennium plugin could not be installed: {Error}")]
    private partial void LogPluginNotInstalled(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin's texts file could not be written: {Error}")]
    private partial void LogTextsNotWritten(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin's reports could not be watched: {Error}")]
    private partial void LogStatusNotWatched(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam's interface could not be brought up to date: {Error}")]
    private partial void LogNotBroughtUpToDate(string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam did not exit in time, so it was not started again")]
    private partial void LogSteamDidNotExit();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam could not be started again")]
    private partial void LogSteamNotStarted();

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of Steam's interface status failed")]
    private partial void LogListenerFailed(Exception exception);

    /// <summary>What the plugin reported last.</summary>
    /// <param name="Hooks">What the hook could wrap; null before the first report.</param>
    /// <param name="Applied">The last change of launch options it made.</param>
    /// <param name="LaunchOptions">The player's launch options by app id.</param>
    /// <param name="Millennium">The version of Millennium that loaded it.</param>
    private sealed record PluginReport(SteamUiHooks? Hooks, long Applied, ImmutableDictionary<string, string> LaunchOptions, string Millennium)
    {
        public static PluginReport None { get; } = new(null, 0, ImmutableDictionary<string, string>.Empty, "");

        public static PluginReport Read(JsonElement report) => new(
            report.GetOrNull("hooks", JsonValueKind.Object) is { } hooks
                ? new SteamUiHooks(hooks.GetBooleanOrNull("achievements") ?? false, hooks.GetBooleanOrNull("notifications") ?? false)
                : null,
            report.GetInt64OrNull("applied") ?? 0,
            report.GetOrNull("launchOptions", JsonValueKind.Object)?.EnumerateObject()
                .Where(option => option.Value.ValueKind == JsonValueKind.String)
                .ToImmutableDictionary(option => option.Name, option => option.Value.GetString()!, StringComparer.Ordinal)
                ?? ImmutableDictionary<string, string>.Empty,
            report.GetTextOrNull("millennium") ?? "");
    }
}
