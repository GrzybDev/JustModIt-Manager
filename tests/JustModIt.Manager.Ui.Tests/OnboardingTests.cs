using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Onboarding;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class OnboardingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public async Task A_new_player_walks_through_the_setup_and_every_choice_reaches_the_agent_at_once()
    {
        using var ui = new UiHarness(FirstRun());
        AgentAppliesSettings(ui);
        await Settle();

        ui.Shell.ShowsPages.Should().BeFalse("the sidebar waits for the setup");
        Setup(ui).CurrentStep.Should().BeOfType<WelcomeStep>();
        NamesOfTheControls(ui).Should().NotContain(string.Empty);
        ui.Render("onboarding-1-welcome");

        ui.Click(Button(ui, Strings.Onboarding_Next));
        await Settle();
        Setup(ui).CurrentStep.Should().BeOfType<AutomationStep>();
        Tile(ui, Strings.Onboarding_AutomateUpdates).IsChecked.Should().BeTrue("updates only is the default");
        ui.Render("onboarding-2-automation");
        ui.Click(Tile(ui, Strings.Onboarding_AutomateEverything));
        ui.Click(Switch(ui, Strings.Onboarding_Notifications));
        Saved(ui).Should().Match<SettingsView>(settings => settings.AutoInstallProjects && settings.AutoUpdateProjects
            && settings.ReapplyAfterGameUpdate && !settings.ShowNotifications && settings.OnboardingStep == 1 && !settings.OnboardingCompleted);
        NamesOfTheControls(ui).Should().NotContain(string.Empty);

        ui.Click(Button(ui, Strings.Onboarding_Next));
        await Settle();
        ui.Click(Tile(ui, Strings.Shell_CloseToTray));
        ui.Click(Switch(ui, Strings.Onboarding_StartWithSystem));
        Saved(ui).Should().Match<SettingsView>(settings => settings.CloseBehavior == CloseBehavior.MinimizeToTray && !settings.StartWithSystem);
        Tile(ui, Strings.Onboarding_CloseAsk).IsChecked.Should().BeFalse();
        NamesOfTheControls(ui).Should().NotContain(string.Empty);
        await Settle();
        ui.Render("onboarding-3-background");

        ui.Click(Button(ui, Strings.Onboarding_Next));
        await Settle();
        ui.Click(Tile(ui, Strings.Onboarding_CreatorTile));
        Saved(ui).CreatorMode.Should().BeTrue();
        await Settle();
        ui.Render("onboarding-4-creator");

        ui.Click(Button(ui, Strings.Onboarding_Next));
        await Settle();
        Setup(ui).Ready.Summary.Select(item => item.Value).Should().Equal(Strings.Onboarding_AutomateEverything,
            Strings.Onboarding_NotificationsOff, Strings.Onboarding_Off, Strings.Shell_CloseToTray, Strings.Onboarding_On,
            Strings.Onboarding_BackupsInGameFolders);
        ui.Find<Button>().Where(button => button.IsEffectivelyVisible).Select(Name).Should().NotContain(Strings.Onboarding_Skip);
        NamesOfTheControls(ui).Should().NotContain(string.Empty);

        ui.Click(Button(ui, Strings.Onboarding_Start));
        Saved(ui).OnboardingCompleted.Should().BeTrue();
        await Settle();
        ui.Shell.FirstRun.Should().BeNull();
        ui.Shell.ShowsPages.Should().BeTrue();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
    }

    [AvaloniaFact]
    public async Task A_setup_left_halfway_opens_on_its_step_with_its_choices_and_the_keyboard_moves_through_it()
    {
        using var ui = new UiHarness(FirstRun(settings => settings with
        {
            OnboardingStep = 3,
            CreatorMode = true,
            CloseBehavior = CloseBehavior.Exit,
        }));
        AgentAppliesSettings(ui);
        await Settle();

        Setup(ui).CurrentStep.Should().BeOfType<CreatorStep>();
        Tile(ui, Strings.Onboarding_CreatorTile).IsChecked.Should().BeTrue();
        Setup(ui).Steps.Select(step => step.IsDone).Should().Equal(true, true, true, false, false);

        ui.Press(Key.Left, RawInputModifiers.Alt);
        await Settle();
        Setup(ui).CurrentStep.Should().BeOfType<BackgroundStep>();
        Tile(ui, Strings.Shell_CloseExit).IsChecked.Should().BeTrue();
        Saved(ui).OnboardingStep.Should().Be(2);

        ui.Press(Key.Enter);
        await Settle();
        Setup(ui).CurrentStep.Should().BeOfType<CreatorStep>();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute(), "going back moves through the setup, not the pages behind it");
    }

    [AvaloniaFact]
    public async Task Skipping_completes_the_setup_with_the_choices_made_but_a_save_the_agent_refuses_changes_nothing()
    {
        using var ui = new UiHarness(FirstRun(settings => settings with { OnboardingStep = 1 }));
        ui.Agent.Answer = request => request is ChangeSettings ? throw new RequestFailedException(ErrorCode.Failed) : null;
        await Settle();

        ui.Click(Tile(ui, Strings.Onboarding_AutomateNothing));
        Setup(ui).Automation.Should().Be(AutomationChoice.UpdatesOnly, "the agent kept its settings");
        Tile(ui, Strings.Onboarding_AutomateUpdates).IsChecked.Should().BeTrue();
        ui.Click(Button(ui, Strings.Onboarding_Skip));
        Setup(ui).IsFinishing.Should().BeFalse("the setup stays to be finished again");

        AgentAppliesSettings(ui);
        ui.Click(Tile(ui, Strings.Onboarding_AutomateNothing));
        ui.Click(Button(ui, Strings.Onboarding_Skip));
        Saved(ui).Should().Match<SettingsView>(settings => settings.OnboardingCompleted && !settings.AutoUpdateProjects
            && !settings.ReapplyAfterGameUpdate && settings.StartWithSystem && settings.CloseBehavior == CloseBehavior.Ask);
        await Settle();
        ui.Shell.FirstRun.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task The_last_step_shows_the_search_until_it_ends_then_the_stores_and_the_games_it_found()
    {
        var state = FirstRun(settings => settings with { OnboardingStep = 4 });
        using var ui = new UiHarness(state);
        ui.Agent.Answer = Answer;
        await Settle();

        var ready = Setup(ui).Ready;
        ready.IsSearching.Should().BeTrue();
        ready.HasStores.Should().BeFalse();
        ui.Find<CoverWall>().Single().Children.Should().BeEmpty("the wall waits for the catalogue");
        ui.Render("onboarding-5-searching");

        ui.Push(Found(state));
        await Settle();
        ready.IsSearching.Should().BeFalse();
        ready.Stores.Select(store => (store.Name, store.Detail)).Should().Equal(
            ("Steam", Text.Format(Strings.Onboarding_StoreFound, ("libraries", 2), ("games", 43))),
            ("GOG Galaxy", Strings.Onboarding_StoreMissing),
            (Strings.Onboarding_StoreEpic, Strings.Onboarding_StoreMissing));
        ready.Result.Should().Be(Text.Format(Strings.Onboarding_Matched, ("count", 3)));
        ready.Matches.Select(game => game.Name).Should().Equal("Ashen Rook", "Ember Hollow", "Quiet Harbour");
        ui.Find<CoverWall>().Single().Children.Should().NotBeEmpty();
        ui.Images.Requests.Select(request => request.Address).Should().Contain(Cover(1)).And.Contain(Cover("ember"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("onboarding-5-ready");
    }

    [AvaloniaFact]
    public async Task The_smallest_window_shortens_the_card_scrolls_the_step_and_keeps_its_buttons_in_view()
    {
        using var ui = new UiHarness(FirstRun(settings => settings with { OnboardingStep = 1 }), width: 960, height: 600);
        await Settle();

        var window = new Rect(ui.Window.Bounds.Size);
        foreach (var name in new[] { Strings.Onboarding_Back, Strings.Onboarding_Skip, Strings.Onboarding_Next })
        {
            var button = Button(ui, name);
            window.Contains(new Rect(button.TranslatePoint(default, ui.Window)!.Value, button.Bounds.Size)).Should().BeTrue(name);
        }

        var step = ui.Find<AutomationStepView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
        step.Extent.Height.Should().BeGreaterThan(step.Viewport.Height, "the step scrolls inside the shorter card");
        ui.Render("onboarding-minimum");
    }

    private static Task Settle() => UiHarness.WaitAsync(TimeSpan.FromMilliseconds(700));

    private static AppStateView FirstRun(Func<SettingsView, SettingsView>? change = null) => TestStates.Idle() with
    {
        Settings = (change ?? (settings => settings))(SettingsView.From(AppSettings.Default)),
        Library = new LibraryView(),
        Catalogue = new CatalogueView(),
    };

    private static AppStateView Found(AppStateView state) => state with
    {
        Version = state.Version + 1,
        Library = new LibraryView { ScannedAt = Now },
        Catalogue = new CatalogueView { FetchedAt = Now },
        Games = [Game("4200", "Ashen Rook", "rook"), Game("4100", "Ember Hollow", "ember"), Game("4300", "Quiet Harbour", "harbour")],
    };

    private static GameView Game(string id, string name, string slug) => new()
    {
        Key = new GameKey(StoreId.Steam, id),
        Name = name,
        Status = new GameStatusView(MyGameState.NotInstalled),
        Projects =
        [
            new ProjectOfferView
            {
                ProjectId = Guid.NewGuid(),
                ProjectSlug = "text",
                ProjectName = "Text",
                GameSlug = slug,
                GameName = name,
                Cover = Cover(slug),
                Team = new TeamView(Guid.NewGuid(), "lantern-team", "Lantern Team"),
            },
        ],
    };

    private static string Cover(object name) => $"https://images.example.com/covers/{name}.jpg";

    private static Message? Answer(Message request) => request switch
    {
        GetDetectionReport => new DetectionReportResult(new DetectionReportView(
        [
            new StoreDetectionView("steam")
            {
                Name = "Steam",
                IsFound = true,
                IsComplete = true,
                GameCount = 43,
                Libraries = [new LibraryDetectionView("c", "steam-library"), new LibraryDetectionView("d", "steam-library")],
            },
            new StoreDetectionView("gog") { Name = "GOG", Launcher = "GOG Galaxy" },
            new StoreDetectionView("epic"),
        ], Now)),
        GetCataloguePage => new CataloguePageResult(30,
            [.. Enumerable.Range(1, 30).Select(index => new CatalogueGameView(Guid.NewGuid(), $"game-{index}", $"Game {index}") { Cover = Cover(index) })],
            Now),
        _ => null,
    };

    // The agent takes the settings and sends them back with its next state, as the real one does.
    private static void AgentAppliesSettings(UiHarness ui) => ui.Agent.Answer = request =>
    {
        if (request is ChangeSettings change)
        {
            ui.Agent.Push(ui.Agent.State with { Version = ui.Agent.State.Version + 1, Settings = change.Settings });
        }

        return null;
    };

    private static OnboardingViewModel Setup(UiHarness ui) => (OnboardingViewModel)ui.Shell.FirstRun!;

    private static SettingsView Saved(UiHarness ui) => ui.Agent.Requests.OfType<ChangeSettings>().Last().Settings;

    private static IEnumerable<string> NamesOfTheControls(UiHarness ui) => ui.Find<OnboardingView>().Single().GetVisualDescendants()
        .OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(Name);

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().Single(button => button.IsEffectivelyVisible && Name(button) == name);

    private static RadioButton Tile(UiHarness ui, string name) =>
        ui.Find<RadioButton>().Single(tile => tile.IsEffectivelyVisible && Name(tile) == name);

    private static ToggleSwitch Switch(UiHarness ui, string name) =>
        ui.Find<ToggleSwitch>().Single(toggle => toggle.IsEffectivelyVisible && Name(toggle) == name);

    private static string Name(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();
}
