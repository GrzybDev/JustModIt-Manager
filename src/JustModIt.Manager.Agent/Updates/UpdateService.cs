using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Updates;

/// <summary>When the app looks for its own updates.</summary>
/// <param name="FirstCheck">How long after the start the first check waits.</param>
/// <param name="Interval">How long between checks.</param>
/// <param name="Retry">After a check that failed, sooner than the usual interval.</param>
public sealed record UpdateSchedule(Func<LaunchArguments, TimeSpan> FirstCheck, TimeSpan Interval, TimeSpan Retry)
{
    public static UpdateSchedule Default { get; } = new(StartupDelays.UpdateCheck, TimeSpan.FromHours(4), TimeSpan.FromHours(1));
}

/// <summary>
/// Downloads the app's own updates in the background and restarts into one only when nobody would notice (no window
/// open and no job running), or at once when the player asks and the window agreed to close.
/// </summary>
/// <param name="updater">The app's feed and Velopack.</param>
/// <param name="schedule">When to look for updates.</param>
/// <param name="status">Shows how the update stands.</param>
/// <param name="settings">Whether updates download and apply on their own.</param>
/// <param name="jobs">The running jobs, which an update waits for.</param>
/// <param name="ui">The window, which an update waits for or closes.</param>
/// <param name="lifetime">Exits the agent for the update.</param>
/// <param name="links">Writes the start's command line back for the restart.</param>
/// <param name="options">Whether the updater started this process.</param>
/// <param name="time">Times the checks.</param>
/// <param name="logger">Receives checks, downloads and failures.</param>
public sealed partial class UpdateService(
    IAppUpdater updater,
    UpdateSchedule schedule,
    AgentStatus status,
    ISettingsService settings,
    JobRunner jobs,
    UiLauncher ui,
    AgentLifetime lifetime,
    AppLinks links,
    AgentOptions options,
    TimeProvider time,
    ILogger<UpdateService> logger) : IAgentService, IDisposable
{
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private int _applying;
    private int _disposed;
    private volatile bool _restartRequested;

    // Applying this version failed once, so it is applied again only when the player asks.
    private volatile string? _failedVersion;

    /// <summary>
    /// Applies an update downloaded by an earlier run before this one starts anything, restarting with the same command
    /// line, since nothing needs the app yet.
    /// </summary>
    /// <param name="start">The command line this process started with.</param>
    /// <returns>Whether the app now exits to be updated.</returns>
    public bool ApplyPending(LaunchArguments start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (Pending() is not { } pending)
        {
            return false;
        }

        // The updater starts the old version again when applying fails, so applying at every start would never end.
        if (options.RestartedByUpdater)
        {
            LogPendingAfterRestart(pending.Version);
            _failedVersion = pending.Version;
            return false;
        }

        LogApplyingPending(pending.Version);
        return Apply(start.ToArgs(links));
    }

    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        if (!updater.IsSupported)
        {
            status.ChangeUpdate(update => update with { State = AppUpdateState.Unavailable });
            return;
        }

        var pending = Pending();
        // A check the player asked for may already have run, since the tray and the pipe work before this starts.
        status.ChangeUpdate(update => update.State != AppUpdateState.Unavailable ? update
            : pending is null ? update with { State = AppUpdateState.Idle }
            : update with { State = AppUpdateState.Ready, Version = pending.Version, ReleaseNotes = pending.Notes });
        jobs.Finished += OnJobFinished;
        ui.Closed += OnWindowClosed;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        try
        {
            await Task.Delay(schedule.FirstCheck(start), time, stopping.Token).ConfigureAwait(false);
            while (true)
            {
                var checkedFine = await CheckAsync(userAsked: false).ConfigureAwait(false);
                await Task.Delay(checkedFine ? schedule.Interval : schedule.Retry, time, stopping.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            ui.Closed -= OnWindowClosed;
            jobs.Finished -= OnJobFinished;
        }
    }

    /// <summary>
    /// Looks for an update at the player's request, downloading what it finds even with automatic updates off.
    /// </summary>
    public Task CheckNowAsync() => updater.IsSupported ? CheckAsync(userAsked: true) : Task.CompletedTask;

    /// <summary>Restarts into the downloaded update at the player's request, once running jobs are done.</summary>
    /// <returns>False when there is nothing to apply, or the window refused to close.</returns>
    public async Task<bool> RestartToUpdateAsync()
    {
        if (status.Update.State != AppUpdateState.Ready)
        {
            return false;
        }

        _restartRequested = true;
        if (jobs.IsBusy)
        {
            return true;
        }

        if (!await ui.CloseAsync(ShutdownReason.Update, _stopping.Token).ConfigureAwait(false))
        {
            _restartRequested = false;
            return false;
        }

        // The player is waiting for the app, so it comes back with its window.
        return Apply([]);
    }

    // The container disposes it once for each service it is registered as.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopping.Cancel();
        _stopping.Dispose();
        _checking.Dispose();
    }

    // False when the check failed and should be tried again sooner.
    private async Task<bool> CheckAsync(bool userAsked)
    {
        // A scheduled check gives way to one that runs; the player's waits for it, since only it downloads what it finds.
        try
        {
            if (!await _checking.WaitAsync(userAsked ? Timeout.InfiniteTimeSpan : TimeSpan.Zero, _stopping.Token).ConfigureAwait(false))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return true;
        }

        try
        {
            if (status.Update.State == AppUpdateState.Ready)
            {
                return true;
            }

            status.ChangeUpdate(update => update with { State = AppUpdateState.Checking });
            var release = await updater.CheckAsync(_stopping.Token).ConfigureAwait(false);
            var now = time.GetUtcNow();
            if (release is null)
            {
                status.ChangeUpdate(update => update with
                {
                    State = AppUpdateState.UpToDate,
                    Version = null,
                    ReleaseNotes = null,
                    CheckedAt = now,
                });
                return true;
            }

            if (!userAsked && !settings.Current.AutoUpdateApp)
            {
                status.ChangeUpdate(update => update with
                {
                    State = AppUpdateState.Available,
                    Version = release.Version,
                    ReleaseNotes = release.Notes,
                    CheckedAt = now,
                });
                return true;
            }

            status.ChangeUpdate(update => update with
            {
                State = AppUpdateState.Downloading,
                Version = release.Version,
                ReleaseNotes = release.Notes,
                Progress = 0,
                CheckedAt = now,
            });
            await updater.DownloadAsync(percent => status.ChangeUpdate(update => update with { Progress = percent }), _stopping.Token)
                .ConfigureAwait(false);
            status.ChangeUpdate(update => update with { State = AppUpdateState.Ready, Progress = null });
            LogDownloaded(release.Version);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception exception)
        {
            LogCheckFailed(exception);
            status.ChangeUpdate(update => update with { State = AppUpdateState.Failed, Progress = null });
            return false;
        }
        finally
        {
            _checking.Release();
        }

        ApplyWhenUnnoticed();
        return true;
    }

    private void OnJobFinished(object? sender, JobResult result) => ApplyWhenUnnoticed();

    private void OnWindowClosed(object? sender, EventArgs e) => ApplyWhenUnnoticed();

    private void ApplyWhenUnnoticed()
    {
        if (status.Update.State != AppUpdateState.Ready || jobs.IsBusy)
        {
            return;
        }

        if (_restartRequested)
        {
            _ = RestartToUpdateAsync();
        }
        else if (settings.Current.AutoUpdateApp && !ui.IsOpenOrStarting && status.Update.Version != _failedVersion)
        {
            Apply([LaunchArguments.BackgroundSwitch]);
        }
    }

    // Null also when the updater cannot tell, since a broken updater must not keep the app from starting.
    private AppRelease? Pending()
    {
        try
        {
            return updater.IsSupported ? updater.Pending : null;
        }
        catch (Exception exception)
        {
            LogPendingUnreadable(exception);
            return null;
        }
    }

    private bool Apply(IReadOnlyList<string> restartArgs)
    {
        if (Interlocked.Exchange(ref _applying, 1) != 0)
        {
            return false;
        }

        try
        {
            updater.ApplyAfterExit(restartArgs);
        }
        // An update that cannot be applied must not take the app down with it.
        catch (Exception exception)
        {
            LogApplyFailed(exception);
            Volatile.Write(ref _applying, 0);
            _restartRequested = false;
            status.ChangeUpdate(update => update with { State = AppUpdateState.Failed });
            return false;
        }

        lifetime.ExitForUpdate();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying version {Version}, downloaded by an earlier run")]
    private partial void LogApplyingPending(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Version {Version} is still pending after the updater restarted the app, so applying it failed")]
    private partial void LogPendingAfterRestart(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The downloaded updates could not be read")]
    private partial void LogPendingUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {Version} is downloaded")]
    private partial void LogDownloaded(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking for an update of the app failed")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The update could not be applied")]
    private partial void LogApplyFailed(Exception exception);
}
