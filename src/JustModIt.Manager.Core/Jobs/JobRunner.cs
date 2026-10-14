using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>
/// Runs jobs in the background: one per game, one download at a time, every change to game files on the install engine's
/// single queue and none before crash recovery, with snapshots and results raised as events, never under a lock.
/// </summary>
public sealed partial class JobRunner : IAsyncDisposable
{
    private readonly IInstallEngine _engine;
    private readonly IPackageDownloader _downloader;
    private readonly ReplacementMarkers _markers;
    private readonly TimeProvider _time;
    private readonly ILogger<JobRunner> _logger;
    private readonly JobPipeline _pipeline;
    private readonly Lock _gate = new();
    private readonly Dictionary<GameKey, JobContext> _active = [];
    private readonly SemaphoreSlim _downloadSlot = new(1, 1);
    private readonly CancellationTokenSource _exiting = new();

    // Only an exit that does not wait for the player's jobs stops putting a previous release back.
    private readonly CancellationTokenSource _halting = new();
    private readonly TaskCompletionSource<IReadOnlyList<RecoveredOperation>> _recovered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly HashSet<Guid> _recentlyFinished = [];
    private TaskCompletionSource _idle = CompletedSignal();
    private bool _shutDown;

    /// <summary>Creates the runner, whose jobs wait for <see cref="RecoverAsync"/>.</summary>
    /// <param name="engine">Changes game files on its single queue and recovers what a crash interrupted.</param>
    /// <param name="catalogue">Reads the release to install fresh from the site.</param>
    /// <param name="downloader">Downloads, keeps and prunes packages.</param>
    /// <param name="library">Gives a fresh look at a game before any write.</param>
    /// <param name="runningGames">Waits for a game's processes to exit.</param>
    /// <param name="settings">Where new installs keep their backups.</param>
    /// <param name="markers">Keeps the replacements under way, so a start after a stop puts back what they removed.</param>
    /// <param name="time">The clock for waits and progress.</param>
    /// <param name="logger">Logs every job and what went wrong.</param>
    /// <param name="installPolicy">The app's rule for installs of several projects on one game; by default they share it.</param>
    public JobRunner(IInstallEngine engine, ICatalogue catalogue, IPackageDownloader downloader, ILibraryService library,
        IRunningGameDetector runningGames, ISettingsService settings, ReplacementMarkers markers, TimeProvider time,
        ILogger<JobRunner> logger, IInstallPolicy? installPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(runningGames);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _downloader = downloader;
        _markers = markers;
        _time = time;
        _logger = logger;
        InstallPolicy = installPolicy ?? SideBySideInstallPolicy.Instance;
        _pipeline = new JobPipeline(engine, catalogue, downloader, library, runningGames, settings, InstallPolicy, markers, _downloadSlot,
            time, logger, _halting.Token);
    }

    public IInstallPolicy InstallPolicy { get; }

    /// <summary>A job started, or changed its stage or what it waits for.</summary>
    public event EventHandler<JobSnapshot>? Changed;

    /// <summary>A job's progress moved, at most about ten times a second per job.</summary>
    public event EventHandler<JobSnapshot>? Progressed;

