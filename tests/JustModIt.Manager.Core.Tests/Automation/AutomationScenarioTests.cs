using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.Jobs;
using JustModIt.Manager.Core.Tests.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Automation;

public sealed class AutomationScenarioTests
{
    private static readonly AppSettings AutoInstall = AppSettings.Default with { OnboardingCompleted = true, AutoInstallProjects = true };

    private static readonly AppSettings AnnounceOnly = AppSettings.Default with { OnboardingCompleted = true };

    private static readonly VerificationResult Modified =
        new([new FileVerification("data/strings.pak", FileChange.Replaced, FileIntegrity.Modified)]);

    [Fact]
    public async Task Installs_a_project_on_its_own_and_tells_the_player()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();

        await app.StartAsync();

        var notice = await app.NoticeAsync();
        notice.Should().BeEquivalentTo(new
        {
            Kind = NoticeKind.Installed,
            Game = (GameKey?)Game,
            TeamName = "Lantern Team",
            ReleaseLabel = "v1",
            ReleaseVersion = 1,
        });
        await app.GameAsync(game => game.Status.State == MyGameState.Installed, "the project is installed");
        app.Settings.Current.GetGame(Game).Should().Be(GamePreferences.Default, "the automation learns only from the player");
    }

    [Fact]
    public async Task Waits_for_the_first_run_setup_and_then_sums_up_what_is_on_offer()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Library.Publish(Installed(), Installed(OtherGame));
        app.Catalogue.Publish(CatalogueState.Fresh, Entry(release: Release(ReleaseOne, 1, File(FileOne))),
            Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        await app.StartAsync();
        await Eventually.SettleAsync();

        app.Coordinator.Notices.TryRead(out _).Should().BeFalse();
        app.Settings.Update(settings => settings with { OnboardingCompleted = true });

        var summary = await app.NoticeAsync();
        summary.Kind.Should().Be(NoticeKind.AvailableSummary);
        summary.Games.Should().BeEquivalentTo([Game, OtherGame]);
        await Eventually.SettleAsync();
        app.Coordinator.Notices.TryRead(out _).Should().BeFalse("each project is announced once");
        app.Jobs.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Announces_a_new_release_once_even_across_restarts()
    {
        var history = new InMemoryDocumentStore<AutomationHistory>(AutomationHistory.Empty.WithBaseline(Now.AddDays(-1)));
        await using (var first = new CoordinatorHarness(AnnounceOnly, history: history).WithGameAndProject())
        {
            await first.StartAsync();

            (await first.NoticeAsync()).Should().BeEquivalentTo(new { Kind = NoticeKind.Available, ReleaseVersion = 1, OffersTargetLanguage = false });
        }

        await using var second = new CoordinatorHarness(AnnounceOnly, history: history).WithGameAndProject();
        await second.StartAsync();
        await Eventually.SettleAsync();

        second.Coordinator.Notices.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Only_announces_a_project_for_a_game_its_store_offers_in_the_target_language()
    {
        var history = new InMemoryDocumentStore<AutomationHistory>(AutomationHistory.Empty.WithBaseline(Now.AddDays(-1)));
        await using var app = new CoordinatorHarness(AutoInstall, TargetLanguage.FromCode("pl"), history);
        app.Library.Publish(Installed());
        app.Catalogue.Publish(CatalogueState.Fresh, Entry(release: Release(ReleaseOne, 1, File(FileOne)), languages: ["polish"]));

        await app.StartAsync();

        (await app.NoticeAsync()).Should().BeEquivalentTo(new { Kind = NoticeKind.Available, OffersTargetLanguage = true });
        app.Catalogue.PageRequests.Should().BeEmpty();
        app.Jobs.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Reports_a_failure_once_and_tries_again_six_hours_later()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Downloader.Failure = new CatalogueException(CatalogueErrorReason.Offline, "No connection.");
        await app.StartAsync();

        var failed = await app.NoticeAsync();
        failed.Should().BeEquivalentTo(new { Kind = NoticeKind.Failed, Job = (JobKind?)JobKind.Install });
        failed.Failure!.Reason.Should().Be(JobFailureReason.Offline);
        await app.GameAsync(game => game.LastFailure is not null && game.Job is null, "the failure shows");

        app.Time.Advance(TimeSpan.FromHours(5));
        app.Library.Publish(Installed());
        await Eventually.SettleAsync();
        app.Catalogue.PageRequests.Should().HaveCount(1, "the attempt is not repeated within six hours");

        app.Time.Advance(TimeSpan.FromHours(1));
        await Eventually.HoldsAsync(() => app.Catalogue.PageRequests.Count == 2, "the attempt is repeated");
        await Eventually.HoldsAsync(() => !app.Jobs.IsBusy, "the second attempt ends");
        await Eventually.SettleAsync();
        app.Coordinator.Notices.TryRead(out _).Should().BeFalse("only the first failure is reported");
    }

    [Fact]
    public async Task A_package_made_for_another_game_version_is_tried_again_only_on_a_new_build()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Engine.Add(Record());
        app.Downloader.Cached[FileOne] = true;
        app.Engine.OnVerify = _ => Modified;
        app.Engine.Prediction = new PreflightReport([new MissingFileIssue("data/strings.pak")], []);
        await app.StartAsync();

        (await app.NoticeAsync()).Failure!.Reason.Should().Be(JobFailureReason.UnsupportedGameVersion);
        app.Time.Advance(TimeSpan.FromDays(2));
        app.Library.Publish(Installed());
        await Eventually.SettleAsync();
        app.Engine.Calls.Count(call => call == "predict").Should().Be(1);

        app.Library.Publish(Installed(build: "101"));

        await Eventually.HoldsAsync(() => app.Engine.Calls.Count(call => call == "predict") == 2, "the new build is tried");
    }

    [Fact]
    public async Task A_player_who_cancels_an_automatic_install_gets_the_game_left_alone()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await app.StartAsync();
        await app.GameAsync(game => game.Job is not null, "the automatic install runs");

        app.Jobs.Cancel(Game);

        await app.GameAsync(game => game.Job is null, "the install is cancelled");
        app.Settings.Current.GetGame(Game).AutoInstall.Should().BeFalse();
        app.Catalogue.PageGate.SetResult();
        app.Library.Publish(Installed());
        await Eventually.SettleAsync();
        app.Jobs.IsBusy.Should().BeFalse();
        app.Coordinator.Notices.TryRead(out _).Should().BeFalse("the player saw the project being installed");
    }

    [Fact]
    public async Task An_install_the_app_exit_cancelled_is_tried_again_at_the_next_start()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await app.StartAsync();
        await app.GameAsync(game => game.Job is not null, "the automatic install runs");

        app.Jobs.Shutdown(cancelPlayerJobs: false);

        await app.GameAsync(game => game.Job is null, "the install is cancelled");
        await Eventually.HoldsAsync(() => app.History.Stored is { Attempts.Count: 0 }, "the attempt is forgotten");
        app.Settings.Current.GetGame(Game).Should().Be(GamePreferences.Default);
    }

    [Fact]
    public async Task Updates_and_repairs_on_their_own_and_says_so()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Library.Publish(Installed(), Installed(OtherGame));
        var two = Release(ReleaseTwo, 2, File(FileTwo));
        var other = Release(Guid.NewGuid(), 4, File(VoiceFile));
        app.Catalogue.Publish(CatalogueState.Fresh, Entry(release: two), Entry(OtherProject, other, OtherGame));
        app.Catalogue.Add(Page(release: two));
        app.Catalogue.Add(Page(OtherProject, other, OtherGame));
        app.Engine.Add(Record());
        var repaired = app.Engine.Add(Record(OtherGame, Source(other.Uuid, VoiceFile, 4, project: OtherProject)));
        app.Engine.OnVerify = record => record == repaired.Id ? Modified : new VerificationResult([]);

        await app.StartAsync();

        var notices = new[] { await app.NoticeAsync(), await app.NoticeAsync() };
        notices.Should().ContainSingle(notice => notice.Kind == NoticeKind.Updated && notice.ReleaseVersion == 2);
        notices.Should().ContainSingle(notice => notice.Kind == NoticeKind.Repaired && notice.Game == OtherGame);
    }

    [Fact]
    public async Task Repairs_again_as_soon_as_a_success_no_longer_holds_the_repair_back()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Engine.Add(Record());
        app.Downloader.Cached[FileOne] = true;
        app.Engine.OnVerify = _ => Modified;

        await app.StartAsync();

        (await app.NoticeAsync()).Kind.Should().Be(NoticeKind.Repaired);
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 2, "the repaired install is checked");
        await app.GameAsync(game => game.Status.State == MyGameState.NeedsRepair && game.Job is null, "the files changed again");
        await Eventually.SettleAsync();
        app.Engine.Calls.Count(call => call == "predict").Should().Be(1, "a repair is not repeated within six hours");

        app.Time.Advance(AutomationHistory.RetryAfter);

        (await app.NoticeAsync()).Kind.Should().Be(NoticeKind.Repaired);
    }

    [Fact]
    public async Task Looks_again_at_a_game_whose_store_was_busy_once_the_library_changes()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Library.Describe(Installed(state: GameInstallState.Updating));

        await app.StartAsync();
        await Eventually.HoldsAsync(() => app.Library.ProbeCount > 0 && !app.Jobs.IsBusy, "the install waits for the store");
        await Eventually.SettleAsync();
        app.Library.ProbeCount.Should().Be(1, "a busy store is not asked again before it changed something");
        app.Catalogue.PageRequests.Should().BeEmpty();

        app.Library.Probes.Clear();
        app.Library.Publish(Installed());

        (await app.NoticeAsync()).Kind.Should().Be(NoticeKind.Installed);
    }

    [Fact]
    public async Task Remembers_the_project_and_variant_the_player_installed()
    {
        await using var app = new CoordinatorHarness(AnnounceOnly).WithGameAndProject();
        await app.StartAsync();

        await app.RunJobAsync(JobsHarness.Install(variant: "Text and voice"));

        await Eventually.HoldsAsync(() => app.Settings.Current.GetGame(Game).VariantName == "Text and voice", "the choice is kept");
        app.Settings.Current.GetGame(Game).ProjectId.Should().Be(Project.ToString("D"));
    }

    [Fact]
    public async Task A_player_who_uninstalls_is_not_offered_the_project_again()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        var record = app.Engine.Add(Record());
        await app.StartAsync();
        await app.GameAsync(game => game.Status.State == MyGameState.Installed, "the project is installed");

        await app.RunJobAsync(new UninstallJobRequest(record));

        await app.GameAsync(game => game.Status.State == MyGameState.NotInstalled && game.Job is null, "the project is uninstalled");
        app.Settings.Current.GetGame(Game).AutoInstall.Should().BeFalse();
        await Eventually.SettleAsync();
        app.Jobs.IsBusy.Should().BeFalse();
        app.Coordinator.Notices.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Waits_for_a_running_game_to_exit_instead_of_holding_a_job_open()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.Library.Runs(Game, "ExampleGame");
        await app.StartAsync();

        await app.GameAsync(game => game.Status.GameState == GameInstallState.Running, "the game runs");
        app.Jobs.IsBusy.Should().BeFalse();
        app.Catalogue.PageRequests.Should().BeEmpty();
        app.Library.Probes.Clear();
        app.RunningGames.ExitAll();

        (await app.NoticeAsync()).Kind.Should().Be(NoticeKind.Installed);
    }

    [Fact]
    public async Task Tells_once_when_an_update_lost_the_installed_release_and_does_not_reinstall_it_at_once()
    {
        await using var app = new CoordinatorHarness(AutoInstall).WithGameAndProject();
        app.PublishReleaseTwo();
        app.Engine.Add(Record());
        app.Downloader.Cached[FileOne] = true;
        app.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new InvalidOperationException("Odd.")));
        app.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallBlockedException([new FileLockedIssue("data/strings.pak", ["Viewer"])]));

        await app.StartAsync();

        var lost = await app.NoticeAsync();
        lost.Kind.Should().Be(NoticeKind.PreviousReleaseLost);
        lost.Job.Should().Be(JobKind.Update);
        var state = await app.GameAsync(game => game.Status.State == MyGameState.NotInstalled && game.Job is null, "the install is gone");
        state.Find(Game)!.LastFailure!.LostPrevious.Should().BeTrue();
        await Eventually.SettleAsync();
        app.Engine.Installs.Should().HaveCount(2, "the lost release is not installed again at once");
        app.Coordinator.Notices.TryRead(out _).Should().BeFalse();
    }
}
