using JustModIt.Manager.Agent.Notifications;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Tests.Tray;

public sealed class TrayContentTests
{
    private static readonly GameKey Game = new(StoreId.Steam, "1000");

    private static readonly AppStateView Loaded = new() { IsLoaded = true };

    [Fact]
    public void Says_it_is_starting_until_the_local_data_is_read() =>
        TrayContent.Status(new AppStateView()).Should().Be("Starting…");

    [Fact]
    public void An_idle_app_is_up_to_date_unless_a_visible_game_needs_attention()
    {
        TrayContent.Status(Loaded).Should().Be("Everything is up to date");
        TrayContent.Status(Loaded with { Games = [Attention(hidden: true)] }).Should().Be("Everything is up to date");
        TrayContent.Status(Loaded with { Games = [Attention(), Attention()] }).Should().Be("2 games need your attention");
    }

    [Theory]
    [InlineData(JobKind.Install, "Installing for Example Game…")]
    [InlineData(JobKind.TestInstall, "Installing for Example Game…")]
    [InlineData(JobKind.Update, "Updating for Example Game…")]
    [InlineData(JobKind.Repair, "Repairing for Example Game…")]
    [InlineData(JobKind.Uninstall, "Uninstalling from Example Game…")]
    [InlineData(JobKind.MoveBackups, "Moving the backups of Example Game…")]
    [InlineData(JobKind.FollowMove, "Following Example Game to its new folder…")]
    public void One_job_is_named_with_its_game(JobKind kind, string status) =>
        TrayContent.Status(Loaded with { Jobs = [Job(kind)] }).Should().Be(status);

    [Fact]
    public void A_job_waiting_for_the_game_asks_to_close_it() =>
        TrayContent.Status(Loaded with { Jobs = [Job(JobKind.Update) with { Stage = JobStage.Waiting, WaitReason = JobWaitReason.GameRunning }] })
            .Should().Be("Waiting for Example Game to close…");

    [Fact]
    public void Jobs_come_before_checks_and_a_pending_exit_before_jobs()
    {
        var busy = Loaded with
        {
            Jobs = [Job(JobKind.Install), Job(JobKind.Update)],
            Activity = new ActivityView { IsChecking = true },
            Catalogue = new CatalogueView { State = CatalogueState.Refreshing },
        };

        TrayContent.Status(busy).Should().Be("2 jobs in progress");
        TrayContent.Status(busy with { Jobs = [] }).Should().Be("Checking your games…");
        TrayContent.Status(busy with { Activity = new ActivityView { IsExitPending = true } }).Should().Be("Exits once the running jobs are done");
    }

    [Fact]
    public void The_tooltip_names_the_app_and_what_it_does() =>
        TrayContent.Tooltip(Loaded, "Example App").Should().Be("Example App – Everything is up to date");

    [Fact]
    public void The_menu_opens_the_app_shows_the_status_checks_and_exits()
    {
        var menu = TrayContent.Menu(Loaded, "Example App");

        menu.Select(item => (item.Command, item.Text, item.IsEnabled, item.IsDefault)).Should().Equal(
            (TrayCommands.Open, "Open Example App", true, true),
            (TrayCommands.Status, "Everything is up to date", false, false),
            ("", "", true, false),
            (TrayCommands.CheckNow, "Check now", true, false),
            ("", "", true, false),
            (TrayCommands.Exit, "Exit", true, false));
    }

    [Fact]
    public void While_jobs_run_the_menu_offers_to_exit_after_them_or_now_and_a_waiting_exit_can_only_be_cut_short()
    {
        var busy = Loaded with { Jobs = [Job(JobKind.Install)] };

        Exits(busy).Should().Equal((TrayCommands.Exit, "Exit after the jobs finish"), (TrayCommands.ExitNow, "Exit now"));
        Exits(busy with { Activity = new ActivityView { IsExitPending = true } }).Should().Equal((TrayCommands.ExitNow, "Exit now"));
    }

    [Theory]
    [InlineData(NoticeKind.Installed, "Installed for Example Game", "The project is ready. Have fun!")]
    [InlineData(NoticeKind.Repaired, "Installed again for Example Game", "The store changed the game's files, so the project was installed again.")]
    [InlineData(NoticeKind.PreviousReleaseLost, "The update for Example Game failed",
        "The previous release could not be put back, so the game has its original files. You can install the project again.")]
    [InlineData(NoticeKind.RepairNeeded, "Repair needed for Example Game",
        "The store changed the game's files, so the project needs to be installed again.")]
    [InlineData(NoticeKind.ChoiceNeeded, "Several projects for Example Game",
        "Different teams made them, so one of them needs to be chosen.")]
    public void Notices_are_told_in_the_apps_words(NoticeKind kind, string title, string message) =>
        NoticeTexts.For(new AutomationNotice(kind) { Game = Game, GameName = "Example Game" }).Should().Be((title, message));

    [Theory]
    [InlineData(NoticeKind.UpdateAvailable, null, "Update for Example Project (Example Game)", "A new release is ready to install.")]
    [InlineData(NoticeKind.Repaired, null, "Installed Example Project again (Example Game)",
        "The store changed the game's files, so the project was installed again.")]
    [InlineData(NoticeKind.Failed, JobKind.Update, "Could not update Example Project (Example Game)", "Open the app to see what went wrong.")]
    [InlineData(NoticeKind.PreviousReleaseLost, JobKind.Update, "The update of Example Project (Example Game) failed",
        "The previous release could not be put back, so the files it had changed are original again. You can install the project again.")]
    [InlineData(NoticeKind.Forgotten, null, "Example Project (Example Game) is no longer installed",
        "The game's folder is gone, so the project installed in it was removed from the list.")]
    public void A_notice_about_one_of_several_installs_on_a_game_names_its_project(NoticeKind kind, JobKind? job, string title,
        string message) =>
        NoticeTexts.For(new AutomationNotice(kind) { Game = Game, GameName = "Example Game", ProjectName = "Example Project", Job = job })
            .Should().Be((title, message));

