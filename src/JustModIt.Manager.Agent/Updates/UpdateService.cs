using System.Text;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
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
/// <param name="options">The running version, and where the record of the last update attempt lies.</param>
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

    // The window asked to stay, so a restart that waits for the jobs keeps it too.
    private volatile bool _keepWindow;

    // Applying this version failed once, so it is applied again only when the player asks.
    private volatile string? _failedVersion;

    /// <summary>
    /// Applies an update downloaded by an earlier run before this one starts anything, restarting as a launch with the same
    /// command line otherwise, since nothing needs the app yet.
    /// </summary>
    /// <param name="start">The command line this process started with.</param>
    /// <returns>Whether the app now exits to be updated.</returns>
    public bool ApplyPending(LaunchArguments start)
    {
        var attempted = UpdateAttempt.Read(options.Locations);
        if (attempted == options.Version)
        {
            AtomicFile.TryDelete(options.Locations.UpdateAttemptFile);
        }

        if (Pending() is not { } pending)
        {
            return false;
        }

        // The updater starts the old version again when applying fails, and on Linux the agent's unit does, so applying at
        // every start would never end; a version the last run tried waits for the player.
        if (pending.Version == attempted)
        {
            LogPendingAfterAttempt(pending.Version);
            _failedVersion = pending.Version;
            return false;
        }

        // A launch that becomes the window takes the window's lock before it starts the agent, and the window then waits for
        // the update, which applies once it closes or the player asks.
        if (InstanceLock.IsHeld(options.Names.UiLock))
        {
            LogPendingWhileWindow(pending.Version);
            return false;
        }

        LogApplyingPending(pending.Version);
        // A launch rather than the agent, since on Linux it starts the agent's unit, and an agent the updater started would run
        // outside it.
        return Apply((start with { Role = ProcessRole.Launcher }).ToArgs(links));
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
    /// <param name="keepWindow">
    /// Leaves the window open, which starts anew in the new version itself; otherwise the window closes first.
    /// </param>
    /// <returns>False when there is nothing to apply, or the window refused to close.</returns>
    public async Task<bool> RestartToUpdateAsync(bool keepWindow = false)
    {
        if (status.Update.State != AppUpdateState.Ready)
        {
            return false;
        }

        _restartRequested = true;
        _keepWindow |= keepWindow;
        if (jobs.IsBusy)
        {
            return true;
        }

        if (!_keepWindow && !await ui.CloseAsync(ShutdownReason.Update, _stopping.Token).ConfigureAwait(false))
        {
            _restartRequested = false;
            return false;
        }

        // The player is waiting for the app, so it comes back with its window, unless the window stays, or the store's game
        // session shows only a window the store starts.
        return Apply(_keepWindow || status.Activity.WindowReturn == WindowReturn.StoreLibrary ? [LaunchArguments.BackgroundSwitch] : []);
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

    private void OnWindowClosed(object? sender, EventArgs e) => ApplyWhenUnnoticed(windowClosed: true);

    // A launch that becomes the window holds the window's lock before it connects, so a held lock is a window on its way,
    // except right after a window closed, whose process holds its lock until it ends.
    private void ApplyWhenUnnoticed(bool windowClosed = false)
    {
        // An exit cancels or waits for the jobs, and the last one's end must not restart the app; the next start applies it.
        if (lifetime.IsExiting || status.Update.State != AppUpdateState.Ready || jobs.IsBusy)
        {
            return;
        }

        if (_restartRequested)
        {
            _ = RestartToUpdateAsync();
        }
        else if (settings.Current.AutoUpdateApp && !ui.IsOpenOrStarting && status.Update.Version != _failedVersion
            && (windowClosed || !InstanceLock.IsHeld(options.Names.UiLock)))
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
            // Written first, so the next start tells an update that failed.
            if (Pending() is { } pending)
            {
                AtomicFile.WriteAllBytesAsync(options.Locations.UpdateAttemptFile, Encoding.UTF8.GetBytes(pending.Version),
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            updater.ApplyAfterExit(restartArgs);
        }
        // An update that cannot be applied must not take the app down with it.
        catch (Exception exception)
        {
            LogApplyFailed(exception);
            // An updater that never started failed to apply nothing, so the next start may try the version again.
            AtomicFile.TryDelete(options.Locations.UpdateAttemptFile);
            Volatile.Write(ref _applying, 0);
            _restartRequested = false;
            _keepWindow = false;
            status.ChangeUpdate(update => update with { State = AppUpdateState.Failed });
            return false;
        }

        LogExitingToApply();
        lifetime.ExitAtOnce();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying version {Version}, downloaded by an earlier run")]
    private partial void LogApplyingPending(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Version {Version} is still pending after the last run started the updater, so applying it failed")]
    private partial void LogPendingAfterAttempt(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {Version} waits, since a window is open or on its way")]
    private partial void LogPendingWhileWindow(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The downloaded updates could not be read")]
    private partial void LogPendingUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {Version} is downloaded")]
    private partial void LogDownloaded(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking for an update of the app failed")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The update could not be applied")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exiting to apply an update")]
    private partial void LogExitingToApply();
}
