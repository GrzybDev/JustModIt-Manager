using System.Collections.Immutable;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class MyGamesComposerTests
{
    private static readonly GameKey Unmatched = new(StoreId.Steam, "3000");

    private static readonly GameKey NothingToInstall = new(StoreId.Gog, "1207658924");

    [Fact]
    public void Lists_only_detected_games_with_something_installable()
    {
        var games = Compose(Input(
            [Installed(), Installed(Unmatched), Installed(NothingToInstall)],
            [Entry(release: Release(ReleaseOne, 1, File(FileOne))),
                Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo, onlyFor: OtherGame)), NothingToInstall)]));

        games.Should().ContainSingle().Which.Key.Should().Be(Game);
        games[0].Status.State.Should().Be(MyGameState.NotInstalled);
        games[0].Project!.Project.ProjectUuid.Should().Be(Project);
        games[0].Name.Should().Be("Example Game");
    }

    [Fact]
    public void A_project_that_is_not_completed_keeps_its_game_off_the_list_unless_the_player_installed_it()
    {
        var testing = Entry(release: Release(ReleaseTwo, 2, File(FileTwo))) with { Status = InProgress };

        Compose(Input([Installed()], [testing])).Should().BeEmpty();

        var game = Compose(Input([Installed()], [testing], [Record()])).Single();
        game.Projects.Should().BeEmpty();
        game.Project.Should().BeNull();
        game.Installs.Single().Project!.Project.ProjectUuid.Should().Be(Project);
        game.Status.State.Should().Be(MyGameState.UpdateAvailable);
    }

    [Fact]
    public void Keeps_an_install_whose_game_and_project_are_gone_under_the_name_it_was_installed_with()
    {
        var record = Record(source: new InstallSource { ProjectId = Project.ToString("D"), GameName = "Example Game (installed)" });

        var game = Compose(Input([], records: [record])).Should().ContainSingle().Which;

        game.Status.Should().Be(new MyGameStatus(MyGameState.Orphaned) { Orphan = OrphanReason.GameNotDetected });
        game.Name.Should().Be("Example Game (installed)");
        game.Installs.Single().Record.Should().Be(record);
        game.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void An_install_is_listed_offline_before_the_catalogue_was_ever_read()
    {
        var game = Compose(Input([Installed()], records: [Record()])).Should().ContainSingle().Which;

        game.Status.Orphan.Should().Be(OrphanReason.NotInCatalog);
        game.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void Finds_an_installed_project_that_no_longer_links_the_game()
    {
        var abandoned = Entry(release: Release(ReleaseOne, 1, File(FileOne)), game: OtherGame);

        var game = Compose(Input([Installed()], [abandoned], [Record()])).Single();

        game.Installs.Single().Project!.Project.ProjectUuid.Should().Be(Project);
        game.Status.State.Should().Be(MyGameState.Installed);
    }

    [Fact]
    public void Leaves_the_choice_between_teams_to_the_player_until_made()
    {
        var entries = new[]
        {
            Entry(release: Release(ReleaseOne, 1, File(FileOne))),
            Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), teamSlug: "paper-team"),
        };

        Compose(Input([Installed()], entries)).Single().Project.Should().BeNull();

        var chosen = Input([Installed()], entries) with
        {
            Settings = AppSettings.Default.WithGame(Game, preferences => preferences with { ProjectId = OtherProject }),
        };
        var game = Compose(chosen).Single();
        game.Project!.Project.ProjectUuid.Should().Be(OtherProject);
        game.Projects.Should().HaveCount(2);
    }

    [Fact]
    public void Takes_the_project_of_the_most_preferred_team_unless_the_player_chose_one()
    {
        var entries = new[]
        {
            Entry(release: Release(ReleaseOne, 1, File(FileOne))),
            Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), teamSlug: "paper-team"),
        };
        var preferring = AppSettings.Default with { PreferredTeams = [Guid.NewGuid(), TeamOf("paper-team"), TeamOf("lantern-team")] };

        Compose(Input([Installed()], entries) with { Settings = preferring }).Single().Project!.Project.ProjectUuid
            .Should().Be(OtherProject);
        Compose(Input([Installed()], entries) with
        {
            Settings = preferring.WithGame(Game, preferences => preferences with { ProjectId = Project }),
        }).Single().Project!.Project.ProjectUuid.Should().Be(Project);
        Compose(Input([Installed()], entries, [Record()]) with { Settings = preferring }).Single().Installs.Single().Project!.Project
            .ProjectUuid.Should().Be(Project, "a preference never swaps the installed project");
    }

    [Fact]
    public void Tells_that_the_store_offers_the_target_language_from_the_link_or_the_game()
    {
        var polishLink = Entry(release: Release(ReleaseOne, 1, File(FileOne)), languages: ["12"]);
        var plain = Entry(release: Release(ReleaseOne, 1, File(FileOne)));

        Compose(Input([Installed()], [polishLink]) with { Language = TargetLanguage.FromCode("pl") }).Single().OffersTargetLanguage
            .Should().BeTrue();
        Compose(Input([Installed() with { Languages = ["pl-PL"] }], [plain]) with { Language = TargetLanguage.FromCode("pl") }).Single()
            .OffersTargetLanguage.Should().BeTrue();
        Compose(Input([Installed() with { Languages = ["pt-BR"] }], [plain]) with { Language = TargetLanguage.FromCode("pl") }).Single()
            .OffersTargetLanguage.Should().BeFalse();
        Compose(Input([Installed() with { Languages = ["pl"] }], [polishLink])).Single().OffersTargetLanguage
            .Should().BeFalse("an app without a target language skips the rule");
    }

    [Fact]
    public void An_epic_game_finds_its_project_by_the_store_s_namespace_and_language()
    {
        var epic = new GameKey(StoreId.Epic, "5b1f0c2e9d4a47c8b3e6a0f1d2c3b4a5");
        var entry = Entry(release: Release(ReleaseOne, 1, File(FileOne)), game: epic, languages: ["en", "pl"]);

        var game = Compose(Input([Installed(epic), Installed(new GameKey(StoreId.Epic, "lantern"))], [entry]) with
        {
            Language = TargetLanguage.FromCode("pl"),
        }).Should().ContainSingle().Which;

        (game.Key, game.Project!.Project.ProjectUuid, game.OffersTargetLanguage).Should().Be((epic, Project, true));
    }

    [Fact]
    public void A_job_makes_the_game_busy_and_travels_with_it()
    {
        var job = new JobSnapshot { Id = Guid.NewGuid(), Game = Game, GameName = "Example Game", Kind = JobKind.Install, Origin = JobOrigin.User };

        var game = Compose(Input([Installed()], [Entry(release: Release(ReleaseOne, 1, File(FileOne)))]) with
        {
            Jobs = ImmutableDictionary<GameKey, JobSnapshot>.Empty.Add(Game, job),
        }).Single();

        game.Status.State.Should().Be(MyGameState.Busy);
        game.Job.Should().Be(job);
    }

    [Fact]
    public void A_game_known_to_run_is_not_ready_and_names_what_runs()
    {
        var running = new ValueList<RunningProcess>([new RunningProcess(1, "ExampleGame", null), new RunningProcess(2, "examplegame", null)]);

        var game = Compose(Input([Installed()], [Entry(release: Release(ReleaseOne, 1, File(FileOne)))]) with
        {
            Running = ImmutableDictionary<GameKey, ValueList<RunningProcess>>.Empty.Add(Game, running),
        }).Single();

        game.Status.GameState.Should().Be(GameInstallState.Running);
        game.RunningProcesses.Should().Equal("ExampleGame");
    }

    [Fact]
    public void A_failed_job_needs_attention_on_the_install_it_worked_on_or_on_the_game()
    {
        var record = Record();
        var update = new FailedAttempt(JobKind.Update, JobOrigin.Automatic, Now, new JobFailure(JobFailureReason.Offline));
        var install = new FailedAttempt(JobKind.Install, JobOrigin.User, Now, new JobFailure(JobFailureReason.ConflictingInstall));
        var input = Input([Installed()], [Entry(release: Release(ReleaseOne, 1, File(FileOne)))], [record]);

        var onInstall = Compose(input with
        {
            Failures = ImmutableDictionary<FailureKey, FailedAttempt>.Empty.Add(new FailureKey(Game, record.Id), update),
        }).Single();
        var onGame = Compose(input with
        {
            Failures = ImmutableDictionary<FailureKey, FailedAttempt>.Empty.Add(new FailureKey(Game, null), install),
        }).Single();

        onInstall.Installs.Single().LastFailure.Should().Be(update);
        onInstall.LastFailure.Should().BeNull();
        onInstall.NeedsAttention.Should().BeTrue();
        onInstall.Status.State.Should().Be(MyGameState.Installed);
        onGame.LastFailure.Should().Be(install);
        onGame.Installs.Single().NeedsAttention.Should().BeFalse();
        onGame.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void Verification_and_missing_originals_reach_the_status()
    {
        var record = Record();
        var input = Input([Installed()], [Entry(release: Release(ReleaseOne, 1, File(FileOne)))], [record]);

        Compose(input with { Repairs = ImmutableDictionary<Guid, RepairReason>.Empty.Add(record.Id, RepairReason.OriginalsRestored) })
            .Single().Status.Repair.Should().Be(RepairReason.OriginalsRestored);
        Compose(input with { UnavailableBackups = ImmutableHashSet.Create(record.Id) }).Single().Status.State
            .Should().Be(MyGameState.BackupsUnavailable);
    }

    [Fact]
    public void Missing_originals_are_known_also_while_the_status_tells_something_else_first()
    {
        var record = Record();
        var running = new ValueList<RunningProcess>([new RunningProcess(1, "ExampleGame", null)]);
        var game = Compose(Input([Installed()], [Entry(release: Release(ReleaseOne, 1, File(FileOne)))], [record]) with
        {
            UnavailableBackups = ImmutableHashSet.Create(record.Id),
            Running = ImmutableDictionary<GameKey, ValueList<RunningProcess>>.Empty.Add(Game, running),
        }).Single();

        var install = game.Installs.Single();
        install.Status.State.Should().Be(MyGameState.GameNotReady);
        install.BackupsUnavailable.Should().BeTrue();
    }

    [Fact]
    public void Lists_every_install_with_its_own_status_and_heads_the_game_with_what_matters_most()
    {
        var updatable = Record();
        var broken = Record(source: Source(ReleaseOne, FileOne, 1, project: OtherProject)) with { InstalledAt = Now.AddHours(-1) };
        var left = Record(folder: Path.Join("copy", "Example")) with { InstalledAt = Now };
        IndexEntry[] entries =
        [
            Entry(release: Release(ReleaseTwo, 2, File(FileTwo))),
            Entry(OtherProject, Release(ReleaseOne, 1, File(FileOne)), teamSlug: "paper-team"),
        ];
        var input = Input([Installed()], entries, [left, updatable, broken]) with
        {
            Repairs = ImmutableDictionary<Guid, RepairReason>.Empty.Add(broken.Id, RepairReason.FilesModified),
        };

        var game = Compose(input).Single();

        game.Installs.Select(install => (install.Record.Id, install.Status.State, install.Project?.Project.ProjectUuid)).Should().Equal(
            (broken.Id, MyGameState.NeedsRepair, (Guid?)OtherProject),
            (updatable.Id, MyGameState.UpdateAvailable, Project),
            (left.Id, MyGameState.Orphaned, Project));
        game.Status.Should().Be(new MyGameStatus(MyGameState.Orphaned) { Orphan = OrphanReason.GameMoved }, "an install lost its game");
        Compose(input with { Records = [updatable, broken] }).Single().Status.State.Should().Be(MyGameState.NeedsRepair);
    }

    [Fact]
    public void Tells_whether_a_new_install_joins_the_game_or_replaces_the_install_in_its_folder()
    {
        var record = Record();
        var input = Input([Installed()], [Entry(release: Release(ReleaseOne, 1, File(FileOne)))], [record]);
        var oneProject = input with { InstallPolicy = new OneProjectPolicy() };

        Compose(input).Single().NewInstall.Should().Be(InstallAdmission.Alongside);
        Compose(oneProject).Single().NewInstall!.Replaces.Should().Be(record);
        Compose(oneProject with { Records = [Record(folder: Path.Join("copy", "Example"))] }).Single().NewInstall
            .Should().Be(InstallAdmission.Alongside, "a replacement uninstalls and installs in the game's one folder");
    }

    [Fact]
    public void Hidden_games_stay_listed_with_their_flag_and_the_list_is_sorted_by_name()
    {
        var input = Input([Installed(), Installed(OtherGame)],
            [Entry(release: Release(ReleaseOne, 1, File(FileOne))), Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame)]);
        var hidden = input with { Settings = AppSettings.Default.WithGame(OtherGame, preferences => preferences with { IsHidden = true }) };

        var games = Compose(hidden);

        games.Select(game => game.Key).Should().Equal(Game, OtherGame);
        games[1].IsHidden.Should().BeTrue();
    }

    private static ImmutableArray<MyGame> Compose(MyGamesInput input) => MyGamesComposer.Compose(input);

    private static MyGamesInput Input(InstalledGame[] games, IndexEntry[]? entries = null, InstallRecord[]? records = null) => new()
    {
        Library = new LibrarySnapshot
        {
            Games = new ValueDictionary<GameKey, InstalledGame>(games.ToImmutableDictionary(game => game.Key)),
        },
        Records = records ?? [],
        Index = new InstallableIndex(Now, entries ?? []),
        Settings = AppSettings.Default,
    };
}
