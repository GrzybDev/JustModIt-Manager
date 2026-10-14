using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>Ends a job with a typed failure.</summary>
/// <param name="failure">Why the job failed.</param>
internal sealed class JobFailedException(JobFailure failure) : Exception($"The job failed: {failure.Reason}.")
{
    public JobFailure Failure { get; } = failure;
}

/// <summary>An automatic job met a game in use and ends without having changed anything, to be tried once it is free.</summary>
/// <param name="reason">What the game is busy with.</param>
/// <param name="processes">What runs from the game's folder, whose exit the next try waits for; empty otherwise.</param>
internal sealed class JobDeferredException(JobWaitReason reason, ValueList<RunningProcess> processes)
    : Exception($"The job was deferred: {reason}.")
{
    public JobWaitReason Reason { get; } = reason;

    public ValueList<RunningProcess> Processes { get; } = processes;
}

/// <summary>Turns what stopped a job into a typed reason the app words in its own language.</summary>
internal static class JobFailures
{
    /// <summary>The typed failure for what stopped a job.</summary>
    /// <param name="exception">What stopped the job.</param>
    public static JobFailure From(Exception exception) => exception switch
    {
        JobFailedException failed => failed.Failure,
        InstallBlockedException blocked => FromIssues(blocked.Issues),
        InstallFailedException { RolledBack: false } => new JobFailure(JobFailureReason.RollbackIncomplete),
        InstallFailedException { InnerException: { } inner } when From(inner).Reason != JobFailureReason.Unexpected => From(inner),
        InstallFailedException => new JobFailure(JobFailureReason.InstallFailed),
        InvalidPackageException invalid => new JobFailure(invalid.Error.NeedsNewerApp()
            ? JobFailureReason.AppUpdateRequired
            : JobFailureReason.InvalidPackage),
        PatchException { Error: PatchError.EngineUnavailable } => new JobFailure(JobFailureReason.Unexpected),
        PatchException => new JobFailure(JobFailureReason.InvalidPackage),
        CatalogueException catalogue => FromCatalogue(catalogue),
        UnauthorizedAccessException => new JobFailure(JobFailureReason.AccessDenied),
        IOException io when IOErrors.IsDiskFull(io) => new JobFailure(JobFailureReason.NotEnoughSpace),
        IOException => new JobFailure(JobFailureReason.DiskError),
        _ => new JobFailure(JobFailureReason.Unexpected),
    };

    /// <summary>
    /// The failure the player has to deal with first among the problems the engine found: the game's state, then what
    /// keeps the install from being touched at all, then whether the package fits the game, then what a retry fixes.
    /// </summary>
    /// <param name="issues">Every problem the engine found.</param>
    public static JobFailure FromIssues(IReadOnlyList<InstallIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.OfType<GameRunningIssue>().ToList() is { Count: > 0 } running)
        {
            return new JobFailure(JobFailureReason.GameRunning) { Processes = Distinct(running.Select(issue => issue.ProcessName)) };
        }

        if (issues.OfType<GameNotReadyIssue>().FirstOrDefault() is { } notReady)
        {
            return new JobFailure(JobFailureReason.GameNotReady) { GameState = notReady.State, StateReason = notReady.Reason };
        }

        if (issues.OfType<GameNotInstalledIssue>().Any())
        {
            return new JobFailure(JobFailureReason.GameNotDetected);
        }

        if (issues.OfType<GameFolderMissingIssue>().FirstOrDefault() is { } missing)
        {
            return new JobFailure(JobFailureReason.GameFolderMissing) { Location = missing.Directory };
        }

        if (issues.OfType<BackupFolderUnavailableIssue>().FirstOrDefault() is { } backups)
        {
            return new JobFailure(JobFailureReason.BackupFolderUnavailable) { Location = backups.Directory };
        }

        if (issues.OfType<GameManagedByOtherAppIssue>().FirstOrDefault() is { } managed)
        {
            return new JobFailure(JobFailureReason.ManagedByOtherApp) { OtherApp = managed.App, Location = managed.Directory };
        }

