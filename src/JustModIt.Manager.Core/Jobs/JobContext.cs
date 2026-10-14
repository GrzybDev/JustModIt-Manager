using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>
/// One running job: its cancellation, the packages it must keep and the snapshots it publishes, built under a lock with
/// a growing sequence number and published outside it, so a listener can drop one that arrives late.
/// </summary>
internal sealed class JobContext
{
    /// <summary>Progress alone is published at most this often, so a busy job cannot flood the window with updates.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly Lock _gate = new();
    private readonly Action<JobSnapshot, bool> _publish;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _cancellation;
    private readonly HashSet<Guid> _protected = [];
    private JobSnapshot _snapshot;
    private JobStage _resumeStage;
    private bool _restoring;
    private long _lastPublished;
    private volatile bool _cancelledByPlayer;

    /// <summary>Creates the job's context, whose first snapshot is not published yet.</summary>
    /// <param name="request">What the job is asked to do.</param>
    /// <param name="cancellation">Cancels the job; <see cref="DisposeCancellation"/> disposes it.</param>
    /// <param name="publish">Receives each snapshot and whether its step changed, never under the lock.</param>
    /// <param name="time">The clock that spaces progress reports.</param>
    public JobContext(JobRequest request, CancellationTokenSource cancellation, Action<JobSnapshot, bool> publish, TimeProvider time)
    {
        Request = request;
        _cancellation = cancellation;
        _publish = publish;
        _time = time;
        _snapshot = new JobSnapshot
        {
            Id = Guid.NewGuid(),
            Game = request.Game,
            GameName = request.GameName,
            Kind = request.Kind,
            Origin = request.Origin,
            InstallId = (request as RecordJobRequest)?.Record.Id,
        };
    }

    public JobRequest Request { get; }

    public GameKey Game => Request.Game;

    public JobKind Kind => Request.Kind;

    public JobOrigin Origin => Request.Origin;

    public CancellationToken Token => _cancellation.Token;

    public bool CancelledByPlayer => _cancelledByPlayer;

    public TaskCompletionSource<JobResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Execution { get; set; } = Task.CompletedTask;

    /// <summary>The packages the job downloaded for what it installs, by the downloads they came from.</summary>
    public ValueList<Guid> PackageFileIds { get; set; }

    public Guid? ReleaseId { get; set; }

