using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Pages.Settings;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class AchievementTextsTests
{
    private static readonly ProjectOfferView Ember = Offer("Ember Hollow");

    private static readonly AchievementTexts Listed = new()
    {
        SteamAppId = 4100,
        Language = "polish",
        Translated = 2,
        Total = 2,
        Revision = 1,
        Achievements =
        [
            new AchievementText { ApiName = "ACH_LANTERN", Name = "Lantern keeper", Description = "Light every lantern in the hollow." },
            new AchievementText { ApiName = "ACH_SECRET", Name = "Under the roots", Description = "Find the hidden cellar.", Hidden = true },
        ],
    };

    [AvaloniaFact]
    public async Task Texts_turn_on_from_their_row_and_unfold_the_translated_names_once_Steam_shows_them()
    {
        var game = WithTexts(Installed("Ember Hollow", "4100", offer: Ember), AchievementTextsState.Off, isOn: false);
        using var ui = Open(game);
        var row = Page(ui).Achievements;

        (row.Title, row.Detail).Should().Be(("Tłumaczenie osiągnięć",
            "Lantern Team · 13 z 13 przetłumaczonych. Steam pokaże je w bibliotece, nakładce w grze i powiadomieniach."));
        ui.Find<Button>("AchievementsListToggle").IsEffectivelyVisible.Should().BeFalse("the list belongs to texts that are on");
        await RenderAsync(ui, "achievements-off");
        ui.Click(ui.Find<Button>("AchievementsSwitch"));
        ui.Agent.Requests.OfType<SetAchievementTexts>().Should().Equal(new SetAchievementTexts(game.Key, IsOn: true));

        ui.Push(State(WithTexts(game, AchievementTextsState.On, isOn: true)) with { Version = 2, SteamUi = Millennium });
        (row.Title, row.Detail, row.DetailTone, row.Notice).Should().Be(("Tłumaczenie osiągnięć włączone", "13 z 13 · przez wtyczkę Millennium",
            Tone.Success, (AchievementTextsNotice?)null));
        AutomationProperties.GetName(ui.Find<Button>("AchievementsSwitch")).Should().Be("Wyłącz tłumaczenie osiągnięć");
        ui.Agent.Answer = request => request is GetAchievementTexts ? new AchievementTextsResult(Listed) : null;
        ui.Click(ui.Find<Button>("AchievementsListToggle"));
        await UiHarness.WaitUntilAsync(() => row.Items.Count == 2);
        row.Items.Should().Equal(new AchievementTextItem("Lantern keeper", "Light every lantern in the hollow."),
            new AchievementTextItem("Under the roots", "Steam pokaże opis po odblokowaniu osiągnięcia."));
        ui.Find<ItemsControl>("AchievementsList").IsEffectivelyVisible.Should().BeTrue();
        ui.Unnamed().Should().BeEmpty();
        await RenderAsync(ui, "achievements-on-list");

        ui.Push(State(WithTexts(game, AchievementTextsState.On, isOn: true)) with { Version = 3, SteamUi = Millennium });
        row.IsListShown.Should().BeTrue("a state that changes nothing else keeps the list unfolded");
        ui.Agent.Requests.OfType<GetAchievementTexts>().Should().ContainSingle("the list is read once while the texts stay the same");
    }

    [AvaloniaFact]
    public async Task Steam_starts_again_from_the_row_unless_a_Steam_game_runs()
    {
        var game = WithTexts(Installed("Ember Hollow", "4100", offer: Ember), AchievementTextsState.WaitingForSteamRestart, isOn: true);
        using var ui = Open(game, Classic with { RestartNeeded = true });
        var row = Page(ui).Achievements;

        row.Notice!.Title.Should().Be("Widoczne po ponownym uruchomieniu Steama");
        row.Notice.Text.Should().Be("Steam musi wystartować ponownie, żeby otworzyć port trybu klasycznego. Potem polskie teksty pojawią się same.");
        row.Detail.Should().Be("13 z 13 · przez tryb klasyczny");
        await ui.RenderBothAsync("achievements-restart", () => ui.Find<Border>("AchievementsRow").BringIntoView());
        ui.Click(ui.Find<Button>("RestartSteam"));
        ui.Agent.Requests.OfType<RestartSteam>().Should().ContainSingle();

        var running = Available("Quiet Harbor", "4200");
        ui.Push(State(WithTexts(game, AchievementTextsState.RestartBlocked, isOn: true), running) with
        {
            Version = 2,
            SteamUi = Classic with { RestartNeeded = true, RunningGame = running.Key, RunningGameName = running.Name },
        });
        ui.Find<Button>("RestartSteam").IsEnabled.Should().BeFalse();
        row.Notice!.Running.Should().Be("Uruchomiona: Quiet Harbor");
        await RenderAsync(ui, "achievements-restart-blocked");
    }

    [AvaloniaFact]
    public async Task Every_state_that_keeps_the_texts_from_Steam_says_why_and_the_way_out()
    {
        var installed = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(WithTexts(installed, AchievementTextsState.On, isOn: true, translated: 11), Millennium);
        var row = Page(ui).Achievements;
        (row.Detail, row.DetailTone).Should().Be(
            ("11 z 13 · 2 osiągnięcia zostają z oryginalnymi tekstami, dopóki grupa ich nie przetłumaczy", Tone.Warning));
        await RenderAsync(ui, "achievements-missing");

        Show(ui, WithTexts(installed, AchievementTextsState.SteamInOtherLanguage, isOn: true), Millennium, version: 2);
        (row.Notice!.Title, row.Notice.Text).Should().Be(("Steam ma interfejs w innym języku", "Steam pokazuje osiągnięcia w języku swojego "
            + "interfejsu. Polskie teksty pojawią się po przełączeniu Steama na język polski (Steam › Ustawienia › Interfejs)."));
        await RenderAsync(ui, "achievements-other-language");

        Show(ui, WithTexts(installed, AchievementTextsState.HookBlocked, isOn: true), Classic, version: 3);
        (row.Notice!.Title, row.Notice.IsWarning).Should().Be(("Ta wersja Steama blokuje podmianę", true));
        await RenderAsync(ui, "achievements-hook-blocked");

        Show(ui, WithTexts(installed, AchievementTextsState.On, isOn: true, withInstall: true), Millennium, version: 4);
        row.Detail.Should().Be("13 z 13 · razem ze spolszczeniem Lantern Team, zamiast oficjalnych polskich tekstów");
        await RenderAsync(ui, "achievements-with-install");

        var choice = Choice("Ember Hollow", "4100");
        Show(ui, WithTexts(choice with { Project = choice.Projects[0] }, AchievementTextsState.Off, isOn: false), Millennium, version: 5);
        row.Detail.Should().Be("Tłumaczenie z projektu wybranego dla gry: Lantern Team · 13 z 13");
        await RenderAsync(ui, "achievements-chosen");

        // The texts come on by themselves, as the settings have them by default, and nothing brings them to Steam.
        Show(ui, WithTexts(installed, AchievementTextsState.NoDelivery, isOn: true), new SteamUiView(), version: 6,
            settings => settings with { AchievementClassicMode = false });
        (row.Notice!.Title, row.CanSwitch, row.Title).Should().Be(("Tryb klasyczny jest wyłączony", false, "Tłumaczenie osiągnięć"));
        row.Detail.Should().Be("Lantern Team · 13 z 13 przetłumaczonych");
        ui.Find<Button>("AchievementsListToggle").IsEffectivelyVisible.Should().BeFalse();
        await RenderAsync(ui, "achievements-no-delivery");

        Show(ui, WithTexts(installed, AchievementTextsState.NoDelivery, isOn: true), Millennium with { Reachable = false, PluginDisabled = true },
            version: 7);
        (row.Notice!.Title, row.Notice.Text).Should().Be(("Wtyczka jest wyłączona w Millennium", "Polskie teksty osiągnięć trafiają do Steama "
            + "przez wtyczkę menadżera, wyłączoną na liście wtyczek Millennium. Po jej włączeniu pojawią się przy następnym uruchomieniu Steama."));
        await RenderAsync(ui, "achievements-plugin-off");
        ui.Click(ui.Find<Button>("AchievementsSettings"));
        ui.Shell.Navigator.Current.Should().BeOfType<SettingsPageViewModel>();
    }

    [AvaloniaFact]
    public async Task A_game_listed_only_for_its_achievement_texts_has_a_card_and_a_page_without_an_install()
    {
        var game = WithTexts(new GameView
        {
            Key = SteamKey("4300"),
            Name = "Quiet Harbor",
            Status = new GameStatusView(MyGameState.NotInstalled),
            Detected = new DetectedGameView { InstallDirectory = @"D:\Games\Quiet Harbor", LibraryId = "steam-library" },
        }, AchievementTextsState.On, isOn: true);
        var installed = WithTexts(Installed("Ember Hollow", "4100", offer: Ember), AchievementTextsState.On, isOn: true);
        using var ui = new UiHarness(State(game, installed, Available("Lantern Bay", "4400")) with { SteamUi = Millennium });
        ui.Shell.Navigator.Navigate(new LibraryRoute());
        UiHarness.Settle();
        var card = ui.Find<Button>().Select(button => button.DataContext).OfType<GameCardViewModel>().Single(shown => shown.Key == game.Key);

        (card.Line.Label, card.HasAchievementTexts, card.TeamName).Should().Be(("Tłumaczenie osiągnięć włączone", true, "Lantern Team"));
        var library = (LibraryPageViewModel)ui.Shell.Navigator.Current!;
        library.Query = "lantern team";
        UiHarness.Settle();
        library.GridItems.OfType<GameCardViewModel>().Should().Contain(card, "the card names the team of its texts");
        library.Query = "";
        UiHarness.Settle();
        card.AccessibleName.Should().Be("Quiet Harbor – Tłumaczenie osiągnięć włączone", "its line says it already");
        var cards = ui.Find<Button>().Select(button => button.DataContext).OfType<GameCardViewModel>().ToList();
        cards.Single(other => other.Key == installed.Key).AccessibleName.Should().EndWith(", tłumaczenie osiągnięć włączone");
        ui.Find<Border>().Count(chip => chip.Name == "AchievementsChip" && chip.IsEffectivelyVisible).Should().Be(2,
            "the game without texts wears no chip");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("achievements-library");

        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        var page = Page(ui);
        (page.IsNotInstalled, page.Step, page.Achievements.IsShown).Should().Be((false, (NextStep?)null, true));
        await RenderAsync(ui, "achievements-only");
    }

    [AvaloniaFact]
    public async Task Texts_that_came_on_by_themselves_show_in_the_corner_with_Steam_s_restart_where_it_waits()
    {
        var ember = Installed("Ember Hollow", "4100", offer: Ember);
        var harbor = Available("Quiet Harbor", "4300");
        var bay = Available("Lantern Bay", "4400");
        using var ui = new UiHarness(State(ember, harbor, bay));

        var one = new NoticeView(NoticeKind.AchievementTexts) { Game = ember.Key, GameName = ember.Name, Games = [ember.Key], GameNames = [ember.Name] };
        ui.Agent.Notice(one);
        ui.Agent.Notice(one with { SteamRestartNeeded = true });
        // Names come with the notice, since the state that lists the games may come after it.
        ui.Agent.Notice(new NoticeView(NoticeKind.AchievementTexts)
        {
            Games = [harbor.Key, bay.Key, ember.Key],
            GameNames = ["Quiet Harbor", "Lantern Bay", "Ember Hollow"],
            SteamRestartNeeded = true,
        });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));

        var toasts = ui.Shell.Toasts.Items;
        (toasts[0].Title, toasts[0].Message, toasts[0].HasAction).Should().Be(("Tłumaczenie osiągnięć: Ember Hollow", (string?)null, false));
        (toasts[1].Message, toasts[1].ActionText).Should().Be(("Pojawią się w Steamie po jego ponownym uruchomieniu.", "Uruchom Steam ponownie"));
        (toasts[2].Title, toasts[2].Message).Should().Be(("Tłumaczenie osiągnięć dla 3 gier",
            "Quiet Harbor, Lantern Bay i 1 inna gra. Widoczne po ponownym uruchomieniu Steama."));
        ui.Render("achievements-notices");

        ui.Click(ui.Find<Button>().Last(button => button.IsEffectivelyVisible && button.Content is "Uruchom Steam ponownie"));
        ui.Agent.Requests.OfType<RestartSteam>().Should().ContainSingle();

        ui.Push(State(ember, harbor, bay) with { Version = 2, SteamUi = Classic with { RunningGame = harbor.Key, RunningGameName = harbor.Name } });
        ui.Agent.Notice(one with { SteamRestartNeeded = true });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Shell.Toasts.Items[^1].HasAction.Should().BeFalse("closing Steam would close the game that runs");
    }

    private static readonly SteamUiView Millennium = new() { Delivery = SteamUiDelivery.Millennium, Reachable = true };

    private static readonly SteamUiView Classic = new() { Delivery = SteamUiDelivery.Classic, Reachable = true };

    private static GameView WithTexts(GameView game, AchievementTextsState state, bool isOn, int translated = 13, bool withInstall = false) =>
        game with
        {
            AchievementTexts = new MyAchievementTexts
            {
                Project = game.Project?.ProjectId ?? Ember.ProjectId,
                TeamName = Lanterns.Name,
                Texts = new ProjectAchievements
                {
                    SteamAppId = long.Parse(game.Key.StoreGameId, System.Globalization.CultureInfo.InvariantCulture),
                    Language = "polish",
                    Translated = translated,
                    Total = 13,
                    Revision = 1,
                    HasOfficial = withInstall,
                },
                IsOn = isOn,
                State = state,
            },
        };

    private static UiHarness Open(GameView game, SteamUiView? steam = null)
    {
        var ui = new UiHarness(State(game) with { SteamUi = steam ?? Millennium });
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        return ui;
    }

    private static void Show(UiHarness ui, GameView game, SteamUiView steam, long version, Func<SettingsView, SettingsView>? settings = null)
    {
        var state = State(game) with { Version = version, SteamUi = steam };
        ui.Push(state with { Settings = settings?.Invoke(state.Settings) ?? state.Settings });
    }

    private static GamePageViewModel Page(UiHarness ui) => (GamePageViewModel)ui.Shell.Navigator.Current!;

    private static async Task RenderAsync(UiHarness ui, string name)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Find<Border>("AchievementsRow").BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(200));
        ui.Render(name);
    }
}
