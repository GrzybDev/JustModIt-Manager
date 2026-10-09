using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

internal static partial class InstallationLog
{
    /// <summary>Logs a finished install.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="package">The package file's name.</param>
    /// <param name="game">The game's key.</param>
    /// <param name="recordId">The new install's record id.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Installed {Package} for {Game} as {RecordId}")]
    public static partial void Installed(this ILogger logger, string package, string game, Guid recordId);

    /// <summary>Logs a failed install.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="rolledBack">Whether every change was undone.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Install {RecordId} failed; rolled back: {RolledBack}")]
    public static partial void InstallFailed(this ILogger logger, Guid recordId, bool rolledBack, Exception exception);

    /// <summary>Logs a finished uninstall.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="kept">How many files stayed because something changed them since the install.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Uninstalled {RecordId}; {Kept} files kept")]
    public static partial void Uninstalled(this ILogger logger, Guid recordId, int kept);

    /// <summary>Logs a failed uninstall.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="rolledBack">Whether every change was undone.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Uninstall of {RecordId} failed; rolled back: {RolledBack}")]
    public static partial void UninstallFailed(this ILogger logger, Guid recordId, bool rolledBack, Exception exception);

    /// <summary>Logs a file left alone because something else changed it after the operation.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="path">The file.</param>
    /// <param name="recordId">The operation's record id.</param>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Something else changed '{Path}' after operation {RecordId} touched it, so it stays as it is")]
    public static partial void ChangedByOthers(this ILogger logger, string path, Guid recordId);

    /// <summary>Logs a file left alone because its backup is damaged.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="path">The file.</param>
    /// <param name="recordId">The install's record id.</param>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The backup of '{Path}' in install {RecordId} is damaged, so the file stays as it is")]
    public static partial void BackupDamaged(this ILogger logger, string path, Guid recordId);

    /// <summary>Logs a journal entry that could not be undone.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="change">The entry's kind.</param>
    /// <param name="recordId">The operation's record id.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not undo a {Change} in operation {RecordId}")]
    public static partial void UndoFailed(this ILogger logger, string change, Guid recordId, Exception exception);

    /// <summary>Logs what an operation left behind and could not remove.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The operation's record id.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove what operation {RecordId} left behind")]
    public static partial void CleanupFailed(this ILogger logger, Guid recordId, Exception exception);

    /// <summary>Logs an interrupted operation that recovery finished or undid.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The operation's record id.</param>
    /// <param name="outcome">What recovery did.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered interrupted operation {RecordId}: {Outcome}")]
    public static partial void Recovered(this ILogger logger, Guid recordId, RecoveryOutcome outcome);

    /// <summary>Logs an interrupted operation that recovery failed on.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The operation's record id.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not recover operation {RecordId}")]
    public static partial void RecoveryFailed(this ILogger logger, Guid recordId, Exception exception);

    /// <summary>Logs an interrupted operation that waits for its game folder or backups to come back.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The operation's record id.</param>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The game folder or the backups of interrupted operation {RecordId} are not available")]
    public static partial void RecoveryUnavailable(this ILogger logger, Guid recordId);

    /// <summary>Logs an install whose backups moved.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="location">The kind of location they moved to.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Moved the backups of install {RecordId} to {Location}")]
    public static partial void BackupsMoved(this ILogger logger, Guid recordId, string location);

    /// <summary>Logs an install whose list of files cannot be read.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "The file list of install {RecordId} cannot be read")]
    public static partial void UnreadableFileList(this ILogger logger, Guid recordId, Exception exception);

    /// <summary>Logs an install forgotten together with its backups.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="count">How many backups were removed.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot install {RecordId} and removed {Count} backups")]
    public static partial void Forgotten(this ILogger logger, Guid recordId, int count);

    /// <summary>Logs an install forgotten whose backups stay as leftovers.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot install {RecordId} and kept its backups as leftovers")]
    public static partial void ForgottenKeepingBackups(this ILogger logger, Guid recordId);

    /// <summary>Logs leftover backups that were removed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The forgotten install's record id.</param>
    /// <param name="count">How many backups were removed.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} leftover backups of install {RecordId}")]
    public static partial void LeftoverBackupsRemoved(this ILogger logger, Guid recordId, int count);

    /// <summary>Logs an install that followed its game to the folder the store moved it to.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="recordId">The install's record id.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Install {RecordId} follows its game to the folder the store moved it to")]
    public static partial void Adopted(this ILogger logger, Guid recordId);
}
