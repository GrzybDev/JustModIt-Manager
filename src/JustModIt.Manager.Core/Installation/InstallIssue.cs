using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

/// <summary>A reason an operation was refused before anything changed, with game-relative paths unless named otherwise.</summary>
public abstract record InstallIssue
{
    /// <summary>
    /// The optional part of an install whose own check raised the issue, counted from 0, which the install then leaves out;
    /// null for an issue of a part the install needs or of the game as a whole.
    /// </summary>
    public int? Part { get; init; }
}

/// <summary>The game's folder is not there.</summary>
/// <param name="Directory">A full path.</param>
public sealed record GameFolderMissingIssue(string Directory) : InstallIssue;

/// <summary>Another install, uninstall or recovery works on this game, possibly in another copy of the app.</summary>
public sealed record GameBusyIssue : InstallIssue;

/// <summary>A program runs from the game's folder.</summary>
/// <param name="ProcessName">The program's name.</param>
public sealed record GameRunningIssue(string ProcessName) : InstallIssue;

/// <summary>The store is changing the game's files, or its records do not say clearly that it is done.</summary>
/// <param name="State">What the store reports.</param>
/// <param name="Reason">Why the store reports it.</param>
public sealed record GameNotReadyIssue(GameInstallState State, GameStateReason Reason) : InstallIssue;

/// <summary>The store no longer lists the game as installed.</summary>
public sealed record GameNotInstalledIssue : InstallIssue;

/// <summary>
/// An interrupted operation on this game has to be finished or undone first; a journal too damaged to tell its game
/// blocks every game.
/// </summary>
/// <param name="RecordId">The interrupted operation's record id.</param>
public sealed record RecoveryPendingIssue(Guid RecordId) : InstallIssue;

/// <summary>There is no record of this install, e.g. it was uninstalled in the meantime.</summary>
/// <param name="RecordId">The install's record id.</param>
public sealed record InstallNotFoundIssue(Guid RecordId) : InstallIssue;

/// <summary>The record of an install on this game cannot be read, e.g. it was written by a newer app.</summary>
/// <param name="RecordId">The install's record id.</param>
public sealed record UnreadableInstallIssue(Guid RecordId) : InstallIssue;

/// <summary>Another project's install owns a file this one would change; two owners would corrupt each other's originals.</summary>
/// <param name="Path">The file.</param>
/// <param name="RecordId">The record id of the install that owns it.</param>
public sealed record FileOwnedByOtherInstallIssue(string Path, Guid RecordId) : InstallIssue;

/// <summary>
/// An earlier uninstall left the file as the package's software changed it, keeping a copy of its original, and the file
/// still holds what the software left, so installing over it would take that for the game's own file; checking the game's
/// files in its store, or removing the kept copies, lets it go.
/// </summary>
/// <param name="Path">The file.</param>
/// <param name="RecordId">The kept-files record that holds it.</param>
public sealed record KeptFileIssue(string Path, Guid RecordId) : InstallIssue;

/// <summary>
/// Another app or build has installs or an unfinished operation on the game, a game around it or one inside it that the
/// package reaches, which only that app can undo, so this one keeps out until the other has uninstalled.
/// </summary>
/// <param name="App">The other app, as its owner file names it.</param>
/// <param name="Directory">Its folder, a full path.</param>
public sealed record GameManagedByOtherAppIssue(string App, string Directory) : InstallIssue;

/// <summary>
/// The path is invalid, a link would take it out of the game folder, or it leads into an app's folder or would make a
/// folder look like one.
/// </summary>
/// <param name="Path">The path as the package names it.</param>
public sealed record UnsafePathIssue(string Path) : InstallIssue;

/// <summary>A folder stands where the package needs a file, or a file where it needs a folder.</summary>
/// <param name="Path">Where the package needs the file or folder.</param>
public sealed record PathConflictIssue(string Path) : InstallIssue;

/// <summary>The package names the games it was made for in the game's own store, and this one is not among them.</summary>
/// <param name="Targets">The games the package names.</param>
public sealed record OtherGameIssue(ValueList<PackageTarget> Targets) : InstallIssue;

/// <summary>Only a newer version of the app can install an optional part of the install.</summary>
/// <param name="Error">Why its package was refused.</param>
public sealed record NewerAppRequiredIssue(PackageError Error) : InstallIssue;

/// <summary>A file the package patches or renames does not exist.</summary>
/// <param name="Path">The missing file.</param>
public sealed record MissingFileIssue(string Path) : InstallIssue;

/// <summary>A file to patch is not the version the package was made for.</summary>
/// <param name="Path">The file to patch.</param>
/// <param name="AlreadyPatched">The file already has the patched content, e.g. from a manual install.</param>
public sealed record FileMismatchIssue(string Path, bool AlreadyPatched) : InstallIssue;

/// <summary>Another program holds a file the operation changes.</summary>
/// <param name="Path">The file.</param>
/// <param name="Holders">The programs holding the files in use, when the system names them.</param>
public sealed record FileLockedIssue(string Path, ValueList<string> Holders) : InstallIssue;

/// <summary>The user may not change the file or folder, e.g. a game under Program Files.</summary>
/// <param name="Path">The file or folder; <c>.</c> for the game folder itself.</param>
public sealed record PermissionRequiredIssue(string Path) : InstallIssue;