    public JobSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public IReadOnlyCollection<Guid> ProtectedFiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _protected];
            }
        }
    }

    public IProgress<DownloadProgress> DownloadProgress =>
        new InlineProgress<DownloadProgress>(progress => Report(progress.BytesReceived, progress.TotalBytes,
            progress.TotalBytes is > 0 and var total ? (double)progress.BytesReceived / total : null, stage: null));

    public IProgress<InstallProgress> InstallProgress => new InlineProgress<InstallProgress>(OnInstallProgress);

    /// <summary>Keeps a package from being pruned while the job may still need it.</summary>
    /// <param name="fileId">The package's release file id.</param>
    public void Protect(Guid fileId)
    {
        lock (_gate)
        {
            _protected.Add(fileId);
        }
    }

    /// <summary>Cancels the job.</summary>
    /// <param name="byPlayer">Whether the player cancelled it, rather than the app's exit.</param>
    public void Cancel(bool byPlayer)
    {
        _cancelledByPlayer |= byPlayer;
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job finished meanwhile.
        }
    }

    /// <summary>Releases the job's cancellation once the job has ended.</summary>
    public void DisposeCancellation() => _cancellation.Dispose();

    /// <summary>Shows the job as waiting for its turn.</summary>
    public void PublishQueued() => Change(snapshot => snapshot with { Stage = JobStage.Queued });

    /// <summary>
    /// Moves the job to a stage; once it restores the previous release it keeps saying so, whichever steps the restore
    /// takes.
    /// </summary>
    /// <param name="stage">The stage the job starts.</param>
    public void Enter(JobStage stage) => Change(snapshot =>
    {
        _restoring |= stage == JobStage.RestoringPrevious;
        return Fresh(snapshot, _restoring ? JobStage.RestoringPrevious : stage);
    });

    /// <summary>Shows that the job waits, until <see cref="Resume"/> or another stage.</summary>
    /// <param name="reason">What the job waits for.</param>
    /// <param name="processes">The programs it waits for to exit, by name; empty for other waits.</param>
    public void Wait(JobWaitReason reason, ValueList<string> processes) => Change(snapshot =>
    {
        if (snapshot.Stage != JobStage.Waiting)
        {
            _resumeStage = snapshot.Stage;
        }

        return Fresh(snapshot, JobStage.Waiting) with { WaitReason = reason, RunningProcesses = processes };
    });

    /// <summary>Goes back to the stage the job waited in, if it is waiting.</summary>
    public void Resume() => Change(snapshot => snapshot.Stage == JobStage.Waiting ? Fresh(snapshot, _resumeStage) : snapshot);

    /// <summary>The result of the job when it succeeded.</summary>
    public JobResult Succeeded() => Result(JobOutcome.Succeeded);

    /// <summary>The job's result, with the release and package it worked with.</summary>
    /// <param name="outcome">How the job ended.</param>
    /// <param name="failure">Why it failed; null otherwise.</param>
    public JobResult Result(JobOutcome outcome, JobFailure? failure = null) => new(Snapshot, outcome)
    {
        Failure = failure,
        CancelledByPlayer = outcome == JobOutcome.Cancelled && CancelledByPlayer,
        PackageFileIds = PackageFileIds,
        ReleaseId = ReleaseId,
    };

    private static JobSnapshot Fresh(JobSnapshot snapshot, JobStage stage) =>
        snapshot with { Stage = stage, WaitReason = null, RunningProcesses = [], Fraction = null, BytesDone = 0, BytesTotal = null };

    private static JobStage StageOf(InstallStage stage) => stage switch
    {
        InstallStage.Checking => JobStage.Checking,
        InstallStage.RollingBack => JobStage.RollingBack,
        InstallStage.Uninstalling => JobStage.Uninstalling,
        InstallStage.Verifying => JobStage.Verifying,
        InstallStage.MovingBackups => JobStage.MovingBackups,
        _ => JobStage.Installing,
    };

    private void OnInstallProgress(InstallProgress progress)
    {
        double? fraction = progress.BytesTotal > 0 ? (double)progress.BytesDone / progress.BytesTotal
            : progress.FileCount > 0 ? (double)progress.FilesDone / progress.FileCount
            : null;
        Report(progress.BytesDone, progress.BytesTotal > 0 ? progress.BytesTotal : null, fraction, StageOf(progress.Stage));
    }

    // A restore keeps showing that it restores whatever stage the engine reports.
    private void Report(long bytesDone, long? bytesTotal, double? fraction, JobStage? stage)
    {
        JobSnapshot published;
        bool stepChanged;
        lock (_gate)
        {
            var current = _snapshot;
            var next = stage is { } engineStage && engineStage != current.Stage && !_restoring && current.Stage != JobStage.Waiting
                ? Fresh(current, engineStage)
                : current;
            stepChanged = !next.HasSameStep(current);
            var now = _time.GetTimestamp();
            var clamped = fraction is { } value ? Math.Clamp(value, 0, 1) : (double?)null;
            next = next with { Fraction = clamped, BytesDone = bytesDone, BytesTotal = bytesTotal };
            var due = stepChanged || (clamped >= 1 && current.Fraction < 1) || (clamped is null) != (current.Fraction is null)
                || _time.GetElapsedTime(_lastPublished, now) >= ProgressInterval;
            if (!due || next == current)
            {
                return;
            }

            _lastPublished = now;
            _snapshot = published = next with { Sequence = current.Sequence + 1 };
        }

        _publish(published, stepChanged);
    }

    private void Change(Func<JobSnapshot, JobSnapshot> change)
    {
        JobSnapshot published;
        lock (_gate)
        {
            var current = _snapshot;
            var next = change(current);
            if (next == current && current.Sequence > 0)
            {
                return;
            }

            _lastPublished = _time.GetTimestamp();
            _snapshot = published = next with { Sequence = current.Sequence + 1 };
        }

        _publish(published, true);
    }
}
