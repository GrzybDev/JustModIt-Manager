using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>What a job is asked to do; a game has at most one job at a time.</summary>
/// <param name="Game">The game the job works on.</param>
/// <param name="GameName">The game's name, shown with the job.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public abstract record JobRequest(GameKey Game, string GameName, JobOrigin Origin)
{
    public abstract JobKind Kind { get; }
}

/// <summary>
/// Installs a project with the add-ons the player's choices give, next to the game's installs or in place of them, as the
/// app's install policy says; a project the game has already takes the place of its install.
/// </summary>
/// <param name="Game">The game to install on.</param>
/// <param name="GameName">The game's name, shown with the job.</param>
/// <param name="Project">The project to install, as the site locates it.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public sealed record InstallJobRequest(GameKey Game, string GameName, ProjectLocator Project, JobOrigin Origin = JobOrigin.User)
    : JobRequest(Game, GameName, Origin)
{
    public override JobKind Kind => JobKind.Install;
}

/// <summary>A job on one of the game's installs.</summary>
/// <param name="Record">The install's record.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public abstract record RecordJobRequest(InstallRecord Record, JobOrigin Origin) : JobRequest(Record.Game, Record.GameName, Origin);

/// <summary>Uninstalls an install, putting the game's originals back.</summary>
/// <param name="Record">The install's record.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public sealed record UninstallJobRequest(InstallRecord Record, JobOrigin Origin = JobOrigin.User) : RecordJobRequest(Record, Origin)
{
    public override JobKind Kind => JobKind.Uninstall;
}

/// <summary>
/// Installs the project's current release in place of the installed one: download, check that it fits once the old one
/// is gone, uninstall, install, and put the old one back if anything fails.
/// </summary>
/// <param name="Record">The installed release's record.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public abstract record ReinstallJobRequest(InstallRecord Record, JobOrigin Origin) : RecordJobRequest(Record, Origin);

/// <summary>Installs the project's newer release in place of the installed one.</summary>
/// <param name="Record">The installed release's record.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public sealed record UpdateJobRequest(InstallRecord Record, JobOrigin Origin = JobOrigin.User) : ReinstallJobRequest(Record, Origin)
{
    public override JobKind Kind => JobKind.Update;
}

/// <summary>Installs the project's current release again in place of an install whose files changed.</summary>
/// <param name="Record">The installed release's record.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public sealed record RepairJobRequest(InstallRecord Record, JobOrigin Origin = JobOrigin.User) : ReinstallJobRequest(Record, Origin)
{
    public override JobKind Kind => JobKind.Repair;
}

/// <summary>
/// Installs the project's current release with the add-ons the player chose in place of an install that is up to date;
/// it needs the project neither on offer nor completed, since the player chose the install already there.
/// </summary>
/// <param name="Record">The installed release's record.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public sealed record ChangeFeaturesJobRequest(InstallRecord Record, JobOrigin Origin = JobOrigin.User) : ReinstallJobRequest(Record, Origin)
{
    public override JobKind Kind => JobKind.Install;
}

/// <summary>
/// Puts back, at the start after a stop, the install that a replacement removed before its new install committed, as the
/// replacement itself would have after a failure.
/// </summary>
/// <param name="Marker">The replacement the stop cut short.</param>
public sealed record RestoreJobRequest(ReplacementMarker Marker) : RecordJobRequest(Marker.Previous, Marker.Origin)
{
    public override JobKind Kind => Marker.Kind;
}

/// <summary>
/// Package files the package creator built, installed on a detected game as one install to try them out, each of them
/// needed.
/// </summary>
/// <param name="Game">The game to install on.</param>
/// <param name="GameName">The game's name, shown with the job.</param>
/// <param name="PackagePaths">The package files' full paths, in the order they install.</param>
/// <param name="Source">What the creator knows about the packages, shown with the install.</param>
public sealed record TestInstallJobRequest(GameKey Game, string GameName, IReadOnlyList<string> PackagePaths, InstallSource? Source = null)
    : JobRequest(Game, GameName, JobOrigin.User)
{
    public override JobKind Kind => JobKind.TestInstall;
}

/// <summary>Moves an install's backups to where the settings now keep them.</summary>
/// <param name="Record">The install's record.</param>
/// <param name="Destination">Where the backups go.</param>
public sealed record MoveBackupsJobRequest(InstallRecord Record, BackupChoice Destination) : RecordJobRequest(Record, JobOrigin.User)
{
    public override JobKind Kind => JobKind.MoveBackups;
}

/// <summary>Points an install at the folder its store moved the game to.</summary>
/// <param name="Record">The install's record.</param>
public sealed record FollowMoveJobRequest(InstallRecord Record) : RecordJobRequest(Record, JobOrigin.Automatic)
{
    public override JobKind Kind => JobKind.FollowMove;
}
