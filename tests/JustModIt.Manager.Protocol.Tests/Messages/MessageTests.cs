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
using JustModIt.Manager.Core.Settings;
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
        new Notify(NoticeView.From(new AutomationNotice(NoticeKind.Failed)
        {
            Game = Game,
            GameName = "Example Game",
            ProjectName = "Example Project",
            Job = JobKind.Update,
            Failure = new JobFailure(JobFailureReason.NotEnoughSpace) { RequiredBytes = 2048, AvailableBytes = 1024 },
        })),
        new Notify(NoticeView.From(new AutomationNotice(NoticeKind.AvailableSummary) { Games = [Game, FailedGame], Updates = [FailedGame] })),
        new WindowActivity(IsActive: true),
        new InstallProject(Game, Project, "Text only"),
        new UpdateProject(RecordId),
        new RepairProject(RecordId),
        new UninstallProject(RecordId),
        new TestInstallPackage(Game, @"C:\Work\example.examplemod"),
        new MoveBackups(),
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
        new LaunchGame(Game),
        new OpenFolder(AppFolder.Game, Game),
        new OpenFolder(AppFolder.InstallBackups, Install: RecordId),
        new OpenFolder(AppFolder.Logs),
        new AddLibraryFolder(@"D:\GOG Games"),
        new LibraryFolderAdded(null, null, LibraryFolderRejection.DlcOnly),
        new RemoveLibraryFolder("gog", @"D:\GOG Games"),
        new ValidateBackupFolder(@"E:\Backups"),
        new BackupFolderChecked(BackupFolderProblem.InsideGameFolder),
        new RemoveLeftoverBackups([RecordId]),
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
        new GetCataloguePage(new CatalogueQuery { Text = "example", IncludeAdult = true, Order = CatalogueOrder.Popular }, 0, 50),
        new CataloguePageResult(1,
        [
            new CatalogueGameView(Guid.Parse("66666666-0000-4000-8000-000000000001"), "example-game", "Example Game")
            {
                Genres = ["Adventure"],
                Projects = [new CatalogueProjectView(Project, "example-project", "Example Project", new TeamView(Guid.Empty, "lantern-team", "Lantern Team"))],
            },
        ], Now),
        new GetCatalogueFilters(),
        new CatalogueFiltersResult(new CatalogueFiltersView { Genres = [new CatalogueOptionView(Guid.Empty, "Adventure")] }),
        new GetDetectionReport(),
        new DetectionReportResult(DetectionReportView.From(DetectionReport.From(new LibrarySnapshot
        {
            Stores = State.Stores,
            ScannedAt = Now,
        }), [new StorefrontPlatform { Uuid = Guid.Empty, Name = "Steam", Provider = "steam", InstallHint = "Open **Properties**." }])),
        new GetLeftoverBackups(),
        new LeftoverBackups([new LeftoverBackup(RecordId, FailedGame, "Other Game", @"E:\Backups\ExampleApp\gog-1", 4096)]),
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