        var simple = issues.Select(issue => issue switch
        {
            GameBusyIssue => JobFailureReason.GameBusy,
            RecoveryPendingIssue => JobFailureReason.RecoveryPending,
            InstallNotFoundIssue => JobFailureReason.InstallNotFound,
            UnreadableInstallIssue => JobFailureReason.UnreadableInstall,
            UnsafePathIssue => JobFailureReason.InvalidPackage,
            _ => (JobFailureReason?)null,
        }).FirstOrDefault(reason => reason is not null);
        if (simple is { } reason)
        {
            return new JobFailure(reason);
        }

        if (issues.OfType<OtherGameIssue>().Any())
        {
            return new JobFailure(JobFailureReason.OtherGame);
        }

        if (issues.OfType<FileOwnedByOtherInstallIssue>().ToList() is { Count: > 0 } owned)
        {
            return new JobFailure(JobFailureReason.ConflictingInstall) { Files = Distinct(owned.Select(issue => issue.Path)) };
        }

        if (issues.OfType<KeptFileIssue>().ToList() is { Count: > 0 } kept)
        {
            return new JobFailure(JobFailureReason.KeptFilesPending) { Files = Distinct(kept.Select(issue => issue.Path)) };
        }

        var incompatible = issues.Where(issue => issue is MissingFileIssue or PathConflictIssue or FileMismatchIssue { AlreadyPatched: false })
            .ToList();
        if (incompatible.Count > 0)
        {
            return new JobFailure(JobFailureReason.UnsupportedGameVersion) { Files = Distinct(incompatible.Select(PathOf)) };
        }

        if (issues.OfType<FileMismatchIssue>().ToList() is { Count: > 0 } applied)
        {
            return new JobFailure(JobFailureReason.AlreadyApplied) { Files = Distinct(applied.Select(issue => issue.Path)) };
        }

        if (issues.OfType<FileLockedIssue>().ToList() is { Count: > 0 } locked)
        {
            return new JobFailure(JobFailureReason.FilesLocked)
            {
                Files = Distinct(locked.Select(issue => issue.Path)),
                Processes = Distinct(locked.SelectMany(issue => issue.Holders)),
            };
        }

        if (issues.OfType<PermissionRequiredIssue>().ToList() is { Count: > 0 } denied)
        {
            return new JobFailure(JobFailureReason.PermissionRequired) { Files = Distinct(denied.Select(issue => issue.Path)) };
        }

        if (issues.OfType<NotEnoughSpaceIssue>().FirstOrDefault() is { } space)
        {
            return new JobFailure(JobFailureReason.NotEnoughSpace)
            {
                RequiredBytes = space.RequiredBytes,
                AvailableBytes = space.AvailableBytes,
                Location = space.Location,
            };
        }

        return new JobFailure(JobFailureReason.Unexpected);
    }

    /// <summary>The typed failure for an error of the site or its storage.</summary>
    /// <param name="exception">The catalogue's or the download's error.</param>
    public static JobFailure FromCatalogue(CatalogueException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new JobFailure(exception switch
        {
            { Reason: CatalogueErrorReason.NotFound } => JobFailureReason.ProjectNotFound,
            { Reason: CatalogueErrorReason.ReleaseUnavailable } => JobFailureReason.ReleaseUnavailable,
            { Reason: CatalogueErrorReason.CorruptDownload } => JobFailureReason.DownloadCorrupt,
            { Reason: CatalogueErrorReason.InsufficientSpace } => JobFailureReason.NotEnoughSpace,
            { IsTransient: true } => JobFailureReason.Offline,
            _ => JobFailureReason.ServerError,
        })
        {
            CatalogueError = exception.Reason,
            RequiredBytes = exception.RequiredBytes,
            AvailableBytes = exception.AvailableBytes,
            Location = exception.Location,
        };
    }

    private static string PathOf(InstallIssue issue) => issue switch
    {
        MissingFileIssue missing => missing.Path,
        PathConflictIssue conflict => conflict.Path,
        FileMismatchIssue mismatch => mismatch.Path,
        _ => "",
    };

    private static ValueList<string> Distinct(IEnumerable<string> values) => [.. values.Distinct(StringComparer.OrdinalIgnoreCase)];
}
