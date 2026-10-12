using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Messages;

// A job request is answered once the job is queued, never when it ends: how it goes arrives with the state. A project is
// installed on a game; every other job works on one install, addressed by its id.

/// <summary>
/// Installs a project on a game as <see cref="Views.GameView.InstallReplaces"/> tells; a project the game has already
/// takes the place of its install, e.g. to switch the variant.
/// </summary>
/// <param name="Game">A detected game.</param>
/// <param name="ProjectId">One of the projects the game offers.</param>
/// <param name="VariantName">The variant to install; null installs the one the release recommends.</param>
public sealed record InstallProject(GameKey Game, Guid ProjectId, string? VariantName = null) : Message;

/// <summary>Installs the project's newer release in place of an install.</summary>
/// <param name="Install">The install's record id.</param>
public sealed record UpdateProject(Guid Install) : Message;

/// <summary>Installs the project's current release again in place of an install whose files changed.</summary>
/// <param name="Install">The install's record id.</param>
public sealed record RepairProject(Guid Install) : Message;

/// <summary>Uninstalls an install, putting the game's originals back.</summary>
/// <param name="Install">The install's record id.</param>
public sealed record UninstallProject(Guid Install) : Message;

/// <summary>Installs a package file the package creator built on a detected game, to try it out.</summary>
/// <param name="Game">A detected game.</param>
/// <param name="PackagePath">The package file's full path.</param>
public sealed record TestInstallPackage(GameKey Game, string PackagePath) : Message;

/// <summary>
/// Moves the backups of every install that keeps them elsewhere, where they can be reached, to where the settings now
/// keep them, one job per install, each once its game has no other job.
/// </summary>
public sealed record MoveBackups : Message;

/// <summary>The answer to a job request, naming the queued job.</summary>
/// <param name="JobId">The id that <see cref="JobView.Id"/> and <see cref="CancelJob"/> use.</param>
public sealed record JobStarted(Guid JobId) : Message;

/// <summary>Cancels a running job.</summary>
/// <param name="Job">
/// The job rather than its game, so a job that started on the game after the one the player meant has ended keeps
/// running.
/// </param>
public sealed record CancelJob(Guid Job) : Message;

/// <summary>Dismisses the last failure the player saw, of an install or, with none, of the game.</summary>
/// <param name="Game">The game the failure shows on.</param>
/// <param name="Install">The install that failed; null for a failure of the game itself.</param>
public sealed record DismissFailure(GameKey Game, Guid? Install = null) : Message;

/// <summary>The player opened the game's page, so its installs are checked now.</summary>
/// <param name="Game">The game whose page opened.</param>
public sealed record OpenedGame(GameKey Game) : Message;

/// <summary>Scans the stores, reads the catalogue again and looks for an update of the app.</summary>
public sealed record CheckNow : Message;

/// <summary>
/// Saves the player's settings; refused with <see cref="Transport.ErrorCode.InvalidRequest"/> for a backup folder the
/// agent does not accept.
/// </summary>
/// <param name="Settings">Every setting the view carries, changed or not.</param>
public sealed record ChangeSettings(SettingsView Settings) : Message;

/// <summary>Sets the player's own automation choices for one game.</summary>
/// <param name="Game">The game the choices are for.</param>
/// <param name="AutoInstall">Whether new projects install on their own; null leaves the choice as it is.</param>
/// <param name="AutoUpdate">Whether installs update on their own; null leaves the choice as it is.</param>
public sealed record SetGameAutomation(GameKey Game, bool? AutoInstall = null, bool? AutoUpdate = null) : Message;

/// <summary>Remembers which project the player chose for a game that offers several.</summary>
/// <param name="Game">The game the choice is for.</param>
/// <param name="ProjectId">Null forgets the choice.</param>
public sealed record ChooseProject(GameKey Game, Guid? ProjectId = null) : Message;

/// <summary>Remembers which variant the player chose for a game's project.</summary>
/// <param name="Game">The game the choice is for.</param>
/// <param name="VariantName">Null forgets the choice.</param>
public sealed record ChooseVariant(GameKey Game, string? VariantName = null) : Message;

