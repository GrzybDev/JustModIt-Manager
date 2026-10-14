using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using static JustModIt.Manager.Protocol.Tests.Views.TestStates;

namespace JustModIt.Manager.Protocol.Tests.Messages;

public sealed class MessageTests
{
    private static readonly Message[] Samples =
    [
        new Hello(Hello.CurrentProtocolVersion, "1.0.0", ProcessRole.Ui, 42),
        new Ping(),
        new Activate(["--background"]),
        new BringToFront(),
        new Navigate(new GameTarget("steam", "1000")),
        new ShutdownRequested(ShutdownReason.Update),
        new ShutdownAnswer(Accepted: false),
        new StateChanged(AppStateView.From(3, State, Settings, new SiteConfiguration { Name = "Example Site" }, new ActivityView(),
            new AppUpdateView { State = AppUpdateState.Downloading, Version = "1.1.0", Progress = 40 })),
        new JobProgress([JobView.From(RunningJob)]),
        new JobEnded(JobView.From(RunningJob), JobOutcome.Failed,
            JobFailureView.From(new JobFailure(JobFailureReason.GameRunning) { Processes = ["example.exe"] })),
        new JobEnded(JobView.From(RunningJob), JobOutcome.Succeeded) { KeptFiles = [new KeptFilesView(KeptReason.ChangedSinceInstall, 2)] },
        new JobEnded(JobView.From(RunningJob), JobOutcome.Succeeded)
        {
            KeptFiles = [new KeptFilesView(KeptReason.GameUpdatedSinceInstall, 1), new KeptFilesView(KeptReason.OriginalUnknown, 1)],
        },
        new JobEnded(JobView.From(RunningJob), JobOutcome.Succeeded) { Forgotten = true },
        new Notify(NoticeView.From(new AutomationNotice(NoticeKind.Failed)
        {
            Game = Game,
            GameName = "Example Game",
            ProjectName = "Example Project",
            Job = JobKind.Update,
            Failure = new JobFailure(JobFailureReason.NotEnoughSpace) { RequiredBytes = 2048, AvailableBytes = 1024 },
        })),
        new Notify(NoticeView.From(new AutomationNotice(NoticeKind.AvailableSummary) { Games = [Game, FailedGame], Updates = [FailedGame] })),
        new Notify(NoticeView.From(new AutomationNotice(NoticeKind.StoreCheckAdvised)
        {
            Game = Game,
            GameName = "Example Game",
            Kept = KeptReason.GameUpdatedSinceInstall,
        })),
        new WindowActivity(IsActive: true),
        new InstallProject(Game, Project, "Text only"),
        new UpdateProject(RecordId),
        new RepairProject(RecordId),
        new UninstallProject(RecordId),
        new TestInstallPackage(Game, @"C:\Work\example.examplemod"),
        new MoveBackups(),
        new StopMovingBackups(),
        new UninstallEverything(),
        new ClearCache(),
        new JobStarted(Job),
        new CancelJob(Job),
        new DismissFailure(FailedGame),
        new DismissFailure(Game, RecordId),
        new OpenedGame(Game),
        new CheckNow(),
        new ChangeSettings(SettingsView.From(Settings)),
        new SetGameAutomation(Game, AutoInstall: true),
        new ChooseProject(Game, Project),
        new ChooseVariant(Game, null),
        new HideGame(Game, IsHidden: true),
        new ConfirmAdultContent(),
        new RevealAdultPictures(Project),
        new RememberCreatorProject(@"C:\Work\Example.exampleproj"),
        new LaunchGame(Game),
        new OpenFolder(AppFolder.Game, Game),
        new OpenFolder(AppFolder.InstallBackups, Install: RecordId),
        new OpenFolder(AppFolder.Logs),
        new AddLibraryFolder(@"D:\GOG Games"),
        new LibraryFolderAdded(null, null, LibraryFolderRejection.DlcOnly),
        new RemoveLibraryFolder("gog", @"D:\GOG Games"),
        new ValidateBackupFolder(@"E:\Backups"),
        new BackupFolderChecked(BackupFolderProblem.InsideGameFolder),
        new BackupFolderChecked(AvailableBytes: 812_000_000_000),
        new RemoveLeftoverBackups([RecordId]),
        new DismissUncleanExit(),
        new SaveDiagnosticReport(@"C:\Reports\report.zip"),
        new ApplyAppUpdate(),
        new ExitApp(ExitMode.AfterJobs),
        new GetProjectPage("example-game", "lantern-team", Project),
        new ProjectPageResult
        {
            Page = JsonDocument.Parse("""{"uuid":"11111111-0000-4000-8000-000000000001","name":"Example"}""").RootElement,
            FetchedAt = Now,
            IsStale = true,
            Error = CatalogueErrorReason.Offline,
        },
        new ProjectPageResult { IsGone = true },
        new GetCataloguePage(new CatalogueQuery { Text = "example", IncludeAdult = true, Order = CatalogueOrder.Popular }, 0, 50, DetectedOnly: true),
        new CataloguePageResult(1,
        [
            new CatalogueGameView(Guid.Parse("66666666-0000-4000-8000-000000000001"), "example-game", "Example Game")
            {
                Genres = ["Adventure"],
                Projects =
                [
                    new CatalogueProjectView(Project, "example-project", "Example Project", new TeamView(Guid.Empty, "lantern-team", "Lantern Team"))
                    {
                        CompletedAt = new DateOnly(2026, 8, 4),
                    },
                ],
                Detected = [Game],
            },
        ], Now)
        {
            Facets = new CatalogueFacets { Statuses = new CatalogueFacet(1, [new CatalogueCount(Project, 1)]), Adult = 2 },
            Detected = 1,
            Unfiltered = 3,
        },
        new GetCatalogueGame("example-game"),
        new CatalogueGameResult(new CatalogueGameView(Guid.Parse("66666666-0000-4000-8000-000000000001"), "example-game", "Example Game")),
        new CatalogueGameResult(),
        new GetCatalogueFilters(),
        new CatalogueFiltersResult(new CatalogueFiltersView { Genres = [new CatalogueOptionView(Guid.Empty, "Adventure")] }),
        new GetDetectionReport(),
        new DetectionReportResult(DetectionReportView.From(DetectionReport.From(new LibrarySnapshot
        {
            Stores = State.Stores,
            ScannedAt = Now,
        }), [new StorefrontPlatform { Uuid = Guid.Empty, Name = "Steam", Provider = "steam", InstallHint = "Open **Properties**." }])),
        new GetAppDataUsage(),
        new AppDataUsage(@"C:\Users\Example\AppData\Local\ExampleVendor\ExampleApp", 317_718_528, 224_395_264, 90_177_536, 3_355_443),
        new GetDetectedGames(),
        new DetectedGames(
        [
            new DetectedGameEntry(Game, "Example Game",
                new DetectedGameView { InstallDirectory = @"D:\Games\Example Game", LibraryId = "steam-library", BuildId = "1412244" })
            {
                Cover = "https://images.example.com/covers/example-game.png",
                Projects = [new SiteProjectView(Project, "example-game", "lantern-team")],
            },
        ]),
        new GetLeftoverBackups(),
        new LeftoverBackups(
        [
            new LeftoverBackup(RecordId, FailedGame, "Other Game", @"E:\Backups\ExampleApp\gog-1", 4096),
            new LeftoverBackup(new Guid("6c1f3a2e-8d4b-4f0a-9e5c-2b7d1a3c4e5f"), Game, "Example Game",
                @"D:\Games\Example Game\.example-app\backups\6c1f3a2e8d4b4f0a9e5c2b7d1a3c4e5f", 96)
            {
                GameDirectory = @"D:\Games\Example Game",
                Cover = new Uri("https://images.example.com/covers/example-game.png"),
                Kept = new KeptFilesSummary(KeptReason.GameUpdatedSinceInstall, ["data/list.dat"], 1, "Example Project",
                    new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.Zero)),
            },
        ]),
    ];

    public static TheoryData<int> SampleIndexes => [.. Enumerable.Range(0, Samples.Length)];

    [Fact]
    public void Every_message_type_has_a_sample()
    {
        var declared = typeof(Message).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(attribute => attribute.DerivedType);

        Samples.Select(sample => sample.GetType()).Distinct().Should().BeEquivalentTo(declared);
    }

    [Theory]
    [MemberData(nameof(SampleIndexes))]
    public void Every_message_round_trips(int index)
    {
        var frame = new RequestFrame(1, Samples[index]);
        var payload = FrameCodec.Serialize(frame, FrameCodec.DefaultMaxFrameLength);

        var read = (RequestFrame)FrameCodec.Deserialize(payload);

        read.Payload.GetType().Should().Be(Samples[index].GetType());
        FrameCodec.Serialize(read, FrameCodec.DefaultMaxFrameLength).Should().Equal(payload);
    }

    [Fact]
    public void A_job_s_end_counts_the_files_its_uninstall_left_by_reason_and_leaves_the_field_out_when_it_left_none()
    {
        var uninstalled = new JobResult(RunningJob, JobOutcome.Succeeded)
        {
            KeptFiles =
            [
                new KeptFile("voice.pak", KeptReason.BackupMissing),
                new KeptFile("text.pak", KeptReason.ChangedSinceInstall),
                new KeptFile("readme.txt", KeptReason.ChangedSinceInstall),
            ],
        };

        var ended = JobEnded.From(uninstalled);
        var plain = Json(JobEnded.From(uninstalled with { KeptFiles = [] }));

        ended.KeptFiles.Should().Equal(new KeptFilesView(KeptReason.ChangedSinceInstall, 2), new KeptFilesView(KeptReason.BackupMissing, 1));
        plain.Should().NotContain("keptFiles", "an end without kept files reads as one from an agent that never sent them");
        ((JobEnded)((EventFrame)FrameCodec.Deserialize(Encoding.UTF8.GetBytes(plain))).Payload).KeptFiles.Should().BeNull();
    }

    [Fact]
    public void A_job_s_end_tells_that_its_uninstall_found_the_install_s_folder_gone()
    {
        var forgotten = new JobResult(RunningJob, JobOutcome.Succeeded) { Forgotten = true, RemovedRecord = Guid.NewGuid() };

        JobEnded.From(forgotten).Forgotten.Should().BeTrue();
        JobEnded.From(forgotten with { Forgotten = false }).Forgotten.Should().BeFalse();
    }

    [Fact]
    public void Games_travel_as_their_text_key()
    {
        Json(new LaunchGame(new GameKey(StoreId.Gog, "1207658924000000001")))
            .Should().Contain("""
                {"type":"launchGame","game":"gog:1207658924000000001"}
                """);
    }

    [Fact]
    public void Typed_reasons_travel_by_name()
    {
        Json(new BackupFolderChecked(BackupFolderProblem.NetworkFolder)).Should().Contain("\"problem\":\"NetworkFolder\"");
        Json(new ExitApp(ExitMode.Now)).Should().Contain("\"mode\":\"Now\"");
    }

    [Fact]
    public void A_project_page_travels_exactly_as_the_site_sent_it()
    {
        const string page = """{"uuid":"11111111-0000-4000-8000-000000000001","unknown_field":[1,2]}""";
        var result = new ProjectPageResult { Page = JsonDocument.Parse(page).RootElement, FetchedAt = Now };

        var read = (ProjectPageResult)((RequestFrame)FrameCodec.Deserialize(FrameCodec.Serialize(new RequestFrame(1, result),
            FrameCodec.DefaultMaxFrameLength))).Payload;

        read.Page!.Value.GetRawText().Should().Be(page);
    }

    [Theory]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"launchGame","game":"not a key"}}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"updateProject","install":"not an id"}}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"updateProject"}}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"exitApp","mode":"Explode"}}""")]
    public void A_command_with_a_broken_value_is_invalid_data(string json)
    {
        var deserialize = () => FrameCodec.Deserialize(Encoding.UTF8.GetBytes(json));

        deserialize.Should().Throw<InvalidDataException>();
    }

    private static string Json(Message message) =>
        Encoding.UTF8.GetString(FrameCodec.Serialize(new EventFrame(message), FrameCodec.DefaultMaxFrameLength));
}
