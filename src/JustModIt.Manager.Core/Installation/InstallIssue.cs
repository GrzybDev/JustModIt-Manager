using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

/// <summary>A reason an operation was refused before anything changed, with game-relative paths unless named otherwise.</summary>
public abstract record InstallIssue;

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

/// <summary>The path is invalid, a link would take it out of the game folder, or it names the app's own folder.</summary>
/// <param name="Path">The path as the package names it.</param>
public sealed record UnsafePathIssue(string Path) : InstallIssue;

/// <summary>A folder stands where the package needs a file, or a file where it needs a folder.</summary>
/// <param name="Path">Where the package needs the file or folder.</param>
public sealed record PathConflictIssue(string Path) : InstallIssue;

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

/// <summary>The folder holding the originals is not there, e.g. on a disconnected drive; it is never recreated empty.</summary>
/// <param name="Directory">A full path.</param>
public sealed record BackupFolderUnavailableIssue(string Directory) : InstallIssue;

/// <summary>Something to tell the player about an operation that can go ahead.</summary>
public abstract record InstallWarning;

/// <summary>Originals are copied to another drive instead of moved, which takes a while for large files.</summary>
/// <param name="Bytes">The bytes of originals to copy.</param>
public sealed record SlowBackupCopyWarning(long Bytes) : InstallWarning;

/// <summary>The package names other games than this one.</summary>
public sealed record OtherGameWarning(ValueList<PackageTarget> Targets) : InstallWarning;

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
}

/// <summary>An operation was refused before anything changed.</summary>
public sealed class InstallBlockedException : Exception
{
    /// <summary>Creates the exception for an operation refused before any change.</summary>
    /// <param name="issues">Every reason it was refused.</param>
    /// <param name="innerException">The exception behind the refusal, if any.</param>
    public InstallBlockedException(IReadOnlyList<InstallIssue> issues, Exception? innerException = null)
        : base($"The operation was refused before any change: {string.Join(", ", issues ?? [])}.", innerException) =>
        Issues = [.. issues ?? []];

    /// <summary>Creates the exception without issues.</summary>
    public InstallBlockedException()
        : this([])
    {
    }

    /// <summary>Creates the exception without issues.</summary>
    /// <param name="message">The description for logs.</param>
    public InstallBlockedException(string message)
        : this(message, null!)
    {
    }

    /// <summary>Creates the exception without issues, caused by another exception.</summary>
    /// <param name="message">The description for logs.</param>
    /// <param name="innerException">The exception behind the refusal.</param>
    public InstallBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

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

    /// <summary>Creates the exception for an operation that was not rolled back.</summary>
    public InstallFailedException()
        : this("The operation failed.")
    {
    }

    /// <summary>Creates the exception for an operation that was not rolled back.</summary>
    /// <param name="message">The description for logs.</param>
    public InstallFailedException(string message)
        : this(message, null!)
    {
    }

    /// <summary>Creates the exception for an operation that was not rolled back, caused by another exception.</summary>
    /// <param name="message">The description for logs.</param>
    /// <param name="innerException">What failed.</param>
    public InstallFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public bool RolledBack { get; }
}
