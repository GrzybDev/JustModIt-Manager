using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Localization;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>A run of Steam as the app follows it.</summary>
/// <param name="StartedAt">When its process started; null when the system does not tell.</param>
/// <param name="SeenAt">When the app first saw it run.</param>
internal sealed record SteamRun(DateTimeOffset? StartedAt, DateTimeOffset SeenAt);

/// <summary>
/// The way into Steam's interface through the app's Millennium plugin: it installs the plugin while Steam is closed and
/// takes it away once nothing needs it, writes it the achievement texts to show and the launch options to change, and reads
/// back what it reports of the run of Steam it loaded in.
/// </summary>
/// <param name="folders">Reports the plugin's reports.</param>
/// <param name="options">The app's name and version, which name the plugin and fill its panel.</param>
/// <param name="time">The clock of the wait for the plugin's first report.</param>
/// <param name="changed">Called when the plugin reported or stayed silent too long.</param>
/// <param name="logger">Receives what could not be done.</param>
internal sealed partial class MillenniumDelivery(IFolderWatcher folders, AgentOptions options, TimeProvider time, Action changed,
    ILogger logger) : IDisposable
{
    // Millennium loads its plugins as Steam's interface starts, which a slow sign-in can hold back.
    private static readonly TimeSpan ReportTimeLimit = TimeSpan.FromMinutes(2);

    private IReadOnlyDictionary<string, byte[]>? _pluginFiles;
    private MillenniumPlugin? _plugin;
    private PluginReport _report = PluginReport.None;
    private List<PendingLaunchOptions>? _pending;
    private long _lastChange;
    private byte[]? _writtenTexts;
    private string? _watchedFolder;
    private IDisposable? _statusWatch;
    private ITimer? _reportWait;

    private IReadOnlyDictionary<string, byte[]> PluginFiles => _pluginFiles ??= MillenniumPlugin.Files(options.Identity.Slug,
        options.Version, Text.Format(Strings.SteamPlugin_Name, ("product", options.ProductName)),
        Text.Format(Strings.SteamPlugin_Description, ("product", options.ProductName)));

    /// <summary>Brings the plugin and its texts file up to date and tells how the plugin stands.</summary>
    /// <param name="millennium">Where Millennium keeps its plugins and its settings.</param>
    /// <param name="run">The run of Steam now; null while Steam is closed.</param>
    /// <param name="needed">Something needs Steam's interface, so the plugin is worth installing.</param>
    /// <param name="unused">Nothing can need it until the player turns something on, so it goes.</param>
    /// <param name="demand">What Steam's interface is to get; null before the first, which leaves the plugin as it is.</param>
    /// <param name="status">The status so far, which the plugin's adds to.</param>
    /// <param name="cancellationToken">Stops the writes.</param>
    public async Task<SteamUiStatus> ReconcileAsync(MillenniumFolders millennium, SteamRun? run, bool needed, bool unused, SteamUiDemand? demand,
        SteamUiStatus status, CancellationToken cancellationToken)
    {
        var plugin = Plugin(millennium);
        // Millennium keeps its list of plugins in memory while Steam runs and writes it back, so both change only while Steam is closed.
        if (run is null && demand is not null)
        {
            await ChangePluginAsync(plugin, needed, unused, cancellationToken).ConfigureAwait(false);
        }

        var installed = plugin.InstalledVersion();
        var enabled = installed is not null && plugin.IsEnabled();
        WatchStatus(installed is null ? null : plugin);
        ReadReport(plugin);
        if (demand is not null && installed is not null)
        {
            await WriteTextsAsync(plugin, demand, cancellationToken).ConfigureAwait(false);
        }

        // A report from an earlier run of Steam tells nothing of this one, in which Millennium may not have loaded the plugin.
        var loaded = run is not null && enabled ? run : null;
        var reported = loaded is not null && IsReportOf(plugin, loaded);
        WaitForReport(reported ? null : loaded);
        return status with
        {
            Delivery = SteamUiDelivery.Millennium,
            Reachable = reported,
            RestartNeeded = run is not null && needed && installed is null,
            PluginDisabled = installed is not null && !enabled,
            Hooks = reported ? _report.Hooks : loaded is not null && HasWaitedTooLong(loaded) ? new SteamUiHooks(false, false) : null,
            LaunchOptions = LaunchOptionsOf(demand?.LaunchOptionsGames ?? []),
            MillenniumVersion = _report.Millennium,
            PluginVersion = installed ?? "",
        };
    }

    /// <summary>Queues a change of a game's launch options for the plugin, which makes it the next time it reads its texts.</summary>
    /// <param name="status">What the app knows of Steam's interface now.</param>
    /// <param name="demand">What Steam's interface is to get, written with the change.</param>
    /// <param name="game">The Steam game.</param>
    /// <param name="add">The options to add where they are missing.</param>
    /// <param name="remove">The options to take out where they are present.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>The game's options as the plugin leaves them, or why it cannot make the change.</returns>
    public async Task<LaunchOptionsResult> ChangeLaunchOptionsAsync(SteamUiStatus status, SteamUiDemand demand, GameKey game,
        IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken cancellationToken)
    {
        if (!status.Reachable || _plugin is not { } plugin || _pending is not { } pending)
        {
            return new LaunchOptionsResult(null, LaunchOptionsFailure.Unreachable);
        }

        // The plugin makes the change the next time it reads its texts file, at the latest before a game starts.
        pending.Add(new PendingLaunchOptions(++_lastChange, game, [.. add], [.. remove]));
        if (!await WriteTextsAsync(plugin, demand, cancellationToken).ConfigureAwait(false))
        {
            pending.RemoveAt(pending.Count - 1);
            return new LaunchOptionsResult(null, LaunchOptionsFailure.Failed);
        }

        return new LaunchOptionsResult(SteamLaunchOptions.Changed(status.LaunchOptions.GetValueOrDefault(game, ""), add, remove));
    }

    /// <summary>Stops following the plugin's reports, as when Millennium is gone.</summary>
    public void Stop()
    {
        WatchStatus(null);
        WaitForReport(null);
    }

    public void Dispose() => Stop();

    private async Task ChangePluginAsync(MillenniumPlugin plugin, bool needed, bool unused, CancellationToken cancellationToken)
    {
        try
        {
            // A plugin the player turned off in Millennium is the player's to remove.
            if (unused && plugin.InstalledVersion() is not null && plugin.IsEnabled())
            {
                await plugin.RemoveAsync(cancellationToken).ConfigureAwait(false);
                LogPluginRemoved();
            }
            else if (!unused && (needed || plugin.InstalledVersion() is not null) && !plugin.IsCurrent(PluginFiles))
            {
                await plugin.InstallAsync(PluginFiles, cancellationToken).ConfigureAwait(false);
                LogPluginInstalled(options.Version);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogPluginNotChanged(exception.GetType().Name);
        }
    }

    private MillenniumPlugin Plugin(MillenniumFolders millennium)
    {
        var plugin = new MillenniumPlugin(millennium, options.Identity.Slug);
        if (_plugin?.Directory != plugin.Directory)
        {
            _plugin = plugin;
            _report = PluginReport.None;
            _pending = null;
            _writtenTexts = null;
        }

        return _plugin;
    }

    // The plugin reports as soon as it loads, so a report written since Steam started is of this run.
    private bool IsReportOf(MillenniumPlugin plugin, SteamRun run) => run.StartedAt is { } started
        ? File.GetLastWriteTimeUtc(Path.Join(plugin.Directory, MillenniumPlugin.StatusFileName)) >= started.UtcDateTime
        : _report != PluginReport.None;

    private bool HasWaitedTooLong(SteamRun run) => time.GetUtcNow() >= run.SeenAt + ReportTimeLimit;

    // Looks again once the plugin had its time to report, so a plugin Millennium did not load is told as blocked.
    private void WaitForReport(SteamRun? run)
    {
        _reportWait?.Dispose();
        _reportWait = null;
        if (run is not null && !HasWaitedTooLong(run))
        {
            _reportWait = time.CreateTimer(_ => changed(), null, run.SeenAt + ReportTimeLimit - time.GetUtcNow(), Timeout.InfiniteTimeSpan);
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

    // The player's options as the plugin reported them, with the changes it has not made yet; a game it never reported is
    // not known, so the app never takes an option the player has for one it added.
    private ValueDictionary<GameKey, string> LaunchOptionsOf(ValueList<GameKey> games)
    {
        var result = ImmutableDictionary<GameKey, string>.Empty;
        foreach (var game in games)
        {
            if (_report.LaunchOptions.GetValueOrDefault(game.StoreGameId) is { } reported)
            {
                result = result.Add(game, (_pending ?? []).Where(change => change.Game == game)
                    .Aggregate(reported, (current, change) => SteamLaunchOptions.Changed(current, change.Add, change.Remove)));
            }
        }

        return new ValueDictionary<GameKey, string>(result);
    }

    private void WatchStatus(MillenniumPlugin? plugin)
    {
        var folder = plugin?.Directory;
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
            _statusWatch = folders.Watch(new WatchedFolder(folder, [MillenniumPlugin.StatusFileName]), _ => changed());
        }
        // Reports are then read at the next change of anything else.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogStatusNotWatched(exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Millennium plugin {Version} was installed")]
    private partial void LogPluginInstalled(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Millennium plugin was removed, since nothing needs it")]
    private partial void LogPluginRemoved();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Millennium plugin could not be installed or removed: {Error}")]
    private partial void LogPluginNotChanged(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin's texts file could not be written: {Error}")]
    private partial void LogTextsNotWritten(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin's reports could not be watched: {Error}")]
    private partial void LogStatusNotWatched(string error);

    /// <summary>What the plugin reported last.</summary>
    /// <param name="Hooks">What the hook could wrap; null before the first report.</param>
    /// <param name="Applied">The last change of launch options it made.</param>
    /// <param name="LaunchOptions">The player's launch options by app id.</param>
    /// <param name="Millennium">The version of Millennium that loaded it.</param>
    private sealed record PluginReport(SteamUiHooks? Hooks, long Applied, ImmutableDictionary<string, string> LaunchOptions, string Millennium)
    {
        public static PluginReport None { get; } = new(null, 0, ImmutableDictionary<string, string>.Empty, "");

        public static PluginReport Read(JsonElement report) => new(
            ScriptReports.Hooks(report.GetOrNull("hooks", JsonValueKind.Object)),
            report.GetInt64OrNull("applied") ?? 0,
            ScriptReports.LaunchOptions(report.GetOrNull("launchOptions", JsonValueKind.Object)),
            report.GetTextOrNull("millennium") ?? "");
    }
}
