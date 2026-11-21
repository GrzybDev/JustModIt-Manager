using System.Collections.Immutable;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Core.Tests.Jobs;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class SteamUiStateTests
{
    private static readonly AppSettings Onboarded = AppSettings.Default with { OnboardingCompleted = true };

    private static readonly SteamUiStatus Plugin = new() { Delivery = SteamUiDelivery.Millennium, Reachable = true };

    [Fact]
    public async Task A_game_s_achievement_texts_follow_what_the_app_learns_of_steam_s_interface()
    {
        await using var app = new CoordinatorHarness(Onboarded, TargetLanguage.FromCode("pl"));
        app.Library.Publish(Installed());
        app.Catalogue.Publish(CatalogueState.Fresh, Entry() with
        {
            Achievements = [new ProjectAchievements { SteamAppId = 1000, Language = "polish", Translated = 13, Total = 13, Revision = 1 }],
        });
        await app.StartAsync();
        await app.GameAsync(game => game.AchievementTexts?.State == AchievementTextsState.NoDelivery, "nothing reaches Steam yet");

        var classic = new SteamUiStatus { Delivery = SteamUiDelivery.Classic, RestartNeeded = true };
        app.SteamUi.Publish(classic);

        var state = await app.GameAsync(game => game.AchievementTexts?.State == AchievementTextsState.WaitingForSteamRestart,
            "the texts wait for Steam to start again");
        state.SteamUi.Should().Be(classic);
    }

    [Fact]
    public async Task The_launch_options_a_project_needs_come_and_go_with_its_install_but_never_back_once_the_player_took_them_out()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Catalogue.Add(NeedingLaunchOptions(Page(release: Release(ReleaseOne, 1, File(FileOne)))));
        await app.StartAsync();
        var installed = (await app.RunJobAsync(JobsHarness.Install())).Installed!;
        await app.GameAsync(game => LineOf(game) == LaunchOptionsState.Manual, "nothing reaches Steam");

        app.SteamUi.Publish(Plugin with { LaunchOptions = Options("-novid") });
        var state = await app.GameAsync(game => LineOf(game) == LaunchOptionsState.Applied, "Steam has it");
        state.LaunchOptionsGames.Should().Equal(Game);
        app.SteamUi.Status.LaunchOptions[Game].Should().Be("-novid -examplelog");
        app.Engine.Records[installed.Id].LaunchOptions!.Added.Should().Equal("-examplelog");

        app.SteamUi.Publish(app.SteamUi.Status with { LaunchOptions = Options("-novid") });
        await app.GameAsync(game => LineOf(game) == LaunchOptionsState.RemovedByPlayer, "the player took it out");
        // Each look follows the state before, so this one comes after the look that would have added it again.
        app.SteamUi.Publish(app.SteamUi.Status with { InterfaceLanguage = "english" });
        await app.StateAsync(state => state.SteamUi.InterfaceLanguage == "english", "Steam's interface reports its language");
        app.SteamUi.Changes.Should().ContainSingle();

        app.Coordinator.ReapplyLaunchOptions(installed.Id);
        await app.GameAsync(game => LineOf(game) == LaunchOptionsState.Applied, "the player asked for it");
        await app.RunJobAsync(new UninstallJobRequest(app.Engine.Records[installed.Id]));

        await app.StateAsync(state => state.SteamUi.LaunchOptions.GetValueOrDefault(Game) == "-novid" && state.LaunchOptionsGames.Count == 0,
            "the uninstall took it back");
    }

    [Fact]
    public async Task What_the_app_added_for_a_gone_install_waits_for_steam_unless_another_install_needs_it()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var record = app.Engine.Add(Record() with
        {
            LaunchOptions = new InstalledLaunchOptions(["-examplelog", "-oldflag"]) { Added = ["-examplelog", "-oldflag"] },
        });
        app.Catalogue.Add(NeedingLaunchOptions(Page(release: Release(ReleaseOne, 1, File(FileOne)))));
        app.SteamUi.Publish(Plugin with { Reachable = false });
        await app.StartAsync();

        await app.RunJobAsync(new UninstallJobRequest(record));
        await app.StateAsync(state => state.LaunchOptionsGames.SequenceEqual([Game]), "the options wait for Steam");
        await Eventually.HoldsAsync(() => app.LaunchOptions.Stored?.Games.Count == 1, "the options to take back are kept");
        var installed = (await app.RunJobAsync(JobsHarness.Install())).Installed!;
        app.SteamUi.Publish(Plugin with { LaunchOptions = Options("-examplelog -oldflag -novid") });

        await app.StateAsync(state => state.SteamUi.LaunchOptions.GetValueOrDefault(Game) == "-examplelog -novid", "Steam no longer has what nothing needs");
        await app.GameAsync(game => game.Installs is [{ Record.LaunchOptions.Added: ["-examplelog"] }],
            "the new install takes over what it needs");
        app.Engine.Records[installed.Id].LaunchOptions!.Added.Should().Equal("-examplelog");
        var change = app.SteamUi.Changes.Should().ContainSingle().Which;
        (change.Add.Count, change.Remove.Single()).Should().Be((0, "-oldflag"));
        await Eventually.HoldsAsync(() => app.LaunchOptions.Stored!.Games.Count == 0, "nothing waits to be taken back");
    }

    [Fact]
    public async Task An_option_the_player_had_before_the_install_stays_the_player_s()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Catalogue.Add(NeedingLaunchOptions(Page(release: Release(ReleaseOne, 1, File(FileOne)))));
        app.SteamUi.Publish(Plugin with { LaunchOptions = Options("-examplelog -novid") });
        await app.StartAsync();

        var installed = (await app.RunJobAsync(JobsHarness.Install())).Installed!;
        await Eventually.HoldsAsync(() => app.Engine.Records[installed.Id].LaunchOptions!.Present.SequenceEqual(["-examplelog"]),
            "the app saw the option before it could add it");
        app.SteamUi.Publish(app.SteamUi.Status with { LaunchOptions = Options("-novid") });
        await app.GameAsync(game => LineOf(game) == LaunchOptionsState.RemovedByPlayer, "the player took it out");
        app.SteamUi.Publish(app.SteamUi.Status with { InterfaceLanguage = "english" });
        await app.StateAsync(state => state.SteamUi.InterfaceLanguage == "english", "Steam's interface reports its language");

        var result = await app.RunJobAsync(new UninstallJobRequest(app.Engine.Records[installed.Id]));

        app.SteamUi.Changes.Should().BeEmpty("the option was the player's to add and to take out");
        result.RemovedLaunchOptions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_steam_refused_or_a_record_could_not_keep_waits_for_steam_s_interface_to_change()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var record = app.Engine.Add(Record() with { LaunchOptions = new InstalledLaunchOptions(["-examplelog"]) });
        app.Catalogue.Add(NeedingLaunchOptions(Page(release: Release(ReleaseOne, 1, File(FileOne)))));
        app.SteamUi.Failure = LaunchOptionsFailure.Failed;
        app.SteamUi.Publish(Plugin with { LaunchOptions = Options("") });
        await app.StartAsync();

        await Eventually.HoldsAsync(() => !app.SteamUi.Changes.IsEmpty, "the app asks Steam");
        await Eventually.SettleAsync();
        app.SteamUi.Changes.Should().ContainSingle("asking again at once would be refused again");

        app.SteamUi.Failure = null;
        app.Engine.LaunchOptionsFailure = new IOException("The record is locked.");
        app.SteamUi.Publish(app.SteamUi.Status with { InterfaceLanguage = "english" });
        await Eventually.HoldsAsync(() => app.Engine.Calls.Contains("set launch options"), "Steam took it and the record is written");
        await Eventually.SettleAsync();
        app.Engine.Calls.Count(call => call == "set launch options").Should().Be(1, "writing the record again at once would fail again");

        app.Engine.LaunchOptionsFailure = null;
        app.SteamUi.Publish(app.SteamUi.Status with { InterfaceLanguage = "polish" });
        await Eventually.HoldsAsync(() => app.Engine.Records[record.Id].LaunchOptions!.Added.SequenceEqual(["-examplelog"]),
            "the record keeps what the app added once it can be written");
        app.SteamUi.Changes.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_gone_install_takes_back_what_the_app_added_while_its_folder_was_looked_for()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var record = app.Engine.Add(Record() with { LaunchOptions = new InstalledLaunchOptions(["-examplelog"]) });
        app.Engine.ForgetGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Library.Publish(Installed(OtherGame));
        await app.StartAsync();
        await Eventually.HoldsAsync(() => app.Engine.Calls.Contains("forget if gone"), "the install's folder is looked for");

        app.SteamUi.Publish(Plugin with { LaunchOptions = Options("-novid") });
        await Eventually.HoldsAsync(() => app.Engine.Records[record.Id].LaunchOptions!.Added.Count == 1, "the app added the option meanwhile");
        await Eventually.SettleAsync();
        app.Engine.Forgetting = ForgetOutcome.Forgotten;
        app.Engine.ForgetGate.SetResult();

        await app.StateAsync(state => state.SteamUi.LaunchOptions.GetValueOrDefault(Game) == "-novid", "the app took the option back");
    }

    private static LaunchOptionsState? LineOf(MyGame game) => game.Installs is [var install] ? install.LaunchOptions?.State : null;

    private static ValueDictionary<GameKey, string> Options(string options) =>
        new(ImmutableDictionary<GameKey, string>.Empty.Add(Game, options));

    private static ProjectPage NeedingLaunchOptions(ProjectPage page) =>
        page with { StorefrontLinks = [.. page.StorefrontLinks.Select(link => link with { LaunchOptions = "-examplelog" })] };
}
