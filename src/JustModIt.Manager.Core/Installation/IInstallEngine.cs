using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

/// <inheritdoc cref="InstallEngine"/>
public interface IInstallEngine
{
    /// <summary>Undoes or finishes what a crash interrupted; meant to be queued first after a start.</summary>
    /// <param name="cancellationToken">Stops recovery.</param>
    Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Other apps, or other builds of this one, that manage files of the game, which keeps this one from installing there;
    /// asked before a package is downloaded, since the site counts every download.
    /// </summary>
    /// <param name="gameDirectory">The game's folder.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    Task<IReadOnlyList<GameManagedByOtherAppIssue>> FindOtherAppsAsync(string gameDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Everything that would stop the install, what to tell the player and the optional parts it would go without, without
    /// changing anything.
    /// </summary>
    /// <param name="request">The packages and the game.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InvalidPackageException">
    /// A package is malformed or unsafe, or one the install needs was made for a newer app.
    /// </exception>
    /// <exception cref="InstallBlockedException">The game folder is gone.</exception>
    Task<PreflightReport> CheckInstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Installs packages as one journaled operation, backing up every original they replace, without the optional parts
    /// whose own checks fail on the game.
    /// </summary>
    /// <param name="request">The packages and the game.</param>
    /// <param name="progress">Reports the check and the install.</param>
    /// <param name="cancellationToken">Stops the install; what it changed by then is undone.</param>
    /// <exception cref="InvalidPackageException">
    /// A package is malformed or unsafe, or one the install needs was made for a newer app.
    /// </exception>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    /// <exception cref="InstallFailedException">Failed part-way; the changes were undone unless it says otherwise.</exception>
    Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether packages would install once the given install is uninstalled, and without which optional parts, as an update
    /// or repair needs to know before it removes anything; files the install touched count as the uninstall will leave
    /// them, all others as they are, and the files the uninstall would leave for later count as kept.
    /// </summary>
    /// <param name="recordId">The install to be replaced.</param>
    /// <param name="replacement">The packages to install in its place.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InvalidPackageException">
    /// A package is malformed or unsafe, or one the install needs was made for a newer app.
    /// </exception>
    /// <exception cref="InstallBlockedException">The install cannot be read or its game folder is gone.</exception>
    Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Puts an install's originals back and removes what it added, leaving alone files changed since, and files its
    /// package's software changed when the game is no longer the version their originals were taken from.
    /// </summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="replacedBy">
    /// The packages that replace the install, whose declared files are handed over to them as they are; null for a plain
    /// uninstall.
    /// </param>
    /// <param name="progress">Reports the uninstall.</param>
    /// <param name="cancellationToken">Stops the uninstall; what it changed by then is undone.</param>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    Task<UninstallResult> UninstallAsync(Guid recordId, IReadOnlyList<string>? replacedBy, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// What keeps an install from being undone for want of its originals: a backup folder that cannot be reached, or files
    /// that hold no original any more until the store brings it back; asked before an update is downloaded.
    /// </summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>A <see cref="BackupFolderUnavailableIssue"/> or an <see cref="OriginalsMissingIssue"/>; empty when nothing is missing.</returns>
    /// <exception cref="InstallBlockedException">The install or its game folder is gone.</exception>
    Task<IReadOnlyList<InstallIssue>> CheckOriginalsAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>Checks an install's files against what it installed.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InstallBlockedException">The game folder is gone.</exception>
    Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Moves an install's backups to where the settings now keep them.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="destination">Where the settings now keep originals.</param>
    /// <param name="progress">Reports each backup.</param>
    /// <param name="cancellationToken">Stops the move, which is then undone.</param>
    /// <returns>The install's record with its new backup folder.</returns>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Points an install at the folder its store moved the game to, when the install's files are there, without changing
    /// anything in the game.
    /// </summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="game">The game as its store reports it now.</param>
    /// <param name="progress">Reports the check of the install's files.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>The install at its new folder; null when the files there are not the install's.</returns>
    /// <exception cref="InstallBlockedException">The install or the new folder cannot be used.</exception>
    Task<InstallRecord?> AdoptMovedAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Forgets, without changing the game, an install whose folder is gone from its attached drive, but never one on a
    /// drive that is unplugged or does not answer in <see cref="LibraryAccess.TimeLimit"/>, which would strand its files.
    /// </summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="keepBackups">
    /// Originals kept outside the game folder stay, listed among the leftover backups; otherwise they go with the record.
    /// </param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InstallBlockedException">An interrupted operation on it has to be recovered first.</exception>
    Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken);

    /// <summary>The installs the app can read.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken);

    /// <summary>The files an install touched.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<InstalledFiles> LoadFilesAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>
    /// The originals that installs forgotten with their game left outside the game folder, and the copies committed
    /// uninstalls kept of files a package's software changed, with their size, so the player can remove them; those whose
    /// folder is not there, or does not answer, are left out until it is back.
    /// </summary>
    /// <param name="cancellationToken">Stops the search.</param>
    Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(CancellationToken cancellationToken);

    /// <summary>Removes the originals a forgotten install left, or the copies an uninstall kept, and the app's list of them.</summary>
    /// <param name="recordId">The forgotten install's record id.</param>
    /// <param name="cancellationToken">Stops the removal before it starts deleting.</param>
    /// <returns>False when there are none, or their folder is not there, which keeps the list for when it is back.</returns>
    Task<bool> RemoveLeftoverBackupsAsync(Guid recordId, CancellationToken cancellationToken);
}

public enum ForgetOutcome
{
    /// <summary>The game folder is there, or may be.</summary>
    Kept,

    Forgotten,

    /// <summary>Forgotten, while the originals it kept outside the game folder stay as leftover backups.</summary>
    ForgottenKeepingBackups,
}
