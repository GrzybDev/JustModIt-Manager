using System.Collections.Immutable;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Automation;

public sealed class AutomationPolicyTests
{
    private static readonly AppSettings Automatic = AppSettings.Default with { OnboardingCompleted = true, AutoInstallProjects = true };

    private static readonly Release One = Release(ReleaseOne, 1, File(FileOne), File(VoiceFile, "Text and voice"));

    private static readonly Release Two = Release(ReleaseTwo, 2, File(FileTwo), File(Guid.NewGuid(), "Text and voice"));

    [Fact]
    public void Installs_the_recommended_variant_or_the_remembered_one_while_it_is_offered()
    {
        Decide(Game(One)).Should().BeEquivalentTo(new { Action = AutomationAction.Install, ReleaseId = ReleaseOne });
        Decide(Game(One)).Variant!.Name.Should().Be("Text only");

        var remembered = Automatic.WithGame(TestProjects.Game, preferences => preferences with { VariantName = "Text and voice" });
        Decide(Game(One, settings: remembered), remembered).Variant!.Name.Should().Be("Text and voice");

        var gone = Automatic.WithGame(TestProjects.Game, preferences => preferences with { VariantName = "Voice only" });
        Decide(Game(One, settings: gone), gone).Variant!.Name.Should().Be("Text only");
    }

    [Fact]
    public void Announces_instead_of_installing_when_automatic_installs_are_off_and_only_once()
    {
        var manual = Automatic with { AutoInstallProjects = false };

        var decision = Decide(Game(One, settings: manual), manual);

        decision.Should().BeEquivalentTo(new { Action = AutomationAction.Announce, Reason = AutomationReason.AutoInstallOff, ReleaseId = ReleaseOne });
        Decide(Game(One, settings: manual), manual, AutomationHistory.Empty.RecordAnnouncement(TestProjects.Game, ReleaseOne)).Reason
            .Should().Be(AutomationReason.AlreadyAnnounced);
    }

