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
    public void Treats_a_game_its_store_offers_in_the_target_language_as_the_owner_decided()
    {
        Decide(Game(One) with { OffersTargetLanguage = true })
            .Should().BeEquivalentTo(new { Action = AutomationAction.Announce, Reason = AutomationReason.TargetLanguageOffered });
        Decide(Game(Two, Record()) with { OffersTargetLanguage = true }).Action
            .Should().Be(AutomationAction.Update, "a project the player installed there by hand keeps its updates");
        Decide(Game(One, Record(), repair: RepairReason.FilesModified) with { OffersTargetLanguage = true }).Action
            .Should().Be(AutomationAction.Repair, "and its repairs");
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
        var hiddenManual = hidden with { AutoUpdateProjects = false };
        Decide(Game(Two, Record(), hiddenManual), hiddenManual).Reason.Should().Be(AutomationReason.Hidden, "nor told of updates");
    }

    [Fact]
    public void Tells_once_of_a_choice_between_teams_and_leaves_it_to_the_player()
    {
        var game = Game(One) with { Project = null };

        Decide(game).Should().BeEquivalentTo(new { Action = AutomationAction.Announce, Reason = AutomationReason.NoProjectChosen });
        Decide(game, history: AutomationHistory.Empty.RecordAnnouncement(TestProjects.Game, ReleaseOne)).Reason
            .Should().Be(AutomationReason.AlreadyAnnounced);
    }

    [Fact]
    public void Names_why_it_leaves_a_game_alone()
    {
        Decide(Game(One, Record(), repair: null)).Reason.Should().Be(AutomationReason.UpToDate);
        Decide(Game(One, installed: Installed(state: GameInstallState.Updating))).Reason.Should().Be(AutomationReason.GameNotReady);
        Decide(Game(One, Record(), detected: false)).Reason.Should().Be(AutomationReason.Orphaned);
        Decide(Game(One, Record(isTest: true))).Reason.Should().Be(AutomationReason.TestInstall);
        Decide(Game(One) with { Status = new MyGameStatus(MyGameState.Busy) }).Reason.Should().Be(AutomationReason.Busy);
        Decide(Game(Two, Record(), backupsUnavailable: true)).Reason.Should().Be(AutomationReason.BackupsUnavailable);
    }

    [Fact]
    public void Decides_for_each_install_and_never_adds_a_project_to_a_game_that_has_one()
    {
        var updated = Record();
        var broken = Record(source: Source(ReleaseOne, FileOne, 1, project: OtherProject));
        var game = MyGamesComposer.Compose(new MyGamesInput
        {
            Library = Library(Installed()),
            Records = [updated, broken],
            Index = new InstallableIndex(Now, [Entry(release: Two), Entry(OtherProject, One, teamSlug: "paper-team"),
                Entry(Guid.NewGuid(), Release(Guid.NewGuid(), 1, File(Guid.NewGuid())), teamSlug: "quill-team")]),
            Settings = Automatic,
            Repairs = ImmutableDictionary<Guid, RepairReason>.Empty.Add(broken.Id, RepairReason.FilesModified),
        }).Single();

        var decisions = AutomationPolicy.Decide(game, Automatic, AutomationHistory.Empty, Now);

        decisions.Select(decision => (decision.Action, decision.Install!.Record.Id))
            .Should().BeEquivalentTo([(AutomationAction.Update, updated.Id), (AutomationAction.Repair, broken.Id)]);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, false, false)]
    [InlineData(false, null, false)]
    [InlineData(false, true, true)]
    public void Updates_or_announces_the_update_once_as_the_settings_and_the_game_say(bool global, bool? own, bool updates)
    {
        var settings = (Automatic with { AutoUpdateProjects = global })
            .WithGame(TestProjects.Game, preferences => preferences with { AutoUpdate = own });
        var game = Game(Two, Record(), settings);

        var decision = Decide(game, settings);

        if (updates)
        {
            decision.Should().BeEquivalentTo(new { Action = AutomationAction.Update, ReleaseId = ReleaseTwo });
        }
        else
        {
            decision.Should().BeEquivalentTo(new { Action = AutomationAction.Announce, Reason = AutomationReason.AutoUpdateOff, ReleaseId = ReleaseTwo });
            Decide(game, settings, AutomationHistory.Empty.RecordAnnouncement(TestProjects.Game, ReleaseTwo)).Reason
                .Should().Be(AutomationReason.AlreadyAnnounced);
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
    public void Repairs_with_the_current_release_unless_reapplying_is_off_and_then_announces_it_once_per_build()
    {
        Decide(Game(Two, Record(), repair: RepairReason.OriginalsRestored))
            .Should().BeEquivalentTo(new { Action = AutomationAction.Repair, ReleaseId = ReleaseTwo });

        var off = Automatic with { ReapplyAfterGameUpdate = false };
        var record = Record();
        var broken = Game(Two, record, off, RepairReason.OriginalsRestored);
        var told = AutomationHistory.Empty.RecordRepairAnnouncement(record.Id, "100");
        Decide(broken, off).Should().BeEquivalentTo(new { Action = AutomationAction.Announce, Reason = AutomationReason.ReapplyOff });
        Decide(broken, off, told).Reason.Should().Be(AutomationReason.AlreadyAnnounced);
        Decide(broken with { Game = broken.Game! with { BuildId = "101" } }, off, told).Action
            .Should().Be(AutomationAction.Announce, "another build broke it again");
        var leftAlone = Automatic.WithGame(TestProjects.Game, preferences => preferences with { AutoUpdate = false });
        Decide(Game(Two, Record(), leftAlone, RepairReason.OriginalsRestored), leftAlone).Reason.Should().Be(AutomationReason.ReapplyOff);
    }

    [Fact]
    public void Repairs_an_install_the_catalogue_does_not_have_with_its_own_release()
    {
        var game = Game(null, Record(), repair: RepairReason.FilesMissing);

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

    [Theory]
    [InlineData(JobFailureReason.UnsupportedGameVersion, AutomationAction.Repair)]
    [InlineData(JobFailureReason.NewReleaseIncompatible, AutomationAction.Update)]
    public void A_package_made_for_another_version_is_tried_again_only_on_another_build(JobFailureReason failure, AutomationAction action)
    {
        var history = AutomationHistory.Empty.RecordAttempt(TestProjects.Game, ReleaseTwo, action, Now)
            .RecordFailure(TestProjects.Game, ReleaseTwo, action, failure, "100", Now, out _);
        var game = Game(Two, Record(), repair: action == AutomationAction.Repair ? RepairReason.FilesModified : null);

        Decide(game, history: history, now: Now.AddDays(3)).Reason.Should().Be(AutomationReason.RecentlyTried);
        Decide(game with { Game = game.Game! with { BuildId = "101" } }, history: history, now: Now.AddDays(3)).Action
            .Should().Be(action);
    }

    [Fact]
    public void A_release_made_for_another_game_is_not_tried_again_even_on_another_build()
    {
        var history = AutomationHistory.Empty.RecordAttempt(TestProjects.Game, ReleaseTwo, AutomationAction.Repair, Now)
            .RecordFailure(TestProjects.Game, ReleaseTwo, AutomationAction.Repair, JobFailureReason.OtherGame, "100", Now, out _);
        var game = Game(Two, Record(), repair: RepairReason.FilesModified);

        Decide(game with { Game = game.Game! with { BuildId = "101" } }, history: history, now: Now.AddDays(3)).Reason
            .Should().Be(AutomationReason.RecentlyTried);
    }

    [Fact]
    public void A_player_action_lifts_the_wait()
    {
        var history = AutomationHistory.Empty.RecordAttempt(TestProjects.Game, ReleaseOne, AutomationAction.Install, Now).Reset(TestProjects.Game);

        Decide(Game(One), history: history, now: Now.AddMinutes(1)).Action.Should().Be(AutomationAction.Install);
    }

    private static AutomationDecision Decide(MyGame game, AppSettings? settings = null, AutomationHistory? history = null,
        DateTimeOffset? now = null) =>
        AutomationPolicy.Decide(game, settings ?? Automatic, history ?? AutomationHistory.Empty, now ?? Now).Single();

    private static MyGame Game(Release? release, InstallRecord? record = null, AppSettings? settings = null, RepairReason? repair = null,
        InstalledGame? installed = null, bool detected = true, bool backupsUnavailable = false)
    {
        var input = new MyGamesInput
        {
            Library = detected ? Library(installed ?? Installed()) : new LibrarySnapshot(),
            Records = record is null ? [] : [record],
            Index = new InstallableIndex(Now, release is null ? [] : [Entry(release: release)]),
            Settings = settings ?? Automatic,
            Repairs = record is not null && repair is { } reason
                ? ImmutableDictionary<Guid, RepairReason>.Empty.Add(record.Id, reason)
                : ImmutableDictionary<Guid, RepairReason>.Empty,
            UnavailableBackups = record is not null && backupsUnavailable ? ImmutableHashSet.Create(record.Id) : ImmutableHashSet<Guid>.Empty,
        };
        return MyGamesComposer.Compose(input).Single();
    }

    private static LibrarySnapshot Library(InstalledGame game) => new()
    {
        Games = new ValueDictionary<GameKey, InstalledGame>(ImmutableDictionary<GameKey, InstalledGame>.Empty.Add(game.Key, game)),
    };
}