/// <summary>Hides a game from the player's list, or shows it again.</summary>
/// <param name="Game">The game to hide or show.</param>
/// <param name="IsHidden">Whether the game is hidden from now on.</param>
public sealed record HideGame(GameKey Game, bool IsHidden) : Message;

/// <summary>The player confirmed being an adult, which lets the catalogue show adult games for a day.</summary>
public sealed record ConfirmAdultContent : Message;

/// <summary>
/// Starts the game through its store, or opens its page in a store client that starts games only from there; refused as
/// unsupported for a store the app has no link for.
/// </summary>
/// <param name="Game">A detected game.</param>
public sealed record LaunchGame(GameKey Game) : Message;

/// <summary>A folder that <see cref="OpenFolder"/> shows in the file manager.</summary>
public enum AppFolder
{
    /// <summary>The folder of <see cref="OpenFolder.Game"/>.</summary>
    Game,

    /// <summary>Where <see cref="OpenFolder.Install"/> keeps the originals.</summary>
    InstallBackups,

    Logs,
}

/// <summary>Shows a folder in the file manager.</summary>
/// <param name="Folder">Which folder to show.</param>
/// <param name="Game">The game, for <see cref="AppFolder.Game"/>.</param>
/// <param name="Install">The install's record id, for <see cref="AppFolder.InstallBackups"/>.</param>
public sealed record OpenFolder(AppFolder Folder, GameKey? Game = null, Guid? Install = null) : Message;

/// <summary>
/// Adds a folder the player picked because detection did not find a game; answered with <see cref="LibraryFolderAdded"/>.
/// </summary>
/// <param name="Path">The folder as picked.</param>
public sealed record AddLibraryFolder(string Path) : Message;

/// <summary>The answer to <see cref="AddLibraryFolder"/>.</summary>
/// <param name="Store">Null when the folder was refused.</param>
/// <param name="Path">The folder added, which may be an ancestor of the one picked.</param>
/// <param name="Rejection">Why the folder was refused; null when it was added.</param>
public sealed record LibraryFolderAdded(string? Store = null, string? Path = null, LibraryFolderRejection? Rejection = null)
    : Message;

/// <summary>Forgets a folder the player added for a store.</summary>
/// <param name="Store">The store's id, as <see cref="LibraryFolderAdded.Store"/> gave it.</param>
/// <param name="Path">The folder as it was added.</param>
public sealed record RemoveLibraryFolder(string Store, string Path) : Message;

/// <summary>Asks whether the agent accepts a folder for backups; answered with <see cref="BackupFolderChecked"/>.</summary>
/// <param name="Folder">The folder the player picked.</param>
public sealed record ValidateBackupFolder(string Folder) : Message;

/// <summary>The answer to <see cref="ValidateBackupFolder"/>.</summary>
/// <param name="Problem">Null when the folder can be used.</param>
public sealed record BackupFolderChecked(BackupFolderProblem? Problem = null) : Message;

/// <summary>
/// Removes the backups that installs forgotten with their game left behind, which only the player may decide; answered
/// once they are gone.
/// </summary>
/// <param name="RecordIds">The forgotten installs' ids; one whose folder is not there stays listed.</param>
public sealed record RemoveLeftoverBackups(IReadOnlyList<Guid> RecordIds) : Message;

/// <summary>Restarts into the downloaded update once the window has agreed to close.</summary>
public sealed record ApplyAppUpdate : Message;

public enum ExitMode
{
    /// <summary>Running jobs are cancelled; changes they made to games are undone first.</summary>
    Now,

    /// <summary>
    /// The player's running jobs finish, automatic ones are cancelled and no new ones start; the window can still be opened
    /// meanwhile, and an exit now cuts the wait short.
    /// </summary>
    AfterJobs,
}

/// <summary>Exits the app.</summary>
/// <param name="Mode">Whether running jobs are cancelled or waited for.</param>
public sealed record ExitApp(ExitMode Mode) : Message;
