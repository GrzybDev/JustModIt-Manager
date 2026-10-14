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

    /// <summary>The one thing the player must hear first about the game and its installs.</summary>
    public required MyGameStatus Status { get; init; }

    /// <summary>What its store reports; null when no store lists the game any more.</summary>
    public InstalledGame? Game { get; init; }

    /// <inheritdoc cref="GameOffer.Projects"/>
    public IReadOnlyList<ProjectOffer> Projects { get; init; } = [];

    /// <summary>
    /// The project on offer that a new install takes, as <see cref="GameOffer.Choose"/> picks it; null while several teams
    /// offer one and none of them is chosen or preferred.
    /// </summary>
    public ProjectOffer? Project { get; init; }

    /// <summary>The installs on the game: those in its current folder first, then those its store moved it away from.</summary>
    public ValueList<MyInstall> Installs { get; init; }

    /// <summary>What installing a project on the game does with its installs; null while no store reports the game.</summary>
    public InstallAdmission? NewInstall { get; init; }

    public GamePreferences Preferences { get; init; } = GamePreferences.Default;

    public JobSnapshot? Job { get; init; }

    /// <summary>
    /// The game's store already offers it in the app's target language, so the app never changes it on its own; always
    /// false for an app without a target language.
    /// </summary>
    public bool OffersTargetLanguage { get; init; }

    /// <summary>
    /// The last job that installed a project on the game failed, or a job lost the release an install came from; it stays
    /// until a job on the game succeeds.
    /// </summary>
    public FailedAttempt? LastFailure { get; init; }

    /// <summary>What runs from the game's folder, when the app happens to know; empty otherwise.</summary>
    public ValueList<string> RunningProcesses { get; init; }

    public bool IsHidden => Preferences.IsHidden;

    public bool IsAdult => Offers.Any(offer => offer.Project.Game.IsAdult);

    /// <summary>The game's ids on the site, as the projects on offer and installed name it.</summary>
    public IEnumerable<Guid> SiteIds => Offers.Select(offer => offer.Project.Game.Uuid).Distinct();

    /// <summary>
    /// Something only the player can sort out: an install that needs repairing, lost its game or its originals, or a job
    /// that failed.
    /// </summary>
    public bool NeedsAttention => LastFailure is not null || Installs.Any(install => install.NeedsAttention);

    /// <summary>The install on the game; null when the game has none with this id.</summary>
    /// <param name="id">The install's record id.</param>
    public MyInstall? FindInstall(Guid id) => Installs.FirstOrDefault(install => install.Record.Id == id);

    private IEnumerable<ProjectOffer> Offers => Projects.Concat(Installs.Select(install => install.Project)).OfType<ProjectOffer>();
}

/// <summary>An install on a game in the player's list, with what it needs.</summary>
public sealed record MyInstall
{
    public required InstallRecord Record { get; init; }

    public required MyGameStatus Status { get; init; }

    /// <summary>
    /// The folder holding the install's originals is not there, also while <see cref="Status"/> tells something else first.
    /// </summary>
    public bool BackupsUnavailable { get; init; }

    /// <summary>
    /// The installed project as the catalogue has it now, also once it is no longer on offer, since the player chose it and
    /// it keeps its updates; null when the catalogue does not have it.
    /// </summary>
    public ProjectOffer? Project { get; init; }

    /// <summary>The last job on the install failed; it stays until a job on it succeeds.</summary>
    public FailedAttempt? LastFailure { get; init; }

    /// <summary>The project's name as the catalogue has it now, else as the install's record keeps it.</summary>
    public string? ProjectName => Project?.Project.ProjectName ?? Record.Source?.ProjectName;

    /// <summary>
    /// Something only the player can sort out: the install needs repairing, lost its game or its originals, or a job on it
    /// failed.
    /// </summary>
    public bool NeedsAttention =>
        LastFailure is not null
        || Status.State is MyGameState.NeedsRepair or MyGameState.BackupsUnavailable
        || Status is { State: MyGameState.Orphaned, Orphan: OrphanReason.GameNotDetected or OrphanReason.GameMoved };
}

/// <summary>
/// What a failed job is kept on until a job on it succeeds or the player dismisses it: the install it worked on, or the
/// game, for a job that installed a project or lost the release an install came from.
/// </summary>
/// <param name="Game">The game the job worked on.</param>
/// <param name="Install">The install's record id; null for the game itself.</param>
public readonly record struct FailureKey(GameKey Game, Guid? Install);

/// <summary>A job that did not end well.</summary>
/// <param name="Kind">What the job did.</param>
/// <param name="Origin">Whether the player or the automation started it.</param>
/// <param name="At">When it ended.</param>
/// <param name="Failure">Null when the job was cancelled.</param>
public sealed record FailedAttempt(JobKind Kind, JobOrigin Origin, DateTimeOffset At, JobFailure? Failure)
{
    /// <summary>An update or repair removed the installed release and could not put it back.</summary>
    public bool LostPrevious { get; init; }

    public Guid? ReleaseId { get; init; }

    /// <summary>The packages the job downloaded, by the downloads they came from, kept so that trying again counts no new download.</summary>
    public ValueList<Guid> PackageFileIds { get; init; }

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
                PackageFileIds = result.PackageFileIds,
            }
            : null;
    }

    /// <summary>
    /// What the failure of <paramref name="result"/> is kept on: the install the job left or worked on, or the game when it
    /// installed a project or left nothing of the install it worked on.
    /// </summary>
    /// <param name="result">How the job ended.</param>
    public static FailureKey KeyOf(JobResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var install = result.Job.InstallId is null || result.LostPrevious ? null : result.Installed?.Id ?? result.Job.InstallId;
        return new FailureKey(result.Job.Game, install);
    }
}
