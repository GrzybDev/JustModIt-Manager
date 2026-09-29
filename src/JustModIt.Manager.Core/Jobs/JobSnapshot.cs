using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Jobs;

public enum JobKind
{
    Install,
    Uninstall,

    /// <summary>The project's current release replaces the installed one.</summary>
    Update,

    /// <summary>The project's current release is installed again after something, usually the store, changed its files.</summary>
    Repair,

    /// <summary>A package file from the package creator, installed to try it out; it is never updated.</summary>
    TestInstall,

    /// <summary>An install's backups go to where the settings now keep them.</summary>
    MoveBackups,

    /// <summary>An install follows its game to the folder the store moved it to, once its files are found there.</summary>
    FollowMove,
}

public enum JobOrigin
{
    User,

    /// <summary>
    /// Started by the automation; the app's exit cancels it, and it ends rather than wait for a game in use until it has
    /// downloaded its package.
    /// </summary>
    Automatic,
}

public enum JobStage
{
    Queued,

    /// <summary>See <see cref="JobSnapshot.WaitReason"/>.</summary>
    Waiting,

    /// <summary>Reading the project's current release from the site.</summary>
    Preparing,

    Downloading,

    /// <summary>
    /// Checking the package, or the files of the install to remove, against the game before anything changes; the engine
    /// moves the job on to <see cref="Installing"/> or <see cref="Uninstalling"/> once it starts changing files.
    /// </summary>
    Checking,

    Uninstalling,

    Installing,

    /// <summary>A change that failed part-way is being undone.</summary>
    RollingBack,

    /// <summary>An update or repair failed after the installed release was removed, which is being put back.</summary>
    RestoringPrevious,

    Verifying,

    MovingBackups,
}

public enum JobWaitReason
{
    /// <summary>Operations a crash interrupted are finished or undone first.</summary>
    Recovery,

    /// <summary>See <see cref="JobSnapshot.RunningProcesses"/>.</summary>
    GameRunning,

    /// <summary>The store is installing, updating or checking the game, or rewriting its records of it.</summary>
    StoreBusy,

    /// <summary>The store touched the game moments ago and may not be done with it.</summary>
    QuietPeriod,

    /// <summary>Another job holds the single download slot.</summary>
    Download,
}

/// <summary>A job as it is at one moment; every change publishes a new snapshot.</summary>
public sealed record JobSnapshot
{
    public required Guid Id { get; init; }

    public required GameKey Game { get; init; }

    public required string GameName { get; init; }

    public required JobKind Kind { get; init; }

    public required JobOrigin Origin { get; init; }

    public JobStage Stage { get; init; }

    /// <summary>Set exactly while <see cref="Stage"/> is <see cref="JobStage.Waiting"/>.</summary>
    public JobWaitReason? WaitReason { get; init; }

    /// <summary>The programs the job waits for to exit, by name.</summary>
    public ValueList<string> RunningProcesses { get; init; }

    /// <summary>From 0 to 1; null while the stage cannot tell how far it is.</summary>
    public double? Fraction { get; init; }

    public long BytesDone { get; init; }

    public long? BytesTotal { get; init; }

    /// <summary>Grows with every snapshot of the job, so one that arrives late can be told apart and dropped.</summary>
    public long Sequence { get; init; }

    /// <summary>Whether the stage, the wait and what it waits for are the same, so the two differ in progress at most.</summary>
    /// <param name="other">Another snapshot of the job.</param>
    public bool HasSameStep(JobSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Stage == other.Stage && WaitReason == other.WaitReason && RunningProcesses == other.RunningProcesses;
    }
}

/// <summary>A started job: what it is, and its result once it has finished.</summary>
/// <param name="Job">The job as it was when it was started.</param>
/// <param name="Completion">Never faults: failures and cancellations are results.</param>
public sealed record JobHandle(JobSnapshot Job, Task<JobResult> Completion);
