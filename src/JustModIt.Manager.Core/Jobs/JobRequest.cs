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

/// <param name="Game">The game to install on.</param>
/// <param name="GameName">The game's name, shown with the job.</param>
/// <param name="Project">The project to install, as the site locates it.</param>
/// <param name="VariantName">The variant to install; null installs the one the release recommends.</param>
/// <param name="Origin">Whether the player or the automation asked for the job.</param>
public sealed record InstallJobRequest(GameKey Game, string GameName, ProjectLocator Project, string? VariantName,
    JobOrigin Origin = JobOrigin.User) : JobRequest(Game, GameName, Origin)
{
    public override JobKind Kind => JobKind.Install;
}

public sealed record UninstallJobRequest(InstallRecord Record, JobOrigin Origin = JobOrigin.User)
    : JobRequest(Record.Game, Record.GameName, Origin)
{
    public override JobKind Kind => JobKind.Uninstall;
}

/// <summary>
/// Installs the project's current release in place of the installed one: download, check that it fits once the old one
/// is gone, uninstall, install, and put the old one back if anything fails.
/// </summary>
public abstract record ReinstallJobRequest(InstallRecord Record, JobOrigin Origin) : JobRequest(Record.Game, Record.GameName, Origin);

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

/// <summary>A package file the package creator built, installed on a detected game to try it out.</summary>
/// <param name="Game">The game to install on.</param>
/// <param name="GameName">The game's name, shown with the job.</param>
/// <param name="PackagePath">The package file's full path.</param>
/// <param name="Source">What the creator knows about the package, shown with the install.</param>
public sealed record TestInstallJobRequest(GameKey Game, string GameName, string PackagePath, InstallSource? Source = null)
    : JobRequest(Game, GameName, JobOrigin.User)
{
    public override JobKind Kind => JobKind.TestInstall;
}

public sealed record MoveBackupsJobRequest(InstallRecord Record, BackupChoice Destination)
    : JobRequest(Record.Game, Record.GameName, JobOrigin.User)
{
    public override JobKind Kind => JobKind.MoveBackups;
}

public sealed record FollowMoveJobRequest(InstallRecord Record) : JobRequest(Record.Game, Record.GameName, JobOrigin.Automatic)
{
    public override JobKind Kind => JobKind.FollowMove;
}
