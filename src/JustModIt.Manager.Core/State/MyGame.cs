using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.State;

/// <summary>A game in the player's list: detected with something to install, or with an install on it.</summary>
public sealed record MyGame
{
    public required GameKey Key { get; init; }

    public required string Name { get; init; }

    public required MyGameStatus Status { get; init; }

    /// <summary>What its store reports; null when no store lists the game any more.</summary>
    public InstalledGame? Game { get; init; }

    /// <inheritdoc cref="GameOffer.Projects"/>
    public IReadOnlyList<ProjectOffer> Projects { get; init; } = [];

    /// <summary>
    /// The installed project as the catalogue has it now, also once it is no longer on offer; otherwise the chosen or only
    /// one on offer. Null while several teams offer one and the player has not chosen.
    /// </summary>
    public ProjectOffer? Project { get; init; }

    public InstallRecord? Installed { get; init; }

    public GamePreferences Preferences { get; init; } = GamePreferences.Default;

    public JobSnapshot? Job { get; init; }

    /// <summary>
    /// The game's store already offers it in the app's target language, so the app never changes it on its own; always
    /// false for an app without a target language.
    /// </summary>
    public bool OffersTargetLanguage { get; init; }

    /// <summary>The last job on the game failed, or lost the release it started from; it stays until a job succeeds.</summary>
    public FailedAttempt? LastFailure { get; init; }

    /// <summary>What runs from the game's folder, when the app happens to know; empty otherwise.</summary>
    public ValueList<string> RunningProcesses { get; init; }

    public bool IsHidden => Preferences.IsHidden;

    public bool IsAdult => Project?.Project.Game.IsAdult ?? Projects.Any(offer => offer.Project.Game.IsAdult);

    /// <summary>
    /// Something only the player can sort out: an install that needs repairing, lost its game or its originals, or a job
    /// that failed.
    /// </summary>
    public bool NeedsAttention =>
        LastFailure is not null
        || Status.State is MyGameState.NeedsRepair or MyGameState.BackupsUnavailable
        || Status is { State: MyGameState.Orphaned, Orphan: OrphanReason.GameNotDetected or OrphanReason.GameMoved };
}

/// <summary>A job on a game that did not end well, kept until a later job on the game succeeds or the player dismisses it.</summary>
/// <param name="Kind">What the job did.</param>
/// <param name="Origin">Whether the player or the automation started it.</param>
/// <param name="At">When it ended.</param>
/// <param name="Failure">Null when the job was cancelled.</param>
public sealed record FailedAttempt(JobKind Kind, JobOrigin Origin, DateTimeOffset At, JobFailure? Failure)
{
    /// <summary>An update or repair removed the installed release and could not put it back.</summary>
    public bool LostPrevious { get; init; }

    public Guid? ReleaseId { get; init; }

    /// <summary>The package the job downloaded, kept so that trying again counts no new download.</summary>
    public Guid? PackageFileId { get; init; }

    /// <summary>What to keep of a job that failed or lost the installed release; null for any other end.</summary>
    /// <param name="result">How the job ended.</param>
    /// <param name="now">When it ended.</param>
    public static FailedAttempt? From(JobResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        var failed = result.Outcome == JobOutcome.Failed && result.Failure?.Reason != JobFailureReason.AlreadyRunning;
        return failed || result.LostPrevious
            ? new FailedAttempt(result.Job.Kind, result.Job.Origin, now, result.Failure)
            {
                LostPrevious = result.LostPrevious,
                ReleaseId = result.ReleaseId,
                PackageFileId = result.PackageFileId,
            }
            : null;
    }
}