/// <summary>A volume lacks the space the operation needs.</summary>
/// <param name="Location">A full path on the volume that lacks space.</param>
/// <param name="RequiredBytes">The bytes the operation needs there.</param>
/// <param name="AvailableBytes">The bytes free there.</param>
public sealed record NotEnoughSpaceIssue(string Location, long RequiredBytes, long AvailableBytes) : InstallIssue;

/// <summary>
/// The folder holding the originals cannot be reached, e.g. on a disconnected drive; it is never recreated empty.
/// </summary>
/// <param name="Directory">A full path.</param>
public sealed record BackupFolderUnavailableIssue(string Directory) : InstallIssue;

/// <summary>
/// Files the operation would have to put back hold no original any more: their copies were deleted, e.g. with the backup
/// folder. The store brings the originals back by checking or reinstalling the game's files.
/// </summary>
/// <param name="Paths">The files, game-relative.</param>
public sealed record OriginalsMissingIssue(ValueList<string> Paths) : InstallIssue;

/// <summary>Something to tell the player about an operation that can go ahead.</summary>
public abstract record InstallWarning;

/// <summary>Originals are copied to another drive instead of moved, which takes a while for large files.</summary>
/// <param name="Bytes">The bytes of originals to copy.</param>
public sealed record SlowBackupCopyWarning(long Bytes) : InstallWarning;

/// <summary>
/// The store keeps an unfinished download of the game that no job claims (<see cref="GameStateReason.UnfinishedDownload"/>);
/// if it applies it later, the installed files may change.
/// </summary>
public sealed record UnfinishedStoreDownloadWarning : InstallWarning;

/// <summary>The package names the game only in other stores, so it was made with another store's version of it.</summary>
/// <param name="Targets">The games the package names.</param>
public sealed record OtherStoreWarning(ValueList<PackageTarget> Targets) : InstallWarning;

/// <summary>The package was made and tested with another build of the game.</summary>
/// <param name="PackageBuildId">The build the package was made with.</param>
/// <param name="GameBuildId">The game's build; null when its store names none.</param>
public sealed record OtherGameBuildWarning(string PackageBuildId, string? GameBuildId) : InstallWarning;

/// <summary>What the check before an operation found.</summary>
/// <param name="Issues">Every reason the operation is refused; none when it can go ahead.</param>
/// <param name="Warnings">What to tell the player about an operation that can go ahead.</param>
public sealed record PreflightReport(ValueList<InstallIssue> Issues, ValueList<InstallWarning> Warnings)
{
    public bool CanProceed => Issues.Count == 0;

    /// <summary>The optional parts the install goes without, since their own checks failed on the game.</summary>
    public ValueList<LeftOutPart> LeftOut { get; init; }
}

public enum PartLeftOutReason
{
    /// <summary>The part does not fit the game as it is, e.g. it patches a file of another version of the game.</summary>
    GameVersion,

    /// <summary>Only a newer version of the app can install the part.</summary>
    NewerApp,

    /// <summary>The part would change a file that another project's install owns.</summary>
    OtherInstall,
}

/// <summary>An optional part an install goes without.</summary>
/// <param name="Index">Its place among the install's parts, counted from 0.</param>
/// <param name="Part">The part as the install was asked for it.</param>
/// <param name="Reason">Why its own check failed on the game.</param>
public sealed record LeftOutPart(int Index, InstallPart Part, PartLeftOutReason Reason)
{
    /// <summary>Why a part's issues leave it out; a newer app is named first, since the part's steps were never read.</summary>
    /// <param name="issues">The issues the part's own check raised.</param>
    public static PartLeftOutReason ReasonOf(IEnumerable<InstallIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var found = issues.ToList();
        return found.Exists(issue => issue is NewerAppRequiredIssue) ? PartLeftOutReason.NewerApp
            : found.TrueForAll(issue => issue is FileOwnedByOtherInstallIssue) ? PartLeftOutReason.OtherInstall
            : PartLeftOutReason.GameVersion;
    }
}

/// <summary>An operation was refused before anything changed.</summary>
public sealed class InstallBlockedException : Exception
{
    /// <summary>Creates the exception for an operation refused before any change.</summary>
    /// <param name="issues">Every reason it was refused.</param>
    /// <param name="innerException">The exception behind the refusal, if any.</param>
    public InstallBlockedException(IReadOnlyList<InstallIssue> issues, Exception? innerException = null)
        : base($"The operation was refused before any change: {string.Join(", ", issues)}.", innerException) =>
        Issues = [.. issues];

    public ValueList<InstallIssue> Issues { get; }
}

/// <summary>An operation failed part-way.</summary>
public sealed class InstallFailedException : Exception
{
    /// <summary>Creates the exception for an operation that failed after it changed something.</summary>
    /// <param name="rolledBack">
    /// False when some change could not be undone; the journal is kept and recovery tries again.
    /// </param>
    /// <param name="innerException">What failed.</param>
    public InstallFailedException(bool rolledBack, Exception innerException)
        : base(rolledBack ? "The operation failed and was rolled back." : "The operation failed and could not be fully rolled back.",
            innerException) =>
        RolledBack = rolledBack;

    public bool RolledBack { get; }
}
