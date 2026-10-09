using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Protocol.Tests.Views;

/// <summary>An invented app state: one installed game with an update on offer and a job, one game that failed.</summary>
internal static class TestStates
{
    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public static readonly GameKey Game = new(StoreId.Steam, "1000");

    public static readonly GameKey FailedGame = new(StoreId.Gog, "1207658924000000001");

    public static readonly Guid Project = Guid.Parse("11111111-0000-4000-8000-000000000001");

    public static readonly Guid ReleaseTwo = Guid.Parse("22222222-0000-4000-8000-000000000002");

    public static readonly Guid FileTwo = Guid.Parse("33333333-0000-4000-8000-000000000002");

    public static readonly Guid RecordId = Guid.Parse("88888888-0000-4000-8000-000000000001");

    public static readonly Guid Job = Guid.Parse("99999999-0000-4000-8000-000000000001");

    private static readonly ReleaseFile PackageFile = new()
    {
        Uuid = FileTwo,
        Name = "Text only",
        Status = "ready",
        RequiresManager = true,
        DownloadFilename = "example-project.examplemod",
        Size = 4096,
    };

    public static ReleaseVariant Variant { get; } = new()
    {
        Name = "Text only",
        Files = [PackageFile],
        Package = PackageFile,
    };

    public static ProjectOffer Offer { get; } = new(
        new IndexEntry
        {
            Game = new GameSummary
            {
                Uuid = Guid.Parse("66666666-0000-4000-8000-000000000001"),
                Slug = "example-game",
                Name = "Example Game",
                Cover = "https://images.example.com/covers/example-game.jpg",
            },
            ProjectUuid = Project,
            ProjectSlug = "example-project",
            ProjectName = "Example Project",
            Team = new TeamReference { Uuid = Guid.Parse("77777777-0000-4000-8000-000000000001"), Slug = "lantern-team", Name = "Lantern Team" },
            Status = new ProjectStatus { Uuid = Guid.Parse("44444444-0000-4000-8000-000000000009"), Name = "Finished", IsCompleted = true },
            Types = [new ProjectType { Uuid = Guid.Parse("44444444-0000-4000-8000-000000000008"), Name = "Text" }],
            UpdatedAt = Now,
        },
        new ReleaseSelection
        {
            Release = new Release { Uuid = ReleaseTwo, Version = 2, Name = "Autumn patch", FirstPublishedAt = Now.AddDays(-1) },
            Links = [],
            Variants = [Variant, new ReleaseVariant { Name = "Text and voice", Files = [], Unavailable = SelectionUnavailability.OtherSystems }],
            Default = Variant,
        });

    public static AppSettings Settings { get; } = AppSettings.Default
        .ChooseAutoUpdate(Game, false)
        .WithLibraryFolder(new LibraryFolder(StoreId.Gog, @"D:\GOG Games")) with
    {
        OnboardingCompleted = true,
        BackupLocation = BackupLocation.CustomFolder,
        CustomBackupFolder = @"E:\Backups",
    };

    public static JobSnapshot RunningJob { get; } = new()
    {
        Id = Job,
        Game = Game,
        GameName = "Example Game",
        Kind = JobKind.Update,
        Origin = JobOrigin.Automatic,
        Stage = JobStage.Waiting,
        WaitReason = JobWaitReason.GameRunning,
        RunningProcesses = ["example.exe"],
        Fraction = 0.25,
        BytesDone = 256,
        BytesTotal = 1024,
        Sequence = 7,
    };

    public static AppState State { get; } = new()
    {
        Version = 12,
        IsLoaded = true,
        Games =
        [
            new MyGame
            {
                Key = Game,
                Name = "Example Game",
                Status = new MyGameStatus(MyGameState.Busy)
                {
                    Update = new UpdateCheck(UpdateState.Available, UpdateKind.NewRelease, Variant, VariantFellBack: true),
                },
                Game = new InstalledGame
                {
                    Key = Game,
                    Name = "Example Game",
                    InstallDirectory = @"C:\Games\Example Game",
                    LibraryId = @"C:\Games",
                    BuildId = "100",
                    Platform = OperatingSystemKind.Windows,
                },
                Projects = [Offer],
                Project = Offer,
                Installed = new InstallRecord
                {
                    Id = RecordId,
                    Game = Game,
                    GameName = "Example Game",
                    InstallDirectory = @"C:\Games\Example Game",
                    InstalledAt = Now.AddDays(-3),
                    Package = new InstalledPackage(PackageKind.Manifest, "package.examplemod", ThirdPartyNotices:
                    [
                        new ThirdPartyNotice
                        {
                            Name = "Example Loader",
                            License = "MIT",
                            SourceUrl = "https://example.com/loader/source",
                            Paths = ["loader.dll"],
                        },
                    ]),
                    Source = new InstallSource
                    {
                        ProjectId = Project.ToString("D"),
                        ProjectName = "Example Project",
                        TeamName = "Lantern Team",
                        ReleaseLabel = "v1",
                        ReleaseVersion = 1,
                        VariantName = "Text only",
                    },
                    Backup = new BackupFolder(BackupLocation.GameFolder, @"C:\Games\Example Game\.example-app\backups"),
                    FileCount = 3,
                },
                Preferences = Settings.GetGame(Game),
                Job = RunningJob,
                RunningProcesses = ["example.exe"],
            },
            new MyGame
            {
                Key = FailedGame,
                Name = "Other Game",
                Status = new MyGameStatus(MyGameState.GameNotReady)
                {
                    GameState = GameInstallState.UpdatePending,
                    StateReason = GameStateReason.UpdateRequired,
                },
                OffersTargetLanguage = true,
                LastFailure = new FailedAttempt(JobKind.Install, JobOrigin.User, Now,
                    new JobFailure(JobFailureReason.FilesLocked) { Files = ["data/text.pak"], Processes = ["editor.exe"] }),
            },
        ],
        Jobs = [RunningJob],
        Stores =
        [
            new StoreStatus(StoreId.Steam, IsPresent: true, ScanFailed: false, IsComplete: true,
                [new LibraryLocation(@"C:\Games", "steam-library", @"C:\Games", LibraryState.Online)]),
            new StoreStatus(StoreId.Gog, IsPresent: true, ScanFailed: false, IsComplete: true,
                [new LibraryLocation(@"D:\GOG Games", "folder", @"D:\GOG Games", LibraryState.Offline, IsManual: true)]),
        ],
        LibraryScannedAt = Now,
        CatalogueState = CatalogueState.Stale,
        CatalogueFetchedAt = Now.AddHours(-7),
        CatalogueFailure = CatalogueErrorReason.Offline,
        PendingRecoveries = [new RecoveredOperation(RecordId, @"D:\Games\Moved", RecoveryOutcome.Unavailable)],
    };
}
