using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Core.Jobs;

public enum JobOutcome
{
    Succeeded,
    Failed,
    Cancelled,

    /// <summary>
    /// An automatic job found its game in use and ended before changing anything; see <see cref="JobResult.DeferredBy"/>.
    /// </summary>
    Deferred,
}

/// <summary>How a job ended.</summary>
/// <param name="Job">The job's last snapshot.</param>
/// <param name="Outcome">How it ended; <see cref="Failure"/> says why a job failed.</param>
public sealed record JobResult(JobSnapshot Job, JobOutcome Outcome)
{
    public JobFailure? Failure { get; init; }

    /// <summary>
    /// The install the job left: a new one, the one it changed (moved backups, followed a moved game), or the previous
    /// release it put back after a failed update.
    /// </summary>
    public InstallRecord? Installed { get; init; }

    /// <summary>The install that no longer exists: uninstalled, replaced by an update or forgotten.</summary>
    public Guid? RemovedRecord { get; init; }

    public ValueList<InstallWarning> Warnings { get; init; }

    /// <summary>Files an uninstall left as they were, because something changed them since the install.</summary>
    public ValueList<KeptFile> KeptFiles { get; init; }

    /// <summary>The game folder was gone for good, so only the record and its backups were dropped.</summary>
    public bool Forgotten { get; init; }

    /// <summary>An update or repair had already uninstalled the previous release when it failed.</summary>
    public bool RemovedPrevious { get; init; }

    /// <summary>The previous release was put back after the update or repair failed.</summary>
    public bool RestoredPrevious { get; init; }

    /// <summary>The player cancelled the job; otherwise the app's exit did, or it never started.</summary>
    public bool CancelledByPlayer { get; init; }

    public JobWaitReason? DeferredBy { get; init; }

    /// <summary>What the game ran when the job was deferred, to wait for its exit.</summary>
    public ValueList<RunningProcess> RunningProcesses { get; init; }

    /// <summary>The release the job installed or tried to install.</summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>The package the job downloaded; kept while a retry may need it, so retrying counts no new download.</summary>
    public Guid? PackageFileId { get; init; }

    public bool IsSuccess => Outcome == JobOutcome.Succeeded;

    /// <summary>The player lost the release an update or repair started from.</summary>
    public bool LostPrevious => RemovedPrevious && !RestoredPrevious;
}
