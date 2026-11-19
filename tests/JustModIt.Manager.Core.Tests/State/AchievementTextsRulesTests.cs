using System.Collections.Immutable;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class AchievementTextsRulesTests
{
    private static readonly AppSettings Onboarded = AppSettings.Default with { OnboardingCompleted = true };

    private static readonly SteamUiStatus Plugin = new() { Delivery = SteamUiDelivery.Millennium, Reachable = true, InterfaceLanguage = "polish" };

    private static readonly TargetLanguage Polish = TargetLanguage.FromCode("pl");

    [Fact]
    public void A_steam_game_with_only_achievement_texts_is_listed_with_them_on()
    {
        var game = Compose(Input([TextsOnly(Project)])).Should().ContainSingle().Which;

        game.Projects.Should().BeEmpty();
        game.AchievementTexts.Should().BeEquivalentTo(new
        {
            Project,
            TeamName = "Lantern Team",
            Texts = new { Translated = 11, Total = 13, Revision = 4 },
            WithInstall = false,
            IsOn = true,
            State = AchievementTextsState.On,
        });
    }

    [Fact]
    public void Texts_in_another_language_than_the_app_s_apply_to_no_game()
    {
        Compose(Input([TextsOnly(Project, language: "german")])).Should().BeEmpty();
        Compose(Input([TextsOnly(Project, language: "german")]) with { Language = TargetLanguage.None }).Single().AchievementTexts!
            .Texts.Language.Should().Be("german", "an app without a target language takes the site's texts");
    }

    [Fact]
    public void Steam_s_official_texts_give_way_only_to_the_team_whose_project_is_installed()
    {
        var official = Installable(Project, hasOfficial: true);

        Compose(Input([official])).Single().AchievementTexts.Should().BeNull();
        var texts = Compose(Input([official], [Record()])).Single().AchievementTexts!;
        (texts.Project, texts.WithInstall, texts.IsOn).Should().Be((Project, true, true));
    }

    [Fact]
    public void The_installed_project_s_texts_win_and_otherwise_the_texts_follow_the_project_chosen_for_the_game()
    {
        var installed = Installable(Project);
        var other = TextsOnly(OtherProject, "paper-team");
        var chosenOther = Onboarded.WithGame(Game, preferences => preferences with { ProjectId = OtherProject });

        Compose(Input([installed, other], [Record()]) with { Settings = chosenOther }).Single().AchievementTexts!.Project
            .Should().Be(Project);
        Compose(Input([installed, other]) with { Settings = chosenOther }).Single().AchievementTexts!.Project.Should().Be(OtherProject);
        Compose(Input([installed, other])).Single().AchievementTexts.Should().BeNull("several teams have texts and none is chosen");
        Compose(Input([installed, other]) with { Settings = Onboarded with { PreferredTeams = [TeamOf("paper-team")] } }).Single()
            .AchievementTexts!.Project.Should().Be(OtherProject);
        Compose(Input([Entry(release: Release(ReleaseOne, 1, File(FileOne))), other], [Record()])).Single().AchievementTexts!.Project
            .Should().Be(OtherProject, "the installed project has no texts and Steam has none in the language");
    }

    [Theory]
    [InlineData(true, true, true, null, true)]
    [InlineData(true, false, true, null, false)]
    [InlineData(true, true, false, null, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    public void The_player_s_choice_for_a_game_wins_over_coming_on_by_themselves_until_all_texts_are_switched_off(bool onboarded,
        bool allTexts, bool automatic, bool? choice, bool isOn)
    {
        var settings = AppSettings.Default with
        {
            OnboardingCompleted = onboarded,
            AchievementTexts = allTexts,
            AutoAchievementTexts = automatic,
        };

        var texts = Compose(Input([TextsOnly(Project)]) with
        {
            Settings = settings.WithGame(Game, preferences => preferences with { AchievementTexts = choice }),
        }).Single().AchievementTexts!;

        texts.IsOn.Should().Be(isOn);
    }

    [Fact]
    public void Tells_first_what_keeps_steam_from_showing_the_texts()
    {
        var restart = Plugin with { Reachable = false, RestartNeeded = true, Hooks = new SteamUiHooks(false, false) };
        (SteamUiStatus Steam, AchievementTextsState State, bool IsOn)[] cases =
        [
            (SteamUiStatus.None, AchievementTextsState.NoDelivery, true),
            (Plugin with { Reachable = false, PluginDisabled = true }, AchievementTextsState.NoDelivery, true),
            (Plugin, AchievementTextsState.Off, false),
            (restart with { RunningGame = OtherGame }, AchievementTextsState.RestartBlocked, true),
            (restart, AchievementTextsState.WaitingForSteamRestart, true),
            (Plugin with { Hooks = new SteamUiHooks(false, true), InterfaceLanguage = "english" }, AchievementTextsState.HookBlocked, true),
            (Plugin with { InterfaceLanguage = "english" }, AchievementTextsState.SteamInOtherLanguage, true),
            (Plugin with { Reachable = false, InterfaceLanguage = "" }, AchievementTextsState.On, true),
        ];

        foreach (var (steam, state, isOn) in cases)
        {
            var settings = Onboarded.WithGame(Game, preferences => preferences with { AchievementTexts = isOn ? null : false });
            Compose(Input([TextsOnly(Project)]) with { SteamUi = steam, Settings = settings }).Single().AchievementTexts!.State
                .Should().Be(state, steam.ToString());
        }
    }

    private static ImmutableArray<MyGame> Compose(MyGamesInput input) => MyGamesComposer.Compose(input);

    private static MyGamesInput Input(IndexEntry[] entries, InstallRecord[]? records = null) => new()
    {
        Library = new LibrarySnapshot
        {
            Games = new ValueDictionary<GameKey, InstalledGame>(ImmutableDictionary<GameKey, InstalledGame>.Empty.Add(Game, Installed())),
        },
        Records = records ?? [],
        Index = new InstallableIndex(Now, entries),
        Settings = Onboarded,
        Language = Polish,
        SteamUi = Plugin,
    };

    private static IndexEntry TextsOnly(Guid project, string teamSlug = "lantern-team", string language = "polish") =>
        WithTexts(Entry(project, teamSlug: teamSlug), language, hasOfficial: false);

    private static IndexEntry Installable(Guid project, bool hasOfficial = false) =>
        WithTexts(Entry(project, Release(ReleaseOne, 1, File(FileOne))), "polish", hasOfficial);

    private static IndexEntry WithTexts(IndexEntry entry, string language, bool hasOfficial) => entry with
    {
        Achievements = [new ProjectAchievements
        {
            SteamAppId = 1000,
            Language = language,
            Translated = 11,
            Total = 13,
            Revision = 4,
            HasOfficial = hasOfficial,
        }],
    };
}