    [Fact]
    public void An_available_project_names_its_team_unless_the_game_speaks_the_language_already()
    {
        var notice = new AutomationNotice(NoticeKind.Available) { Game = Game, GameName = "Example Game", TeamName = "Lantern Team" };

        NoticeTexts.For(notice).Should().Be(("New for Example Game", "Lantern Team made a project for it that you can install."));
        NoticeTexts.For(notice with { TeamName = null }).Message.Should().Be("There is a project for it that you can install.");
        NoticeTexts.For(notice with { OffersTargetLanguage = true }).Message
            .Should().Be("It already comes in your language, but you can also install a project for it.");
        NoticeTexts.For(new AutomationNotice(NoticeKind.AvailableSummary) { Games = [Game, Game] })
            .Should().Be(("New for your games", "2 games have a project you can install."));
    }

    [Fact]
    public void A_summary_tells_projects_updates_and_repairs_apart()
    {
        GameKey updated = new(StoreId.Steam, "2000"), broken = new(StoreId.Gog, "3000");
        var notice = new AutomationNotice(NoticeKind.AvailableSummary) { Games = [Game, updated, broken], Updates = [updated], Repairs = [broken] };

        NoticeTexts.For(notice).Message.Should().Be("1 game has a project you can install. 1 game has an update ready to install. "
            + "1 game needs its project installed again.");
        NoticeTexts.For(notice with { Games = [updated, broken] }).Message.Should().Be("1 game has an update ready to install. "
            + "1 game needs its project installed again.");
        NoticeTexts.For(notice with { Games = [Game, broken], Updates = [broken] }).Message.Should().Be("1 game has a project you can "
            + "install. 1 game has an update ready to install. 1 game needs its project installed again.",
            "one install on a game may have an update while another needs a repair");
    }

    [Fact]
    public void A_forgotten_install_says_whether_its_originals_are_kept()
    {
        var notice = new AutomationNotice(NoticeKind.Forgotten) { Game = Game, GameName = "Example Game" };

        NoticeTexts.For(notice).Should().Be(("Example Game is no longer installed",
            "The game's folder is gone, so the project installed in it was removed from the list."));
        NoticeTexts.For(notice with { KeptBackups = true }).Message.Should().EndWith(
            "The backed-up original files are kept and can be removed in the settings.");
    }

    [Theory]
    [InlineData(JobKind.Install, "Could not install for Example Game")]
    [InlineData(JobKind.Update, "Could not update for Example Game")]
    [InlineData(JobKind.Uninstall, "Could not uninstall from Example Game")]
    [InlineData(JobKind.MoveBackups, "Something went wrong with Example Game")]
    public void A_failure_names_what_failed(JobKind job, string title) =>
        NoticeTexts.For(new AutomationNotice(NoticeKind.Failed) { Game = Game, GameName = "Example Game", Job = job })
            .Should().Be((title, "Open the app to see what went wrong."));

    [Fact]
    public void A_failure_says_when_the_release_is_no_longer_available()
    {
        var notice = new AutomationNotice(NoticeKind.Failed)
        {
            Game = Game,
            GameName = "Example Game",
            Job = JobKind.Update,
            Failure = new JobFailure(JobFailureReason.ReleaseUnavailable),
        };

        NoticeTexts.For(notice).Message.Should().Be("The release is no longer available for download, so nothing was changed.");
    }

    [Fact]
    public void An_update_names_the_version_when_the_team_named_the_release()
    {
        var notice = new AutomationNotice(NoticeKind.Updated) { Game = Game, GameName = "Example Game", ReleaseLabel = "v2" };

        NoticeTexts.For(notice).Message.Should().Be("Version 2 is installed.");
        NoticeTexts.For(notice with { ReleaseLabel = "Final cut" }).Message.Should().Be("Version Final cut is installed.");
        NoticeTexts.For(notice with { ReleaseLabel = "Version" }).Message.Should().Be("The newest release is installed.");
        NoticeTexts.For(notice with { ReleaseLabel = null }).Message.Should().Be("The newest release is installed.");
        NoticeTexts.For(notice with { Kind = NoticeKind.UpdateAvailable }).Should().Be(("Update for Example Game",
            "Version 2 is ready to install."));
        NoticeTexts.For(notice with { Kind = NoticeKind.UpdateAvailable, ReleaseLabel = null }).Message
            .Should().Be("A new release is ready to install.");
    }

    private static IEnumerable<(string Command, string Text)> Exits(AppStateView state) =>
        TrayContent.Menu(state, "Example App").Where(item => item.Command is TrayCommands.Exit or TrayCommands.ExitNow)
            .Select(item => (item.Command, item.Text));

    private static GameView Attention(bool hidden = false) => new()
    {
        Key = Game,
        Name = "Example Game",
        Status = new GameStatusView(MyGameState.NeedsRepair),
        NeedsAttention = true,
        IsHidden = hidden,
    };

    private static JobView Job(JobKind kind) => new()
    {
        Id = Guid.NewGuid(),
        Game = Game,
        GameName = "Example Game",
        Kind = kind,
        Origin = JobOrigin.User,
        Stage = JobStage.Installing,
    };
}
