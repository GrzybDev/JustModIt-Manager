using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class JobFailuresTests
{
    private static readonly (Exception Exception, JobFailureReason Reason)[] Exceptions =
    [
        (new InstallFailedException(rolledBack: false, new IOException("Stuck.")), JobFailureReason.RollbackIncomplete),
        (new InstallFailedException(rolledBack: true, new InvalidOperationException("Odd.")), JobFailureReason.InstallFailed),
        (new InstallFailedException(rolledBack: true, new UnauthorizedAccessException()), JobFailureReason.AccessDenied),
        (new InstallFailedException(rolledBack: true, new IOException("Full.", unchecked((int)0x80070070))), JobFailureReason.NotEnoughSpace),
        (new InstallFailedException(rolledBack: true, new IOException("Full.", unchecked((int)0x80070027))), JobFailureReason.NotEnoughSpace),
        (new InstallFailedException(rolledBack: true, new IOException("Bad sector.")), JobFailureReason.DiskError),
        (new InvalidPackageException(PackageError.UnsupportedFormatVersion), JobFailureReason.AppUpdateRequired),
        (new InvalidPackageException(PackageError.UnsupportedCapability, "repack"), JobFailureReason.AppUpdateRequired),
        (new InvalidPackageException(PackageError.PayloadMismatch), JobFailureReason.InvalidPackage),
        (new PatchException(PatchError.OutputMismatch, "size"), JobFailureReason.InvalidPackage),
        (new PatchException(PatchError.EngineUnavailable, "missing"), JobFailureReason.Unexpected),
        (new CatalogueException(CatalogueErrorReason.NotFound, "Gone."), JobFailureReason.ProjectNotFound),
        (new CatalogueException(CatalogueErrorReason.ReleaseUnavailable, "Hidden."), JobFailureReason.ReleaseUnavailable),
        (new CatalogueException(CatalogueErrorReason.CorruptDownload, "Wrong bytes."), JobFailureReason.DownloadCorrupt),
        (new CatalogueException(CatalogueErrorReason.InsufficientSpace, "Full."), JobFailureReason.NotEnoughSpace),
        (new CatalogueException(CatalogueErrorReason.Maintenance, "Later."), JobFailureReason.Offline),
        (new CatalogueException(CatalogueErrorReason.Forbidden, "No."), JobFailureReason.ServerError),
        (new UnauthorizedAccessException(), JobFailureReason.AccessDenied),
        (new InvalidOperationException(), JobFailureReason.Unexpected),
    ];

    [Fact]
    public void Maps_what_stopped_a_job_to_a_reason()
    {
        foreach (var (exception, reason) in Exceptions)
        {
            JobFailures.From(exception).Reason.Should().Be(reason, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    [Fact]
    public void Keeps_the_catalogue_error()
    {
        JobFailures.From(new CatalogueException(CatalogueErrorReason.RateLimited, "Slow down.")).CatalogueError
            .Should().Be(CatalogueErrorReason.RateLimited);
    }

    [Fact]
    public void A_download_without_space_carries_the_sizes_and_the_place()
    {
        var full = new CatalogueException(CatalogueErrorReason.InsufficientSpace, "Full.")
        {
            RequiredBytes = 2048,
            AvailableBytes = 1024,
            Location = @"D:\Downloads",
        };

        JobFailures.From(full).Should().BeEquivalentTo(new JobFailure(JobFailureReason.NotEnoughSpace)
        {
            CatalogueError = CatalogueErrorReason.InsufficientSpace,
            RequiredBytes = 2048,
            AvailableBytes = 1024,
            Location = @"D:\Downloads",
        });
    }

    [Fact]
    public void A_running_game_comes_first_with_each_process_once()
    {
        var failure = JobFailures.FromIssues([
            new MissingFileIssue("data/strings.pak"),
            new GameRunningIssue("ExampleGame"),
            new GameRunningIssue("examplegame"),
            new GameNotReadyIssue(GameInstallState.Updating, GameStateReason.StoreBusy),
        ]);

        failure.Reason.Should().Be(JobFailureReason.GameRunning);
        failure.Processes.Should().Equal("ExampleGame");
    }

    [Fact]
    public void A_game_that_is_not_ready_carries_its_state()
    {
        JobFailures.FromIssues([new FileLockedIssue("a", ["Viewer"]), new GameNotReadyIssue(GameInstallState.Broken, GameStateReason.FilesMissing)])
            .Should().BeEquivalentTo(new JobFailure(JobFailureReason.GameNotReady)
            {
                GameState = GameInstallState.Broken,
                StateReason = GameStateReason.FilesMissing,
            });
    }

    [Fact]
    public void Files_of_another_version_come_before_files_in_use_and_list_the_files()
    {
        var failure = JobFailures.FromIssues([
            new FileLockedIssue("bin/game.exe", ["Viewer"]),
            new FileMismatchIssue("data/strings.pak", AlreadyPatched: false),
            new MissingFileIssue("data/fonts.pak"),
            new PathConflictIssue("movies"),
        ]);

        failure.Reason.Should().Be(JobFailureReason.UnsupportedGameVersion);
        failure.Files.Should().Equal("data/strings.pak", "data/fonts.pak", "movies");
    }

    [Fact]
    public void A_package_already_applied_by_hand_is_told_apart()
    {
        JobFailures.FromIssues([new FileMismatchIssue("data/strings.pak", AlreadyPatched: true)])
            .Should().BeEquivalentTo(new JobFailure(JobFailureReason.AlreadyApplied) { Files = ["data/strings.pak"] });
    }

    [Fact]
    public void Locked_files_name_the_files_and_the_programs_holding_them()
    {
        var failure = JobFailures.FromIssues([new FileLockedIssue("a.pak", ["Viewer", "Editor"]), new FileLockedIssue("b.pak", ["Viewer"])]);

        failure.Reason.Should().Be(JobFailureReason.FilesLocked);
        failure.Files.Should().Equal("a.pak", "b.pak");
        failure.Processes.Should().Equal("Viewer", "Editor");
    }

    [Fact]
    public void Missing_space_carries_the_sizes_and_the_place()
    {
        JobFailures.FromIssues([new NotEnoughSpaceIssue("D:\\Games", 2048, 1024)])
            .Should().BeEquivalentTo(new JobFailure(JobFailureReason.NotEnoughSpace)
            {
                RequiredBytes = 2048,
                AvailableBytes = 1024,
                Location = "D:\\Games",
            });
    }

    [Fact]
    public void An_unavailable_backup_folder_names_the_folder()
    {
        JobFailures.FromIssues([new FileLockedIssue("a.pak", []), new BackupFolderUnavailableIssue("E:\\Backups")])
            .Should().BeEquivalentTo(new JobFailure(JobFailureReason.BackupFolderUnavailable) { Location = "E:\\Backups" });
    }

    [Fact]
    public void A_game_another_app_manages_names_the_app_and_its_folder()
    {
        JobFailures.FromIssues([new FileLockedIssue("a.pak", []), new GameManagedByOtherAppIssue("OtherApp", @"D:\Games\Example\.other-app")])
            .Should().BeEquivalentTo(new JobFailure(JobFailureReason.ManagedByOtherApp)
            {
                OtherApp = "OtherApp",
                Location = @"D:\Games\Example\.other-app",
            });
    }

    [Fact]
    public void A_package_for_another_game_comes_before_the_files_it_does_not_fit()
    {
        JobFailures.FromIssues([
            new MissingFileIssue("data/fonts.pak"),
            new FileOwnedByOtherInstallIssue("readme.txt", Guid.NewGuid()),
            new OtherGameIssue([new PackageTarget(StoreId.Steam, "3000")]),
        ]).Reason.Should().Be(JobFailureReason.OtherGame);
    }

    [Fact]
    public void Every_kind_of_install_issue_has_a_reason_of_its_own()
    {
        var issues = typeof(InstallIssue).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(InstallIssue)) && !type.IsAbstract)
            .ToList();

        issues.Should().NotBeEmpty();
        foreach (var type in issues)
        {
            JobFailures.FromIssues([Example(type)]).Reason.Should().NotBe(JobFailureReason.Unexpected, $"{type.Name} needs a reason");
        }
    }

    private static InstallIssue Example(Type type) => type.Name switch
    {
        nameof(GameFolderMissingIssue) => new GameFolderMissingIssue("folder"),
        nameof(GameBusyIssue) => new GameBusyIssue(),
        nameof(GameRunningIssue) => new GameRunningIssue("ExampleGame"),
        nameof(GameNotReadyIssue) => new GameNotReadyIssue(GameInstallState.Updating, GameStateReason.StoreBusy),
        nameof(GameNotInstalledIssue) => new GameNotInstalledIssue(),
        nameof(RecoveryPendingIssue) => new RecoveryPendingIssue(Guid.NewGuid()),
        nameof(InstallNotFoundIssue) => new InstallNotFoundIssue(Guid.NewGuid()),
        nameof(UnreadableInstallIssue) => new UnreadableInstallIssue(Guid.NewGuid()),
        nameof(FileOwnedByOtherInstallIssue) => new FileOwnedByOtherInstallIssue("a.pak", Guid.NewGuid()),
        nameof(GameManagedByOtherAppIssue) => new GameManagedByOtherAppIssue("OtherApp", "folder"),
        nameof(UnsafePathIssue) => new UnsafePathIssue("../a"),
        nameof(PathConflictIssue) => new PathConflictIssue("a"),
        nameof(MissingFileIssue) => new MissingFileIssue("a"),
        nameof(FileMismatchIssue) => new FileMismatchIssue("a", AlreadyPatched: false),
        nameof(OtherGameIssue) => new OtherGameIssue([new PackageTarget(StoreId.Steam, "3000")]),
        nameof(FileLockedIssue) => new FileLockedIssue("a", []),
        nameof(PermissionRequiredIssue) => new PermissionRequiredIssue("a"),
        nameof(NotEnoughSpaceIssue) => new NotEnoughSpaceIssue("folder", 2, 1),
        nameof(BackupFolderUnavailableIssue) => new BackupFolderUnavailableIssue("folder"),
        _ => throw new InvalidOperationException($"{type.Name} needs an example here."),
    };
}
