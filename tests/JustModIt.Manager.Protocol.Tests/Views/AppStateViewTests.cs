using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
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
    public void An_install_of_several_packages_names_what_every_package_carries_from_others_once()
    {
        var install = State.Games[0].Installs[0];
        var loader = install.Record.Package.ThirdPartyNotices![0];
        var tool = new ThirdPartyNotice { Name = "Example Tool", License = "Apache-2.0", SourceUrl = "https://example.com/tool/source" };
        var addOn = new InstalledPackage(PackageKind.Manifest, "add-on.examplemod", ThirdPartyNotices: [loader with { Paths = ["voices/loader.dll"] }, tool]);
        var parts = install with
        {
            Record = install.Record with { Parts = [new InstalledPart(install.Record.Package), new InstalledPart(addOn, "voices", "Voices")] },
        };

        InstallView.From(parts).ThirdPartyNotices.Should().Equal(
            new ThirdPartyNoticeView("Example Loader", "MIT", "https://example.com/loader/source"),
            new ThirdPartyNoticeView("Example Tool", "Apache-2.0", "https://example.com/tool/source"));
    }

    [Fact]
    public void An_install_names_the_files_the_software_of_every_package_changes_while_the_game_runs_once()
    {
        var install = State.Games[0].Installs[0];
        var single = install with
        {
            Record = install.Record with { Package = install.Record.Package with { RuntimeFiles = ["data/list.dat"] }, Parts = null },
        };
        var addOn = new InstalledPackage(PackageKind.Manifest, "add-on.examplemod", RuntimeFiles: ["Data/List.dat", "data/list_original.dat"]);
        var parts = install with
        {
            Record = install.Record with
            {
                Package = install.Record.Package with { RuntimeFiles = ["data/list.dat"] },
                Parts = [new InstalledPart(install.Record.Package), new InstalledPart(addOn, "series", "Series")],
            },
        };

        InstallView.From(install).RuntimeFiles.Should().BeEmpty();
        InstallView.From(single).RuntimeFiles.Should().Equal("data/list.dat");
        InstallView.From(parts).RuntimeFiles.Should().Equal(["Data/List.dat", "data/list_original.dat"],
            "the record's parts name what each package declares, and the base declares nothing");
    }

    [Fact]
    public void A_game_carries_its_status_installs_offer_and_job()
    {
        var game = View.Games[0];

        game.Key.Should().Be(Game);
        game.Status.Should().BeEquivalentTo(new
        {
            State = MyGameState.Busy,
            Update = UpdateState.Available,
            UpdateKind = UpdateKind.NewRelease,
            UpdateSize = 6144L,
        });
        game.Status.UpdateDrops.Should().Equal(new InstalledFeature("signs", "Signs"));
        game.Detected!.InstallDirectory.Should().Be(@"C:\Games\Example Game");
        game.Project!.ProjectId.Should().Be(Project);
        var install = game.Installs.Should().ContainSingle().Subject;
        install.Should().BeEquivalentTo(new
        {
            Id = RecordId,
            ProjectId = (Guid?)Project,
            ReleaseLabel = "v1",
            VariantName = (string?)null,
            Features = new[] { new InstalledFeature("signs", "Signs") },
            LeftOut = new[] { new LeftOutFeature("voice-over", "Voice-over", PartLeftOutReason.NewerApp) },
            BackupLocation = BackupLocation.GameFolder,
            BackupsUnavailable = true,
            NeedsAttention = true,
        });
        install.PackageFileNames.Should().Equal("package.examplemod", "signs.examplemod");
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
        game.SiteIds.Should().Equal([Guid.Parse("66666666-0000-4000-8000-000000000001")], "the window unveils an adult game by its id on the site");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_install_s_offer_marks_the_add_ons_whose_package_it_holds_itself_or_as_the_file_it_copies(bool copied)
    {
        var held = Guid.NewGuid();
        var feature = copied ? Voice with { Package = Voice.Package with { Uuid = Guid.NewGuid(), CopiedFrom = held } } : Voice;
        var offer = Offer with { Selection = Offer.Selection with { Features = [feature] } };
        var installed = new InstalledRelease(ReleaseOne, 1, null, [FileOne, copied ? held : Voice.Package.Uuid]);

        ProjectOfferView.From(offer, installed).Features.Single().IsPresent.Should().BeTrue();
        ProjectOfferView.From(offer, new InstalledRelease(ReleaseOne, 1, null, [FileOne])).Features.Single().IsPresent.Should().BeFalse();
    }

    [Fact]
    public void A_project_offer_carries_the_sites_status_release_and_add_ons_with_what_the_plan_takes()
    {
        var offer = View.Games[0].Projects.Should().ContainSingle().Subject;

        offer.ProjectName.Should().Be("Example Project");
        offer.Team.Name.Should().Be("Lantern Team");
        offer.Status!.IsCompleted.Should().BeTrue();
        offer.Types.Should().Equal("Text");
        offer.Release.Should().Be(new ReleaseView(ReleaseTwo, 2) { Name = "Autumn patch", Date = Now.AddDays(-1), DateKind = ReleaseDateKind.Released });
        offer.Base.Should().Be(new PackageView(FileTwo, "example-project.examplemod") { Size = 4096, Format = PackageFormat.Manifest });
        offer.IsLegacy.Should().BeFalse();
        offer.Features.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Key = "voice-over",
            Name = "Voice-over",
            Description = "Spoken lines in the cutscenes.",
            Group = (string?)null,
            IsDefault = true,
            IsOn = true,
            IsPresent = false,
            Package = new { Size = (long?)2048 },
        });
        View.Games[0].Installs[0].Project!.Features[0].IsPresent.Should().BeFalse("the install has another add-on's package");
    }

    [Fact]
    public void A_release_came_out_when_the_site_created_it_unless_the_site_tells_when_it_went_public()
    {
        var release = new Release { Uuid = ReleaseTwo, Version = 2, CreatedAt = Now.AddDays(-20), UpdatedAt = Now.AddDays(-3) };

        ReleaseView.From(release).Should().Be(new ReleaseView(ReleaseTwo, 2) { Date = Now.AddDays(-20), DateKind = ReleaseDateKind.Released },
            "the site's pages and its newest release of a game date it so");
        ReleaseView.From(release with { FirstPublishedAt = Now.AddDays(-10) }).Date.Should().Be(Now.AddDays(-10));
        ReleaseView.From(release with { CreatedAt = null }).Should().Be(
            new ReleaseView(ReleaseTwo, 2) { Date = Now.AddDays(-3), DateKind = ReleaseDateKind.Updated });
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
