using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>The agent's whole state after a change, coalesced: a reader that falls behind gets only the newest.</summary>
/// <param name="State">The newest snapshot.</param>
public sealed record StateChanged(AppStateView State) : Message;

/// <summary>How far the running jobs are, between state changes, at most about ten times a second.</summary>
/// <param name="Jobs">The jobs whose progress moved since the last time.</param>
public sealed record JobProgress(IReadOnlyList<JobView> Jobs) : Message;

/// <summary>
/// A job ended, sent once for each: unlike the coalesced state, it also tells of a job that ended before any state listed
/// it, and why it failed when no game in the player's list keeps the failure.
/// </summary>
/// <param name="Job">The job's last snapshot.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Failure">Why it failed; null for any other end.</param>
public sealed record JobEnded(JobView Job, JobOutcome Outcome, JobFailureView? Failure = null) : Message
{
    /// <summary>How many files an uninstall left as they were, by reason; null when it left none.</summary>
    public IReadOnlyList<KeptFilesView>? KeptFiles { get; init; }

    /// <summary>
    /// The install's folder was gone for good, so an uninstall put no files back and only dropped what the app kept of it.
    /// </summary>
    public bool Forgotten { get; init; }

    /// <summary>What the player hears of what the job installed, each once; null when nothing.</summary>
    public IReadOnlyList<InstallWarningKind>? Warnings { get; init; }

    /// <summary>
    /// The launch options the app added for the removed install, which it takes out of the player's options in Steam; null when
    /// there are none.
    /// </summary>
    public IReadOnlyList<string>? RemovedLaunchOptions { get; init; }

    /// <summary>Maps how a job ended.</summary>
    /// <param name="result">The job's result.</param>
    public static JobEnded From(JobResult result)
    {
        var kept = result.KeptFiles.GroupBy(file => file.Reason).OrderBy(files => files.Key)
            .Select(files => new KeptFilesView(files.Key, files.Count()));
        var warnings = result.Warnings.Select(warning => warning switch
        {
            OtherStoreWarning => InstallWarningKind.OtherStore,
            OtherGameBuildWarning => InstallWarningKind.OtherBuild,
            UnfinishedStoreDownloadWarning => InstallWarningKind.UnfinishedStoreDownload,
            // A slow copy of originals matters only before an install, so it stays in the log.
            _ => (InstallWarningKind?)null,
        }).OfType<InstallWarningKind>().Distinct().ToList();
        return new JobEnded(JobView.From(result.Job), result.Outcome, result.Failure is { } failure ? JobFailureView.From(failure) : null)
        {
            KeptFiles = result.KeptFiles.Count == 0 ? null : [.. kept],
            Forgotten = result.Forgotten,
            Warnings = warnings.Count == 0 ? null : warnings,
            RemovedLaunchOptions = result.RemovedLaunchOptions.Count == 0 ? null : [.. result.RemovedLaunchOptions],
        };
    }
}

/// <summary>A notice for the window to show while it is in front; otherwise the agent shows it from the tray.</summary>
/// <param name="Notice">What the automation did or found.</param>
public sealed record Notify(NoticeView Notice) : Message;

/// <summary>The window came to the front or left it, which decides where notices are shown.</summary>
/// <param name="IsActive">Whether the window is now in front.</param>
public sealed record WindowActivity(bool IsActive) : Message;

/// <summary>
/// The window closes to start anew in the agent's version in the same process, so its going is no close of the player's, and
/// the page it was given as it connected goes to the window it becomes.
/// </summary>
public sealed record WindowRestarting : Message;

/// <summary>The window's answer to <see cref="ShutdownRequested"/>; it refuses while the player has unsaved work.</summary>
/// <param name="Accepted">Whether the window closes.</param>
public sealed record ShutdownAnswer(bool Accepted) : Message;

/// <summary>A step of uninstalling the app itself, in the order they run; the program goes last.</summary>
public enum AppUninstallStep
{
    /// <summary>Starting with the system, which also runs the agent: its registration goes.</summary>
    Autostart,

    /// <summary>The app's entry among the system's apps, its icons and its link scheme.</summary>
    Integration,

    /// <summary>The program file, then what its runs left outside the app's data.</summary>
    Program,
}

public enum AppUninstallProblem
{
    /// <summary>The system did not answer within <see cref="UninstallApp.SystemTimeLimit"/>.</summary>
    NoAnswer,

    /// <summary>The system's service manager refused the change.</summary>
    Refused,

    /// <summary>The user may not change the files.</summary>
    AccessDenied,

    /// <summary>The files could not be changed for another reason.</summary>
    Failed,
}

/// <summary>Why a step of uninstalling the app failed, and whether what the steps before it took away came back.</summary>
/// <param name="Problem">What went wrong.</param>
/// <param name="Folder">The folder whose files could not be changed, when the step names one; null otherwise.</param>
/// <param name="Restored">Everything the steps before took away came back, so the app runs as before.</param>
public sealed record AppUninstallFailure(AppUninstallProblem Problem, string? Folder = null, bool Restored = false);

/// <summary>A step of uninstalling the app ended, done or failed with what came back, the last one before the agent exits.</summary>
/// <param name="Step">The step that ended.</param>
/// <param name="Failure">Why it failed; null once it is done.</param>
public sealed record AppUninstallProgress(AppUninstallStep Step, AppUninstallFailure? Failure = null) : Message;
