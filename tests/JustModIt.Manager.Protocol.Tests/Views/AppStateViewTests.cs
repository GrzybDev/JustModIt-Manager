using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Views;
using static JustModIt.Manager.Protocol.Tests.Views.TestStates;

namespace JustModIt.Manager.Protocol.Tests.Views;

public sealed class AppStateViewTests
{
    private static AppStateView View => AppStateView.From(40, State, Settings, new SiteConfiguration { Name = "Example Site" },
        new ActivityView { IsChecking = true }, new AppUpdateView { State = AppUpdateState.Ready, Version = "1.1.0" });

    [Fact]
    public void Carries_the_snapshot_number_and_what_the_agent_does()
    {
        var view = View;

        view.Version.Should().Be(40);
        view.IsLoaded.Should().BeTrue();
        view.Activity.IsChecking.Should().BeTrue();
        view.Update.Should().Be(new AppUpdateView { State = AppUpdateState.Ready, Version = "1.1.0" });
        view.Jobs.Should().ContainSingle().Which.Should().BeEquivalentTo(view.Games[0].Job);
    }

    [Fact]
    public void A_game_carries_its_status_installs_offer_and_job()
    {
        var game = View.Games[0];

        game.Key.Should().Be(Game);
        game.Status.Should().Be(new GameStatusView(MyGameState.Busy)
        {
            Update = UpdateState.Available,
            UpdateKind = UpdateKind.NewRelease,
            UpdateVariant = "Text only",
            UpdateVariantFellBack = true,
        });
        game.Detected!.InstallDirectory.Should().Be(@"C:\Games\Example Game");
        game.Project!.ProjectId.Should().Be(Project);
        var install = game.Installs.Should().ContainSingle().Subject;
        install.Should().BeEquivalentTo(new
        {
            Id = RecordId,
            ProjectId = (Guid?)Project,
            ReleaseLabel = "v1",
            VariantName = "Text only",
            BackupLocation = BackupLocation.GameFolder,
            NeedsAttention = true,
        });
        install.Status.State.Should().Be(MyGameState.Busy);
        install.Project!.Release!.Id.Should().Be(ReleaseTwo);
        install.LastFailure!.Failure!.Reason.Should().Be(JobFailureReason.Offline);
        install.ThirdPartyNotices.Should().Equal(new ThirdPartyNoticeView("Example Loader", "MIT", "https://example.com/loader/source"));
        game.Preferences.AutoUpdate.Should().BeFalse();
        game.Job!.Should().BeEquivalentTo(new
        {
            Id = Job,
            InstallId = (Guid?)RecordId,
            Stage = JobStage.Waiting,
            WaitReason = JobWaitReason.GameRunning,
            Sequence = 7L,
        });
        game.Job.RunningProcesses.Should().Equal("example.exe");
        game.RunningProcesses.Should().Equal("example.exe");
    }

    [Fact]
    public void A_project_offer_carries_the_sites_status_release_and_variants()
    {
        var offer = View.Games[0].Projects.Should().ContainSingle().Subject;

        offer.ProjectName.Should().Be("Example Project");
        offer.Team.Name.Should().Be("Lantern Team");
        offer.Status!.IsCompleted.Should().BeTrue();
        offer.Types.Should().Equal("Text");
        offer.Release.Should().Be(new ReleaseView(ReleaseTwo, 2) { Name = "Autumn patch", Date = Now.AddDays(-1), DateKind = ReleaseDateKind.Released });
        offer.DefaultVariant.Should().Be("Text only");
        offer.Variants.Select(variant => (variant.Name, variant.Package?.FileId, variant.Unavailable))
            .Should().Equal(("Text only", FileTwo, null), ("Text and voice", null, SelectionUnavailability.OtherSystems));
        offer.Variants[0].Package!.Format.Should().Be(PackageFormat.Manifest);
    }

    [Fact]
    public void A_release_without_a_known_publication_shows_when_it_last_changed_never_when_it_was_created()
    {
        var release = new Release { Uuid = ReleaseTwo, Version = 2, CreatedAt = Now.AddYears(-1), UpdatedAt = Now.AddDays(-3) };

        ReleaseView.From(release).Should().Be(new ReleaseView(ReleaseTwo, 2) { Date = Now.AddDays(-3), DateKind = ReleaseDateKind.Updated });
    }

    [Fact]
    public void A_failed_game_carries_the_typed_failure_and_why_it_is_not_ready()
    {
        var game = View.Games[1];

        game.Status.GameState.Should().Be(GameInstallState.UpdatePending);
        game.Status.StateReason.Should().Be(GameStateReason.UpdateRequired);
        game.OffersTargetLanguage.Should().BeTrue();
        game.NeedsAttention.Should().BeTrue();
        game.LastFailure!.Failure!.Reason.Should().Be(JobFailureReason.FilesLocked);
        game.LastFailure.Failure.Files.Should().Equal("data/text.pak");
        game.LastFailure.Failure.Processes.Should().Equal("editor.exe");
    }

    [Fact]
    public void The_library_summary_flags_stores_with_problems_and_lists_added_folders()
    {
        var library = View.Library;

        library.Stores.Select(store => (store.Store, store.HasProblems, store.OfflineLibraryCount))
            .Should().Equal(("steam", false, 0), ("gog", true, 1));
        library.AddedFolders.Should().Equal(new LibraryFolderView("gog", @"D:\GOG Games"));
    }

    [Fact]
    public void The_catalogue_says_how_fresh_it_is_and_why_it_is_stale()
    {
        View.Catalogue.Should().Be(new CatalogueView
        {
            State = CatalogueState.Stale,
            FetchedAt = Now.AddHours(-7),
            Failure = CatalogueErrorReason.Offline,
            SiteName = "Example Site",
        });
        View.PendingRecoveries.Should().Equal(new RecoveredOperation(RecordId, @"D:\Games\Moved", RecoveryOutcome.Unavailable));
    }

    [Fact]
    public void Changed_settings_keep_the_per_game_choices_and_added_folders()
    {
        var view = SettingsView.From(Settings) with { AutoInstallProjects = true, BackupLocation = BackupLocation.AppData };

        var applied = view.ApplyTo(Settings);

        applied.Should().Be(Settings with { AutoInstallProjects = true, BackupLocation = BackupLocation.AppData });
        SettingsView.From(applied).CustomBackupFolder.Should().Be(@"E:\Backups");
    }

    [Fact]
    public void The_default_snapshot_shows_the_default_settings()
    {
        new AppStateView().Settings.ApplyTo(AppSettings.Default).Should().Be(AppSettings.Default);
    }
}
