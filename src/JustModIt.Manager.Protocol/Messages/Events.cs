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

    /// <summary>Maps how a job ended.</summary>
    /// <param name="result">The job's result.</param>
    public static JobEnded From(JobResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var kept = result.KeptFiles.GroupBy(file => file.Reason).OrderBy(files => files.Key)
            .Select(files => new KeptFilesView(files.Key, files.Count()));
        return new JobEnded(JobView.From(result.Job), result.Outcome, result.Failure is { } failure ? JobFailureView.From(failure) : null)
        {
            KeptFiles = result.KeptFiles.Count == 0 ? null : [.. kept],
            Forgotten = result.Forgotten,
        };
    }
}

/// <summary>A notice for the window to show while it is in front; otherwise the agent shows it from the tray.</summary>
/// <param name="Notice">What the automation did or found.</param>
public sealed record Notify(NoticeView Notice) : Message;

/// <summary>The window came to the front or left it, which decides where notices are shown.</summary>
/// <param name="IsActive">Whether the window is now in front.</param>
public sealed record WindowActivity(bool IsActive) : Message;

/// <summary>The window's answer to <see cref="ShutdownRequested"/>; it refuses while the player has unsaved work.</summary>
/// <param name="Accepted">Whether the window closes.</param>
public sealed record ShutdownAnswer(bool Accepted) : Message;