    /// <summary>A job finished; it is no longer among <see cref="Active"/>.</summary>
    public event EventHandler<JobResult>? Finished;

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _active.Count > 0;
            }
        }
    }

    public IReadOnlyList<JobSnapshot> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _active.Values.Select(job => job.Snapshot)];
            }
        }
    }

    /// <summary>What recovery did at the start; completes once it has run, even when it failed.</summary>
    public Task<IReadOnlyList<RecoveredOperation>> Recovery => _recovered.Task;

    /// <summary>The game's job; null when it has none.</summary>
    /// <param name="game">The game's store and id.</param>
    public JobSnapshot? Find(GameKey game)
    {
        lock (_gate)
        {
            return _active.TryGetValue(game, out var job) ? job.Snapshot : null;
        }
    }

    /// <summary>
    /// Finishes or undoes what a crash interrupted, and starts putting back the installs that replacements removed before a
    /// stop; jobs wait for it, so it is called once at the start before anything else looks at the installs.
    /// </summary>
    /// <param name="cancellationToken">Stops recovery, which then counts as done.</param>
    public async Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            var recovered = await _engine.RecoverAsync(cancellationToken).ConfigureAwait(false);
            await StartRestoresAsync(cancellationToken).ConfigureAwait(false);
            _recovered.TrySetResult(recovered);
            return recovered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _recovered.TrySetResult([]);
            throw;
        }
        // The engine keeps what it could not undo and refuses to touch those games, so the others can go on.
        catch (Exception exception)
        {
            LogRecoveryFailed(exception);
            _recovered.TrySetResult([]);
            return [];
        }
    }

    /// <summary>Starts a job in the background and returns at once.</summary>
    /// <param name="request">What the job is to do.</param>
    /// <returns>
    /// The job; a refused one (<see cref="JobHandle.IsRefused"/>) when the game has a job
    /// (<see cref="JobFailureReason.AlreadyRunning"/>) or the runner is shut down (cancelled).
    /// </returns>
    public JobHandle Start(JobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JobContext job;
        lock (_gate)
        {
            if (_shutDown || _active.ContainsKey(request.Game))
            {
                var refused = new JobContext(request, new CancellationTokenSource(), (_, _) => { }, _time);
                var result = _shutDown
                    ? refused.Result(JobOutcome.Cancelled)
                    : refused.Result(JobOutcome.Failed, new JobFailure(JobFailureReason.AlreadyRunning));
                refused.DisposeCancellation();
                return new JobHandle(refused.Snapshot, Task.FromResult(result)) { IsRefused = true };
            }

            // The app's exit cancels automatic jobs; the player's jobs finish unless the player cancels them.
            var cancellation = request.Origin == JobOrigin.Automatic
                ? CancellationTokenSource.CreateLinkedTokenSource(_exiting.Token)
                : new CancellationTokenSource();
            job = new JobContext(request, cancellation, Publish, _time);
            if (_active.Count == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _active[request.Game] = job;
            job.Execution = Task.Run(() => RunAsync(job));
        }

        return new JobHandle(job.Snapshot, job.Completion.Task);
    }

    /// <summary>Cancels a job for the player; an update past its uninstall still puts the previous release back.</summary>
    /// <param name="jobId">The <see cref="JobSnapshot.Id"/> of the job.</param>
    /// <returns>False when the job is not running, e.g. because it finished meanwhile.</returns>
    public bool Cancel(Guid jobId)
    {
        JobContext? job;
        lock (_gate)
        {
            job = _active.Values.FirstOrDefault(active => active.Snapshot.Id == jobId);
        }

        job?.Cancel(byPlayer: true);
        return job is not null;
    }

    /// <summary>Completes once no job runs.</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    public Task WhenIdleAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _idle.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Refuses new jobs and cancels the automatic ones, as the app exits; the player's jobs, and putting back a release a
    /// cancelled update removed, go on unless <paramref name="cancelPlayerJobs"/>.
    /// </summary>
    /// <param name="cancelPlayerJobs">Whether the exit does not wait for the player's jobs.</param>
    public void Shutdown(bool cancelPlayerJobs)
    {
        List<JobContext> jobs;
        lock (_gate)
        {
            _shutDown = true;
            jobs = [.. _active.Values];
        }

        _exiting.Cancel();
        if (cancelPlayerJobs)
        {
            _halting.Cancel();
            foreach (var job in jobs)
            {
                job.Cancel(byPlayer: false);
            }
        }
    }

    /// <summary>
    /// Deletes downloaded packages, only while no job runs, since a job may be about to use a package it has not claimed
    /// yet.
    /// </summary>
    /// <param name="keep">
    /// The release file ids of the packages to keep, besides those of jobs that finished since the last prune, whose retry
    /// the caller may not know of yet.
    /// </param>
    /// <param name="cancellationToken">Stops deleting.</param>
    /// <returns>How many files were deleted; none while a job runs.</returns>
    public Task<int> PruneDownloadsAsync(IReadOnlySet<Guid> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        HashSet<Guid> kept;
        lock (_gate)
        {
            if (_active.Count > 0)
            {
                return Task.FromResult(0);
            }

            kept = [.. keep, .. _recentlyFinished];
            _recentlyFinished.Clear();
        }

        return _downloader.PruneAsync(kept, keepPartial: true, cancellationToken);
    }

    /// <summary>
    /// Deletes every downloaded package but the kept ones, partial downloads too, as the player asked; only while no job
    /// runs, since a job may be about to use a package it has not claimed yet.
    /// </summary>
    /// <param name="keep">The release file ids of the packages that installs need.</param>
    /// <param name="cancellationToken">Stops deleting.</param>
    /// <returns>False when a job runs, and nothing was deleted.</returns>
    public async Task<bool> ClearDownloadsAsync(IReadOnlySet<Guid> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        HashSet<Guid> kept;
        lock (_gate)
        {
            if (_active.Count > 0)
            {
                return false;
            }

            // A job that finished since may have installed a package the caller's list does not know of yet.
            kept = [.. keep, .. _recentlyFinished];
        }

        await _downloader.PruneAsync(kept, keepPartial: false, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        Shutdown(cancelPlayerJobs: true);
        List<Task> running;
        lock (_gate)
        {
            running = [.. _active.Values.Select(job => job.Execution)];
        }

        await Task.WhenAll(running).ConfigureAwait(false);
        _exiting.Dispose();
        _halting.Dispose();
        _downloadSlot.Dispose();
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private async Task RunAsync(JobContext job)
    {
        JobResult result;
        try
        {
            job.PublishQueued();
            LogStarted(job.Kind, job.Game, job.Origin);
            // Recovery itself starts the jobs that put back what a stop left removed, once the engine has recovered.
            if (job.Request is not RestoreJobRequest && !_recovered.Task.IsCompleted)
            {
                job.Wait(JobWaitReason.Recovery, []);
                await _recovered.Task.WaitAsync(job.Token).ConfigureAwait(false);
                job.Resume();
            }

            result = await _pipeline.RunAsync(job).ConfigureAwait(false);
        }
        catch (JobDeferredException deferred)
        {
            result = job.Result(JobOutcome.Deferred) with { DeferredBy = deferred.Reason, RunningProcesses = deferred.Processes };
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            result = job.Result(JobOutcome.Cancelled);
        }
        catch (Exception exception)
        {
            var failure = JobFailures.From(exception);
            if (failure.Reason == JobFailureReason.Unexpected)
            {
                LogUnexpected(job.Kind, job.Game, exception);
            }

            result = job.Result(JobOutcome.Failed, failure);
        }

        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            _active.Remove(job.Game);
            _recentlyFinished.UnionWith(job.ProtectedFiles);
            job.DisposeCancellation();
            if (_active.Count == 0)
            {
                idle = _idle;
            }
        }

        LogFinished(job.Kind, job.Game, result.Outcome, result.Failure?.Reason);
        EventRaising.RaiseEach(Finished, this, result, LogListenerFailed);
        job.Completion.TrySetResult(result);
        idle?.TrySetResult();
    }

    // Started before recovery counts as done, so neither the player nor the automation gets a job onto the game first; a
    // replacement whose new install committed, or whose uninstall was undone, only loses its marker.
    private async Task StartRestoresAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplacementMarker> markers;
        IReadOnlyList<InstallRecord> records;
        try
        {
            markers = await _markers.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            if (markers.Count == 0)
            {
                return;
            }

            records = await _engine.LoadRecordsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogRestoresFailed(exception);
            return;
        }

        foreach (var marker in markers)
        {
            if (records.Any(marker.IsSettledBy))
            {
                _markers.Remove(marker.Previous.Id);
            }
            else
            {
                LogRestoring(marker.Previous.Game);
                Start(new RestoreJobRequest(marker));
            }
        }
    }

    private void Publish(JobSnapshot snapshot, bool stepChanged) =>
        EventRaising.RaiseEach(stepChanged ? Changed : Progressed, this, snapshot, LogListenerFailed);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} of {Game} started ({Origin})")]
    private partial void LogStarted(JobKind kind, GameKey game, JobOrigin origin);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} of {Game} finished: {Outcome} {Reason}")]
    private partial void LogFinished(JobKind kind, GameKey game, JobOutcome outcome, JobFailureReason? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Kind} of {Game} failed unexpectedly")]
    private partial void LogUnexpected(JobKind kind, GameKey game, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recovering interrupted operations failed")]
    private partial void LogRecoveryFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app stopped while replacing an install on {Game}, which is put back")]
    private partial void LogRestoring(GameKey game);

    [LoggerMessage(Level = LogLevel.Error, Message = "The replacements a stop cut short could not be read")]
    private partial void LogRestoresFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A job listener failed")]
    private partial void LogListenerFailed(Exception exception);
}
