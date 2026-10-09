using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>A package downloaded for a job, with where it came from.</summary>
/// <param name="Path">The package file.</param>
/// <param name="FileId">The release file, kept in the download cache while a retry may need it.</param>
/// <param name="ReleaseId">The release the file belongs to.</param>
/// <param name="Source">What the install's record keeps of where it came from.</param>
internal sealed record PreparedPackage(string Path, Guid FileId, Guid ReleaseId, InstallSource Source);

/// <summary>The steps of every kind of job. Nothing in a game changes before its package is downloaded and checked.</summary>
internal sealed partial class JobPipeline(
    IInstallEngine engine,
    ICatalogue catalogue,
    IPackageDownloader downloader,
    ILibraryService library,
    IRunningGameDetector runningGames,
    ISettingsService settings,
    SemaphoreSlim downloadSlot,
    TimeProvider time,
    ILogger logger,
    CancellationToken halting)
{
    /// <summary>How long a job waits for a busy store before it looks again, in case no change of the library is reported.</summary>
    public static readonly TimeSpan StoreBusyRecheck = TimeSpan.FromMinutes(1);

    /// <summary>The pause after the engine found the game in use although the job's own look had not.</summary>
    public static readonly TimeSpan InUseRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Runs the steps of the job's kind.</summary>
    /// <param name="job">The job, whose token cancels it.</param>
    /// <returns>How the job ended; most failures, a cancellation and a deferral throw instead.</returns>
    public Task<JobResult> RunAsync(JobContext job) => job.Request switch
    {
        InstallJobRequest install => InstallAsync(job, install),
        UninstallJobRequest uninstall => UninstallAsync(job, uninstall.Record),
        ReinstallJobRequest reinstall => ReinstallAsync(job, reinstall.Record),
        TestInstallJobRequest test => TestInstallAsync(job, test),
        MoveBackupsJobRequest move => MoveBackupsAsync(job, move),
        FollowMoveJobRequest follow => FollowMoveAsync(job, follow.Record),
        _ => throw new ArgumentException($"Unknown job request {job.Request.GetType().Name}.", nameof(job)),
    };

    private async Task<JobResult> InstallAsync(JobContext job, InstallJobRequest request)
    {
        // Looking first means a game the store is not done with fails before anything is downloaded. Once the package is
        // downloaded, even an automatic job waits rather than leave it to be downloaded, and counted, again.
        var game = await WaitUntilSafeAsync(job, job.Origin == JobOrigin.Automatic, job.Token).ConfigureAwait(false);
        await EnsureNoOtherAppAsync(game, job.Token).ConfigureAwait(false);
        var package = await PrepareAsync(job, game, request.Project, request.VariantName, fallBackToDefault: false).ConfigureAwait(false);
        var installed = await InstallPackageAsync(job, package.Path, package.Source, isTest: false, canDefer: false, job.Token)
            .ConfigureAwait(false);
        return job.Succeeded() with { Installed = installed.Record, Warnings = installed.Warnings };
    }

    private async Task<JobResult> TestInstallAsync(JobContext job, TestInstallJobRequest request)
    {
        var installed = await InstallPackageAsync(job, request.PackagePath, request.Source, isTest: true, canDefer: false, job.Token)
            .ConfigureAwait(false);
        return job.Succeeded() with { Installed = installed.Record, Warnings = installed.Warnings };
    }

    private async Task<JobResult> UninstallAsync(JobContext job, InstallRecord record)
    {
        job.Enter(JobStage.Checking);
        // A game no longer detected in the install's folder has no store state to wait for; the engine still refuses
        // while anything runs from the folder.
        var detected = library.Snapshot.Find(record.Game) is { } game && PathComparison.AreSame(game.InstallDirectory, record.InstallDirectory);
        try
        {
            var result = await WhenSafeAsync(job, detected, canDefer: false,
                _ => engine.UninstallAsync(record.Id, job.InstallProgress, job.Token), job.Token).ConfigureAwait(false);
            return job.Succeeded() with { KeptFiles = result.KeptFiles, RemovedRecord = record.Id };
        }
        catch (InstallBlockedException blocked) when (blocked.Issues.All(issue => issue is GameFolderMissingIssue))
        {
            if (await engine.ForgetIfGoneAsync(record.Id, keepBackups: false, job.Token).ConfigureAwait(false) == ForgetOutcome.Kept)
            {
                throw;
            }

            LogForgotten(record.Game);
            return job.Succeeded() with { Forgotten = true, RemovedRecord = record.Id };
        }
    }

    private async Task<JobResult> ReinstallAsync(JobContext job, InstallRecord record)
    {
        var project = InstallSources.ProjectOf(record.Source)
            ?? throw new JobFailedException(new JobFailure(JobFailureReason.ProjectNotFound));
        if (Guid.TryParse(record.Source?.ReleaseFileId, out var installedFile))
        {
            job.Protect(installedFile);
        }

        var game = await WaitUntilSafeAsync(job, job.Origin == JobOrigin.Automatic, job.Token).ConfigureAwait(false);
        await EnsureNoOtherAppAsync(game, job.Token).ConfigureAwait(false);
        // Downloading first means a missing connection or package leaves the installed release as it is. Only the player
        // may end up with another variant, e.g. text only for a voice-over the new release does not have yet.
        var package = await PrepareAsync(job, game, project, record.Source?.VariantName, fallBackToDefault: job.Origin == JobOrigin.User)
            .ConfigureAwait(false);
        job.Enter(JobStage.Checking);
        var removed = await WhenSafeAsync(job, detected: true, canDefer: false, async current =>
        {
            await EnsureReplaceableAsync(job, record, package, current!).ConfigureAwait(false);
            return await engine.UninstallAsync(record.Id, job.InstallProgress, job.Token).ConfigureAwait(false);
        }, job.Token).ConfigureAwait(false);
        try
        {
            var installed = await InstallPackageAsync(job, package.Path, package.Source, isTest: false, canDefer: false, job.Token)
                .ConfigureAwait(false);
            return job.Succeeded() with
            {
                Installed = installed.Record,
                Warnings = installed.Warnings,
                KeptFiles = removed.KeptFiles,
                RemovedRecord = record.Id,
            };
        }
        catch (Exception exception)
        {
            // Not everything can be foreseen, e.g. a file locked meanwhile, and the player may cancel; either way the player
            // must not lose the release that worked.
            LogReplacementFailed(record.Game, exception.GetType().Name);
            var restored = await TryRestoreAsync(job, record).ConfigureAwait(false);
            var cancelled = exception is OperationCanceledException && job.Token.IsCancellationRequested;
            return (cancelled ? job.Result(JobOutcome.Cancelled) : job.Result(JobOutcome.Failed, JobFailures.From(exception))) with
            {
                RemovedPrevious = true,
                RestoredPrevious = restored is not null,
                Installed = restored,
                RemovedRecord = record.Id,
            };
        }
    }

    private async Task<JobResult> MoveBackupsAsync(JobContext job, MoveBackupsJobRequest request)
    {
        job.Enter(JobStage.MovingBackups);
        var moved = await engine.MoveBackupsAsync(request.Record.Id, request.Destination, job.InstallProgress, job.Token)
            .ConfigureAwait(false);
        return job.Succeeded() with { Installed = moved };
    }

    // The result has no Installed when the install's files are not in the game's new folder.
    private async Task<JobResult> FollowMoveAsync(JobContext job, InstallRecord record)
    {
        var game = await WaitUntilSafeAsync(job, canDefer: true, job.Token).ConfigureAwait(false);
        job.Enter(JobStage.Verifying);
        var adopted = await engine.AdoptMovedAsync(record.Id, game, job.InstallProgress, job.Token).ConfigureAwait(false);
        return job.Succeeded() with { Installed = adopted };
    }

    // Returns the game as a fresh look describes it. With canDefer, an automatic job that has changed nothing yet ends
    // instead of waiting, possibly for hours, for a game that is in use.
    private async Task<InstalledGame> WaitUntilSafeAsync(JobContext job, bool canDefer, CancellationToken cancellationToken)
    {
        var skewLogged = false;
        while (true)
        {
            using var changed = new LibraryChangeSignal(library);
            var probe = await library.ProbeAsync(job.Game, cancellationToken).ConfigureAwait(false);
            var verdict = SafetyGate.Check(probe, job.Kind, time.GetUtcNow());
            if (verdict.ClockSkewIgnored && !skewLogged)
            {
                skewLogged = true;
                LogClockSkew(job.Game);
            }

            if (verdict.Status == GateStatus.Ready)
            {
                job.Resume();
                return probe.Game!;
            }

            if (verdict.Status == GateStatus.Fail)
            {
                throw new JobFailedException(verdict.Failure!);
            }

            var reason = verdict.WaitReason!.Value;
            if (canDefer && reason is JobWaitReason.GameRunning or JobWaitReason.StoreBusy)
            {
                throw new JobDeferredException(reason, verdict.Processes);
            }

            job.Wait(reason, [.. verdict.Processes.Select(process => process.Name).Distinct(StringComparer.OrdinalIgnoreCase)]);
            await (reason switch
            {
                JobWaitReason.GameRunning when verdict.Processes.Count > 0 =>
                    runningGames.WaitForExitAsync(verdict.Processes, cancellationToken),
                JobWaitReason.QuietPeriod => WaitAnyAsync(changed.Task, verdict.WaitUntil!.Value - time.GetUtcNow(), cancellationToken),
                _ => WaitAnyAsync(changed.Task, StoreBusyRecheck, cancellationToken),
            }).ConfigureAwait(false);
        }
    }

    // A game not detected where the operation works has nothing to wait for.
    private async Task<T> WhenSafeAsync<T>(JobContext job, bool detected, bool canDefer, Func<InstalledGame?, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var game = detected ? await WaitUntilSafeAsync(job, canDefer, cancellationToken).ConfigureAwait(false) : null;
            try
            {
                return await operation(game).ConfigureAwait(false);
            }
            catch (InstallBlockedException blocked) when (detected && SafetyGate.IsOnlyInUse(blocked.Issues))
            {
                // The game started, or the store began working on it, between the job's look and the engine's own.
                LogInUse(job.Game);
                await Task.Delay(InUseRetryDelay, time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // A game another app manages would refuse the package, so it is not downloaded, and counted, in vain.
    private async Task EnsureNoOtherAppAsync(InstalledGame game, CancellationToken cancellationToken)
    {
        if (await engine.FindOtherAppsAsync(game.InstallDirectory, cancellationToken).ConfigureAwait(false) is { Count: > 0 } others)
        {
            throw new InstallBlockedException(others);
        }
    }

    private async Task<PreparedPackage> PrepareAsync(JobContext job, InstalledGame game, ProjectLocator project, string? variantName,
        bool fallBackToDefault)
    {
        job.Enter(JobStage.Preparing);
        // The release and file ids must be the site's current ones.
        var page = await catalogue.GetProjectPageAsync(project, PageFreshness.Fresh, job.Token).ConfigureAwait(false);
        if (page is null or { IsGone: true })
        {
            throw new JobFailedException(new JobFailure(JobFailureReason.ProjectNotFound));
        }

        var detail = page.Page;
        var selection = ReleaseSelector.Select(detail, game.Key, game.Platform);
        var variant = Choose(selection, variantName, fallBackToDefault)
            ?? throw new JobFailedException(new JobFailure(JobFailureReason.NoPackage) { Unavailability = UnavailabilityOf(selection, variantName) });
        var release = selection.Release!;
        var file = variant.Package!;
        job.Protect(file.Uuid);
        job.PackageFileId = file.Uuid;
        job.ReleaseId = release.Uuid;
        var downloaded = await DownloadAsync(job, PackageRequest.For(new ReleaseFileLocator(detail.Team.Slug, detail.Slug, release.Uuid,
            file.Uuid), file)).ConfigureAwait(false);
        return new PreparedPackage(downloaded.Path, file.Uuid, release.Uuid, new InstallSource
        {
            ProjectId = detail.Uuid.ToString("D"),
            ProjectSlug = detail.Slug,
            ProjectName = detail.Name,
            GameSlug = detail.Game.Slug,
            GameName = detail.Game.Name,
            TeamSlug = detail.Team.Slug,
            TeamName = detail.Team.Name,
            ReleaseId = release.Uuid.ToString("D"),
            ReleaseLabel = release.NameRaw.Length > 0 ? release.NameRaw : null,
            ReleaseVersion = release.Version > 0 ? release.Version : null,
            ReleasePublishedAt = release.PublishedAt,
            ReleaseFileId = file.Uuid.ToString("D"),
            VariantName = variant.Name,
            CoverUrl = CoverOf(detail),
        });
    }

    private async Task<DownloadedPackage> DownloadAsync(JobContext job, PackageRequest request)
    {
        // A kept package is handed out without waiting for another game's download.
        if (await downloader.FindCachedAsync(request, job.Token).ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        if (!await downloadSlot.WaitAsync(0, job.Token).ConfigureAwait(false))
        {
            job.Wait(JobWaitReason.Download, []);
            await downloadSlot.WaitAsync(job.Token).ConfigureAwait(false);
        }

        try
        {
            job.Enter(JobStage.Downloading);
            return await downloader.DownloadAsync(request, job.DownloadProgress, job.Token).ConfigureAwait(false);
        }
        finally
        {
            downloadSlot.Release();
        }
    }

    private Task<InstallResult> InstallPackageAsync(JobContext job, string packagePath, InstallSource? source, bool isTest, bool canDefer,
        CancellationToken cancellationToken)
    {
        job.Enter(JobStage.Checking);
        return WhenSafeAsync(job, detected: true, canDefer,
            game => engine.InstallAsync(new InstallRequest(game!, packagePath, BackupChoice.From(settings.Current), source, isTest),
                job.InstallProgress, cancellationToken),
            cancellationToken);
    }

    // Uninstalling a release that the new package cannot replace would leave the player with none.
    private async Task EnsureReplaceableAsync(JobContext job, InstallRecord record, PreparedPackage package, InstalledGame game)
    {
        var report = await engine.PredictReplacementAsync(record.Id,
            new InstallRequest(game, package.Path, BackupChoice.From(settings.Current), package.Source), job.InstallProgress, job.Token)
            .ConfigureAwait(false);
        if (report.CanProceed)
        {
            return;
        }

        if (SafetyGate.IsOnlyInUse(report.Issues))
        {
            throw new InstallBlockedException(report.Issues);
        }

        var failure = JobFailures.FromIssues(report.Issues);
        var isNewRelease = !Guid.TryParse(record.Source?.ReleaseId, out var installedRelease) || installedRelease != package.ReleaseId;
        throw new JobFailedException(failure.Reason == JobFailureReason.UnsupportedGameVersion && isNewRelease
            ? failure with { Reason = JobFailureReason.NewReleaseIncompatible }
            : failure);
    }

    // The player's cancel does not stop this, since a job cancelled half-way must still end with the release it started
    // from; only an exit without waiting for jobs does.
    private async Task<InstallRecord?> TryRestoreAsync(JobContext job, InstallRecord record)
    {
        if (record.Source is not { TeamSlug: { } team, ProjectSlug: { } project } source
            || !Guid.TryParse(source.ReleaseId, out var release) || !Guid.TryParse(source.ReleaseFileId, out var file))
        {
            return null;
        }

        job.Enter(JobStage.RestoringPrevious);
        try
        {
            var path = await FindPreviousPackageAsync(new ReleaseFileLocator(team, project, release, file), record.Package.Kind)
                .ConfigureAwait(false);
            var restored = await InstallPackageAsync(job, path, source, isTest: false, canDefer: false, halting).ConfigureAwait(false);
            LogRestored(record.Game);
            return restored.Record;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !halting.IsCancellationRequested)
        {
            LogRestoreFailed(record.Game, exception);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // The package of an install is kept for exactly this, so a download here is rare; it still has to stay bounded.
    private async Task<string> FindPreviousPackageAsync(ReleaseFileLocator locator, PackageKind kind)
    {
        // The kind comes from the content, the cached file's name from the site's format, and the two may differ.
        PackageFormat[] formats = kind == PackageKind.Manifest
            ? [PackageFormat.Manifest, PackageFormat.Zip]
            : [PackageFormat.Zip, PackageFormat.Manifest];
        foreach (var format in formats)
        {
            if (await downloader.FindCachedAsync(new PackageRequest(locator, format, null), halting).ConfigureAwait(false) is { } cached)
            {
                return cached.Path;
            }
        }

        return (await downloader.DownloadAsync(new PackageRequest(locator, formats[0], null), null, halting).ConfigureAwait(false)).Path;
    }

    private static ReleaseVariant? Choose(ReleaseSelection selection, string? variantName, bool fallBackToDefault) =>
        variantName is null
            ? selection.Default
            : selection.FindInstallable(variantName) ?? (fallBackToDefault ? selection.Default : null);

    private static SelectionUnavailability UnavailabilityOf(ReleaseSelection selection, string? variantName) =>
        selection.Unavailable
        ?? selection.Variants.FirstOrDefault(variant => variant.Name == variantName)?.Unavailable
        ?? SelectionUnavailability.NoReadyFiles;

    private static Uri? CoverOf(ProjectPage page) =>
        Uri.TryCreate(page.CoverImage.Length > 0 ? page.CoverImage : page.Game.Cover, UriKind.Absolute, out var cover)
        && cover.Scheme == Uri.UriSchemeHttps
            ? cover
            : null;

    private async Task WaitAnyAsync(Task signal, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var elapsed = Task.Delay(timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero, time, waiting.Token);
        await Task.WhenAny(signal, elapsed).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The folder of an install on {Game} is gone for good, so the install was forgotten")]
    private partial void LogForgotten(GameKey game);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replacing the install on {Game} failed ({Error}); putting the previous release back")]
    private partial void LogReplacementFailed(GameKey game, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "The previous release of {Game} was put back")]
    private partial void LogRestored(GameKey game);

    [LoggerMessage(Level = LogLevel.Error, Message = "The previous release of {Game} could not be put back")]
    private partial void LogRestoreFailed(GameKey game, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The store's last change to {Game} lies far in the future; the quiet period is skipped")]
    private partial void LogClockSkew(GameKey game);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Game} came into use right before it was to change; waiting again")]
    private partial void LogInUse(GameKey game);
}

/// <summary>Completes on the library's next change, e.g. when the store finished what the job waits for.</summary>
internal sealed class LibraryChangeSignal : IDisposable
{
    private readonly ILibraryService _library;
    private readonly TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts listening for the library's next change.</summary>
    /// <param name="library">The library to listen to.</param>
    public LibraryChangeSignal(ILibraryService library)
    {
        _library = library;
        _library.Changed += OnChanged;
    }

    public Task Task => _changed.Task;

    public void Dispose() => _library.Changed -= OnChanged;

    private void OnChanged(object? sender, LibraryChangedEventArgs e) => _changed.TrySetResult();
}
