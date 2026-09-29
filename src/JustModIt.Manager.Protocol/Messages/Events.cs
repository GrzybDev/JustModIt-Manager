using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>The agent's whole state after a change, coalesced: a reader that falls behind gets only the newest.</summary>
/// <param name="State">The newest snapshot.</param>
public sealed record StateChanged(AppStateView State) : Message;

/// <summary>How far the running jobs are, between state changes, at most about ten times a second.</summary>
/// <param name="Jobs">The jobs whose progress moved since the last time.</param>
public sealed record JobProgress(IReadOnlyList<JobView> Jobs) : Message;

/// <summary>A notice for the window to show while it is in front; otherwise the agent shows it from the tray.</summary>
/// <param name="Notice">What the automation did or found.</param>
public sealed record Notify(NoticeView Notice) : Message;

/// <summary>
/// The player asked to exit while jobs run; the window asks whether to wait for them and answers with
/// <see cref="ExitApp"/>, or does nothing when the player changes their mind.
/// </summary>
public sealed record ExitConfirmationRequested(int RunningJobs) : Message;

/// <summary>The window came to the front or left it, which decides where notices are shown.</summary>
/// <param name="IsActive">Whether the window is now in front.</param>
public sealed record WindowActivity(bool IsActive) : Message;

/// <summary>The window's answer to <see cref="ShutdownRequested"/>; it refuses while the player has unsaved work.</summary>
/// <param name="Accepted">Whether the window closes.</param>
public sealed record ShutdownAnswer(bool Accepted) : Message;
