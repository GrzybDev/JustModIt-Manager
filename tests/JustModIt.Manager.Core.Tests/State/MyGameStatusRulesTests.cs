using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.State;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class MyGameStatusRulesTests
{
    private static readonly ProjectOffer CurrentRelease = Offer(Release(ReleaseOne, 1, File(FileOne)));

    private static readonly ProjectOffer NewerRelease = Offer(Release(ReleaseTwo, 2, File(FileTwo)));

    [Fact]
    public void A_detected_game_without_an_install_is_not_installed_while_it_is_ready()
    {
        MyGameStatusRules.Resolve(Installed(), hasJob: false, []).State.Should().Be(MyGameState.NotInstalled);
        MyGameStatusRules.Resolve(Installed(state: GameInstallState.Updating), hasJob: false, []).State.Should().Be(MyGameState.GameNotReady);
    }

    [Fact]
    public void A_game_tells_first_what_the_player_must_hear_first_of_its_installs()
    {
        var repair = new MyGameStatus(MyGameState.NeedsRepair) { Repair = RepairReason.FilesModified };
        var update = new MyGameStatus(MyGameState.UpdateAvailable);
        var test = new MyGameStatus(MyGameState.TestInstalled);
        var moved = new MyGameStatus(MyGameState.Orphaned) { Orphan = OrphanReason.GameMoved };

        MyGameStatusRules.Resolve(Installed(), hasJob: false, [test, update, repair]).Should().Be(repair);
        MyGameStatusRules.Resolve(Installed(), hasJob: false, [new MyGameStatus(MyGameState.Installed), test]).Should().Be(test);
        MyGameStatusRules.Resolve(Installed(), hasJob: false, [repair, moved]).Should().Be(moved, "an install lost its game");
        MyGameStatusRules.Resolve(Installed(), hasJob: true, [repair]).State.Should().Be(MyGameState.Busy);
    }

    [Theory]
    [InlineData(GameInstallState.Updating, false)]
    [InlineData(GameInstallState.Broken, false)]
    [InlineData(GameInstallState.Offline, false)]
    [InlineData(GameInstallState.Unknown, false)]
    [InlineData(GameInstallState.Running, false)]
    [InlineData(GameInstallState.Updating, true)]
    [InlineData(GameInstallState.Offline, true)]
    public void A_game_the_store_is_not_done_with_is_not_ready_with_its_state(GameInstallState state, bool installed)
    {
        var game = Installed(state: state) with { Reason = GameStateReason.StoreBusy };

        (installed ? Resolve(game, Record(), CurrentRelease) : MyGameStatusRules.Resolve(game, hasJob: false, []))
            .Should().Be(new MyGameStatus(MyGameState.GameNotReady) { GameState = state, StateReason = GameStateReason.StoreBusy });
    }

    [Fact]
    public void A_game_whose_records_could_not_be_read_is_not_ready()
    {
        Resolve(Installed() with { IsStale = true }, Record(), CurrentRelease).Should().Be(new MyGameStatus(MyGameState.GameNotReady)
        {
            GameState = GameInstallState.Unknown,
            StateReason = GameStateReason.UnreadableStoreData,
        });
    }

    [Fact]
    public void The_current_release_installed_is_installed_and_up_to_date()
    {
        var status = Resolve(Installed(), Record(), CurrentRelease);

        status.State.Should().Be(MyGameState.Installed);
        status.Update!.State.Should().Be(UpdateState.UpToDate);
    }

    [Fact]
    public void A_newer_release_is_an_update_in_the_installed_variant()
    {
        var status = Resolve(Installed(), Record(), NewerRelease);

        status.State.Should().Be(MyGameState.UpdateAvailable);
        status.Update!.Kind.Should().Be(UpdateKind.NewRelease);
        status.Update.Variant!.Package!.Uuid.Should().Be(FileTwo);
        status.Update.VariantFellBack.Should().BeFalse();
    }

    [Fact]
    public void A_newer_release_with_nothing_for_the_game_is_no_update()
    {
        var status = Resolve(Installed(), Record(), Offer(Release(ReleaseTwo, 2, File(FileTwo, onlyFor: OtherGame))));

        status.State.Should().Be(MyGameState.Installed);
        status.Update!.State.Should().Be(UpdateState.NotForThisGame);
    }

    [Fact]
    public void An_older_release_made_public_again_is_never_offered_as_an_update()
    {
        var record = Record(source: Source(ReleaseTwo, FileTwo, 2));

        var status = Resolve(Installed(), record, CurrentRelease);

        status.State.Should().Be(MyGameState.Installed);
        status.Update!.State.Should().Be(UpdateState.RolledBack);
    }

    [Fact]
    public void An_install_without_its_release_is_never_offered_an_update()
    {
        var record = Record(source: new InstallSource { ProjectId = Project.ToString("D") });

        Resolve(Installed(), record, NewerRelease).Should().Be(new MyGameStatus(MyGameState.Installed));
    }

    [Theory]
    [InlineData(RepairReason.FilesModified)]
    [InlineData(RepairReason.OriginalsRestored)]
    [InlineData(RepairReason.FilesMissing)]
    public void Changed_files_need_a_repair_even_when_an_update_exists(RepairReason reason)
    {
        Resolve(Installed(), Record(), NewerRelease, reason).Should().Be(new MyGameStatus(MyGameState.NeedsRepair) { Repair = reason });
    }

    [Fact]
    public void An_install_whose_game_or_project_is_gone_is_orphaned_with_the_reason()
    {
        Resolve(null, Record(), CurrentRelease).Orphan.Should().Be(OrphanReason.GameNotDetected);
        Resolve(Installed(folder: Path.Join("elsewhere", "Example")), Record(), CurrentRelease).Orphan.Should().Be(OrphanReason.GameMoved);
        Resolve(Installed(), Record(), null).Orphan.Should().Be(OrphanReason.NotInCatalog);
    }

    [Fact]
    public void An_install_whose_originals_are_not_there_says_so_before_anything_else_to_do()
    {
        var record = Record();

        Resolve(Installed(), record, NewerRelease, RepairReason.FilesModified, backupsUnavailable: true)
            .Should().Be(new MyGameStatus(MyGameState.BackupsUnavailable) { BackupDirectory = record.Backup.Directory });
        Resolve(Installed(state: GameInstallState.Offline), record, NewerRelease, backupsUnavailable: true).State
            .Should().Be(MyGameState.GameNotReady);
    }

    [Fact]
    public void A_test_install_is_never_repaired_or_updated()
    {
        Resolve(Installed(), Record(isTest: true), NewerRelease, RepairReason.FilesModified).State.Should().Be(MyGameState.TestInstalled);
    }

    [Fact]
    public void A_job_comes_first()
    {
        Resolve(null, Record(), null, RepairReason.FilesMissing, hasJob: true).Should().Be(new MyGameStatus(MyGameState.Busy));
        MyGameStatusRules.Resolve(Installed(state: GameInstallState.Updating), hasJob: true, []).State.Should().Be(MyGameState.Busy);
    }

    private static MyGameStatus Resolve(InstalledGame? game, InstallRecord record, ProjectOffer? project, RepairReason? repair = null,
        bool hasJob = false, bool backupsUnavailable = false) =>
        MyGameStatusRules.Resolve(game, record, project, repair, hasJob, backupsUnavailable);

    private static ProjectOffer Offer(Release? release)
    {
        var entry = Entry(release: release);
        return new ProjectOffer(entry, ReleaseSelector.Select(entry, Game, OperatingSystemKind.Windows));
    }
}