    [Fact]
    public void Never_installs_on_its_own_on_a_game_its_store_offers_in_the_target_language()
    {
        var game = Game(One) with { OffersTargetLanguage = true };

        Decide(game).Should().BeEquivalentTo(new { Action = AutomationAction.Announce, Reason = AutomationReason.TargetLanguageOffered });
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, false, false)]
    [InlineData(false, null, false)]
    [InlineData(false, true, true)]
    public void A_games_own_choice_overrides_the_setting(bool global, bool? own, bool installs)
    {
        var settings = (Automatic with { AutoInstallProjects = global })
            .WithGame(TestProjects.Game, preferences => preferences with { AutoInstall = own });

        Decide(Game(One, settings: settings), settings).Action.Should().Be(installs ? AutomationAction.Install : AutomationAction.Announce);
    }

    [Fact]
    public void Does_nothing_before_the_first_run_setup_nor_for_hidden_games()
    {
        Decide(Game(One), Automatic with { OnboardingCompleted = false }).Reason.Should().Be(AutomationReason.OnboardingPending);

        var hidden = Automatic.WithGame(TestProjects.Game, preferences => preferences with { IsHidden = true });
        Decide(Game(One, settings: hidden), hidden).Reason.Should().Be(AutomationReason.Hidden);
        Decide(Game(Two, Record(), hidden), hidden).Reason.Should().Be(AutomationReason.Hidden, "hidden games are not even updated");
    }

    [Fact]
    public void Leaves_the_choice_between_teams_to_the_player()
    {
        var game = Game(One) with { Project = null };

        Decide(game).Reason.Should().Be(AutomationReason.NoProjectChosen);
    }

    [Fact]
    public void Names_why_it_leaves_a_game_alone()
    {
        Decide(Game(One, Record(), repair: null)).Reason.Should().Be(AutomationReason.UpToDate);
        Decide(Game(One, installed: Installed(state: GameInstallState.Updating))).Reason.Should().Be(AutomationReason.GameNotReady);
        Decide(Game(One, Record(), detected: false)).Reason.Should().Be(AutomationReason.Orphaned);
        Decide(Game(One) with { Status = new MyGameStatus(MyGameState.Unavailable) }).Reason.Should().Be(AutomationReason.Unavailable);
        Decide(Game(One, Record(isTest: true))).Reason.Should().Be(AutomationReason.TestInstall);
        Decide(Game(One) with { Status = new MyGameStatus(MyGameState.Busy) }).Reason.Should().Be(AutomationReason.Busy);
        Decide(Game(One) with { Status = new MyGameStatus(MyGameState.BackupsUnavailable) }).Reason.Should().Be(AutomationReason.BackupsUnavailable);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, false, false)]
    [InlineData(false, null, false)]
    [InlineData(false, true, true)]
    public void Updates_as_the_settings_and_the_game_say(bool global, bool? own, bool updates)
    {
        var settings = (Automatic with { AutoUpdateProjects = global })
            .WithGame(TestProjects.Game, preferences => preferences with { AutoUpdate = own });

        var decision = Decide(Game(Two, Record(), settings), settings);

        if (updates)
        {
            decision.Should().BeEquivalentTo(new { Action = AutomationAction.Update, ReleaseId = ReleaseTwo });
        }
        else
        {
            decision.Reason.Should().Be(AutomationReason.AutoUpdateOff);
        }
    }

    [Fact]
    public void Waits_while_the_current_release_lacks_the_installed_variant()
    {
        var record = Record(source: Source(ReleaseOne, VoiceFile, 1, "Text and voice"));
        var textOnly = Release(ReleaseTwo, 2, File(FileTwo));

        Decide(Game(textOnly, record)).Reason.Should().Be(AutomationReason.VariantUnavailable);
        Decide(Game(textOnly, record, repair: RepairReason.FilesModified)).Reason.Should().Be(AutomationReason.VariantUnavailable);
    }

    [Fact]
    public void Repairs_with_the_current_release_unless_reapplying_is_off_for_the_app_or_the_game()
    {
        Decide(Game(Two, Record(), repair: RepairReason.OriginalsRestored))
            .Should().BeEquivalentTo(new { Action = AutomationAction.Repair, ReleaseId = ReleaseTwo });

        var off = Automatic with { ReapplyAfterGameUpdate = false };
        Decide(Game(Two, Record(), off, RepairReason.OriginalsRestored), off).Reason.Should().Be(AutomationReason.ReapplyOff);
        var leftAlone = Automatic.WithGame(TestProjects.Game, preferences => preferences with { AutoUpdate = false });
        Decide(Game(Two, Record(), leftAlone, RepairReason.OriginalsRestored), leftAlone).Reason.Should().Be(AutomationReason.ReapplyOff);
    }

    [Fact]
    public void Repairs_an_install_the_catalogue_does_not_have_with_its_own_release()
    {
        var game = Game(null, Record(), repair: RepairReason.FilesMissing) with { Project = null };

        Decide(game).Should().BeEquivalentTo(new { Action = AutomationAction.Repair, ReleaseId = ReleaseOne });
    }

    [Fact]
    public void Tries_an_action_on_a_release_again_after_six_hours_and_a_newer_release_at_once()
    {
        var history = AutomationHistory.Empty.RecordAttempt(TestProjects.Game, ReleaseOne, AutomationAction.Install, Now);

        Decide(Game(One), history: history, now: Now.AddHours(5)).Reason.Should().Be(AutomationReason.RecentlyTried);
        Decide(Game(One), history: history, now: Now + AutomationHistory.RetryAfter).Action.Should().Be(AutomationAction.Install);
        Decide(Game(Two), history: history, now: Now.AddHours(1)).Action.Should().Be(AutomationAction.Install);
    }

    [Fact]
    public void A_success_holds_a_repeat_back_so_a_repair_cannot_loop()
    {
        var history = AutomationHistory.Empty.RecordSuccess(TestProjects.Game, ReleaseTwo, AutomationAction.Repair, Now);

        Decide(Game(Two, Record(), repair: RepairReason.FilesModified), history: history, now: Now.AddHours(1)).Reason
            .Should().Be(AutomationReason.RecentlyTried);
    }

    [Fact]
    public void A_package_made_for_another_version_is_tried_again_only_on_another_build()
    {
        var history = AutomationHistory.Empty.RecordAttempt(TestProjects.Game, ReleaseTwo, AutomationAction.Repair, Now)
            .RecordFailure(TestProjects.Game, ReleaseTwo, AutomationAction.Repair, JobFailureReason.UnsupportedGameVersion, "100", Now, out _);
        var game = Game(Two, Record(), repair: RepairReason.FilesModified);

        Decide(game, history: history, now: Now.AddDays(3)).Reason.Should().Be(AutomationReason.RecentlyTried);
        Decide(game with { Game = game.Game! with { BuildId = "101" } }, history: history, now: Now.AddDays(3)).Action
            .Should().Be(AutomationAction.Repair);
    }

    [Fact]
    public void A_player_action_lifts_the_wait()
    {
        var history = AutomationHistory.Empty.RecordAttempt(TestProjects.Game, ReleaseOne, AutomationAction.Install, Now).Reset(TestProjects.Game);

        Decide(Game(One), history: history, now: Now.AddMinutes(1)).Action.Should().Be(AutomationAction.Install);
    }

    private static AutomationDecision Decide(MyGame game, AppSettings? settings = null, AutomationHistory? history = null,
        DateTimeOffset? now = null) =>
        AutomationPolicy.Decide(game, settings ?? Automatic, history ?? AutomationHistory.Empty, now ?? Now);

    /// <summary>The game as the player's list shows it, with <paramref name="release"/> on offer.</summary>
    private static MyGame Game(Release? release, InstallRecord? record = null, AppSettings? settings = null, RepairReason? repair = null,
        InstalledGame? installed = null, bool detected = true)
    {
        var game = installed ?? Installed();
        var input = new MyGamesInput
        {
            Library = new LibrarySnapshot
            {
                Games = detected
                    ? new ValueDictionary<GameKey, InstalledGame>(ImmutableDictionary<GameKey, InstalledGame>.Empty.Add(game.Key, game))
                    : default,
            },
            Records = record is null ? [] : [record],
            Index = new InstallableIndex(Now, release is null ? [] : [Entry(release: release)]),
            Settings = settings ?? Automatic,
            Repairs = record is not null && repair is { } reason
                ? ImmutableDictionary<Guid, RepairReason>.Empty.Add(record.Id, reason)
                : ImmutableDictionary<Guid, RepairReason>.Empty,
        };
        return MyGamesComposer.Compose(input).Single();
    }
}
