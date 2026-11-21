using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Settings;
using JustModIt.Manager.Ui.Pages.Undetected;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class GameModeTests
{
    [AvaloniaFact]
    public void In_Game_Mode_links_go_through_Steam_and_folders_and_other_clients_open_nothing()
    {
        using var ui = GameModeWindow();
        var links = ui.Session.Services.Links;

        links.Open(new Uri("https://example.com/games/ember-hollow?tab=releases#notes"));
        links.Open(PageLinks.StorePage(TestStates.Ember));
        links.Open(new Uri("http://example.com/"));
        links.Open(PageLinks.StoreClient(new StoreDetectionView("steam") { IsFound = true }));
        links.Open(PageLinks.StoreClient(new StoreDetectionView("gog") { IsFound = true, Libraries = [new("gog-registry", "gog-galaxy")] }));
        links.OpenFolder(TestPaths.Rooted('D', "Games", "Ember Hollow"));

        ui.Steam.Sent.Should().Equal(
            "steam://openurl/https://example.com/games/ember-hollow?tab=releases#notes",
            "steam://nav/games/details/4100",
            "steam://open/games");
        (ui.Links.Opened.Count, ui.Links.Clients.Count, ui.Links.Folders.Count).Should().Be((0, 0, 0),
            "gamescope never shows what the system's opener starts");
    }

    [AvaloniaFact]
    public void Steams_keyboard_opens_by_a_tapped_field_and_closes_when_focus_leaves_it()
    {
        using var ui = GameModeWindow();
        var search = ui.Find<TextBox>().Single(box => box.IsEffectivelyVisible);
        var at = search.TranslatePoint(default, ui.Window)!.Value;
        static int Pixels(double value) => (int)Math.Round(value);
        var opened = $"steam://open/keyboard?XPosition={Pixels(at.X)}&YPosition={Pixels(at.Y)}&Width={Pixels(search.Bounds.Width)}"
            + $"&Height={Pixels(search.Bounds.Height)}&Mode=0";
        const string closed = "steam://close/keyboard";

        search.Focus(NavigationMethod.Tab);
        ui.Press(Key.Down);
        ui.Steam.Sent.Should().BeEmpty("a controller passing through the field must not cover half the screen");

        ui.Press(Key.Enter);
        ui.Press(Key.Tab);
        ui.Click(search);
        ui.Press(Key.Tab);
        ui.Press(Key.F, RawInputModifiers.Control);

        search.IsFocused.Should().BeTrue();
        ui.Steam.Sent.Should().Equal(opened, closed, opened, closed, opened);
    }

    [AvaloniaFact]
    public async Task A_notice_that_comes_while_Steam_covers_the_window_waits_in_its_corner_until_the_window_is_back()
    {
        var (harbor, ember) = (Installed("Quiet Harbor", "4200"), UpdateAvailable("Ember Hollow", "4100"));
        using var ui = new UiHarness(State(ember, harbor) with { Settings = TestStates.Idle().Settings with { ControlsHintShown = true } },
            context: context => context with { Frame = WindowFrame.Screen }, activated: false);
        ui.SizeToDeck();

        ui.Agent.Notice(new NoticeView(NoticeKind.Updated) { Game = harbor.Key, GameName = harbor.Name, ReleaseLabel = "v2" });
        ui.Agent.Notice(new NoticeView(NoticeKind.UpdateAvailable) { Game = ember.Key, GameName = ember.Name, ReleaseLabel = "v2" });
        UiHarness.Settle();
        (ui.Window.IsActive, ui.Shell.Toasts.IsWindowInFront, ui.Shell.Toasts.Items.Count).Should().Be((false, false, 2));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-mode-toasts-behind-steam");

        ui.Window.Activate();
        UiHarness.Settle();
        ui.Shell.Toasts.IsWindowInFront.Should().BeTrue("their time runs once the player is back");
        ui.Window.Hide();
        UiHarness.Settle();
        ui.Window.Show();
        UiHarness.Settle();
        ui.Shell.Toasts.IsWindowInFront.Should().BeFalse("Steam's overlay took the focus again");
    }

    // The same walk in Game Mode at the Steam Deck's screen and on the desktop at the smallest window, both rendered.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Game_Mode_hides_what_needs_a_desktop(bool inGameMode)
    {
        var (shown, name) = (!inGameMode, inGameMode ? "game-mode" : "desktop");
        var ember = Installed("Ember Hollow", "4100");
        ember = WithTexts(ember with
        {
            Installs = [ember.Installs[0] with { LaunchOptions = new LaunchOptionsView(LaunchOptionsState.Manual, ["-nointro"], []) }],
        }, AchievementTextsState.WaitingForSteamRestart);
        var orchard = Available("Glass Orchard", "1100000002") with { Key = new GameKey(StoreId.Gog, "1100000002") };
        var classic = new SteamUiView { Delivery = SteamUiDelivery.Classic, Reachable = true, RestartNeeded = true };
        var state = State(ember, orchard) with
        {
            Settings = TestStates.Idle(creatorMode: true).Settings with { ControlsHintShown = true },
            SteamUi = classic,
            UncleanExit = new UncleanExitView("0.1.0", null),
            AppUninstall = new AppUninstallView(TestPaths.Rooted('C', "Apps", "ExampleApp.AppImage"), TestPaths.Rooted('C', "Data"),
                TestPaths.Rooted('C', "Cache"), TestPaths.Rooted('C', "Data", "logs")),
        };
        var report = new DetectionReportView(
        [
            new StoreDetectionView("steam") { IsFound = true, GameCount = 1, Libraries = [new LibraryDetectionView("steam", "steam-library")] },
            new StoreDetectionView("gog") { IsFound = true, GameCount = 1, Libraries = [new LibraryDetectionView("heroic", "heroic")] },
        ]);
        using var ui = new UiHarness(state, width: 960, height: 600,
            context: context => inGameMode ? context with { Frame = WindowFrame.Screen } : context,
            answer: request => request switch
            {
                GetDetectionReport => new DetectionReportResult(report),
                ValidateBackupFolder => new BackupFolderChecked(AvailableBytes: 212_400_000_000),
                GetAppDataUsage => new AppDataUsage(TestPaths.Rooted('C', "Data"), 317_718_528, 224_395_264, 90_177_536, 3_355_443),
                _ => null,
            });
        if (inGameMode)
        {
            ui.SizeToDeck();
        }

        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        var told = ui.Shell.Dialogs.Current.Should().BeOfType<UncleanExitDialogViewModel>().Subject;
        told.Message.EndsWith("zgłoszenia problemu.", StringComparison.Ordinal).Should().Be(shown, "Game Mode has no picker to save a report with");
        ui.Find<Button>("SaveReport").IsVisible.Should().Be(shown);
        ui.Render(name + "-unclean-exit");
        ui.Shell.Dialogs.CancelCurrent();

        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(ember.Key)]);
        UiHarness.Settle();
        var game = (GamePageViewModel)ui.Shell.Navigator.Current!;
        (game.CanLaunch, game.HasFolder).Should().Be((true, shown), "Steam starts its own games in Game Mode too");
        game.Installs.Single().ManualLaunchOptions!.Steps.Should().StartWith(inGameMode
            ? "W bibliotece Steama: strona gry › przycisk z kołem zębatym › Właściwości › Opcje uruchamiania."
            : "W bibliotece Steama: prawy przycisk na grze › Właściwości › Opcje uruchamiania.");
        ui.Find<Button>("RestartSteam").IsVisible.Should().Be(shown, "restarting Steam would end the Game Mode window with it");
        await RenderAsync(ui, name + "-game", "InstallRows");
        ui.Agent.Notice(new NoticeView(NoticeKind.AchievementTexts) { Games = [ember.Key], GameNames = [ember.Name], SteamRestartNeeded = true });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Toasts.Items[^1].HasAction.Should().Be(shown);
        await RenderAsync(ui, name + "-game-achievements", "AchievementsRow");
        ui.Shell.Toasts.Dismiss(ui.Shell.Toasts.Items[^1]);

        var harbor = Available("Quiet Harbor", "4200");
        ui.Push(state with
        {
            Version = 2,
            Games = [WithTexts(ember, AchievementTextsState.RestartBlocked), orchard],
            SteamUi = classic with { RunningGame = harbor.Key, RunningGameName = harbor.Name },
        });
        game.Achievements.Notice!.Text.Should().StartWith(inGameMode ? "Steam musi wystartować ponownie" : "Gra ze Steama jest uruchomiona",
            "without the button, the row does not say when it works");
        ui.Shell.Navigator.Navigate(new GameRoute(orchard.Key));
        ((GamePageViewModel)ui.Shell.Navigator.Current!).CanLaunch.Should().Be(shown, "Game Mode shows no window of a program Steam did not start");

        ui.Shell.Navigator.Navigate(new SettingsRoute());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        var settings = (SettingsPageViewModel)ui.Shell.Navigator.Current!;
        (ui.Find<RadioButton>("BackupsInFolder").IsVisible, ui.Find<Button>("OpenDataFolder").IsVisible, ui.Find<Button>("OpenLogs").IsVisible,
            ui.Find<SettingRow>("UninstallAppRow").IsVisible).Should().Be((shown, shown, shown, shown));
        settings.CreatorModeDescription.Should().EndWith(inGameMode
            ? "tłumaczeniami gier. W trybie gry Kreator spolszczeń jest ukryty; działa w trybie pulpitu."
            : "tłumaczeniami gier.");
        settings.NotificationsDescription.Should().EndWith(inGameMode
            ? "albo znalezione. W trybie gry aplikacja nie wysyła powiadomień systemowych; to, co wymaga uwagi, czeka w „Do zrobienia” "
                + "w „Moich grach”."
            : "albo znalezione.");
        await RenderAsync(ui, name + "-settings-notifications", "NotificationsRow");
        await RenderAsync(ui, name + "-settings-backups", "BackupsInGameFolder");
        await RenderAsync(ui, name + "-settings-data", "DataSizes");
        await RenderAsync(ui, name + "-settings-about", "UpdateStatus");
        ui.Agent.Navigate(new UninstallAppTarget());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.IsOpen.Should().Be(shown, "the menu entry's uninstall waits for the desktop too");
        ui.Shell.Dialogs.CancelCurrent();

        ui.Push(state with
        {
            Version = 3,
            Settings = state.Settings with { BackupLocation = BackupLocation.CustomFolder, CustomBackupFolder = TestPaths.Rooted('E', "Backups") },
        });
        ui.Shell.Navigator.Navigate(new LibraryRoute());
        ui.Shell.Navigator.Navigate(new SettingsRoute());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        (ui.Find<RadioButton>("BackupsInFolder").IsVisible, ui.Find<Button>("PickBackupFolder").IsVisible,
            ui.Find<TextBlock>("BackupFolderInGameMode").IsVisible).Should().Be((true, shown, inGameMode), "a folder chosen before stays chosen");
        await RenderAsync(ui, name + "-settings-folder", "BackupsInGameFolder");

        ui.Shell.Navigator.Navigate(new UndetectedGamesRoute());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ((UndetectedGamesViewModel)ui.Shell.Navigator.Current!).Stores.Select(store => store.Client is not null).Should()
            .Equal([true, shown], "Steam's own link opens its library in Game Mode, while Heroic's window would stay unseen");
        (ui.Find<Button>("AddLibrary").IsVisible, ui.Find<Button>("AddGame").IsVisible, ui.Find<StatusLine>("FoldersInGameMode").IsVisible)
            .Should().Be((shown, shown, inGameMode));
        await RenderAsync(ui, name + "-undetected", "Stores");
    }

    private static GameView WithTexts(GameView game, AchievementTextsState state) => game with
    {
        AchievementTexts = new MyAchievementTexts
        {
            Project = game.Project!.ProjectId,
            TeamName = Lanterns.Name,
            Texts = new ProjectAchievements { SteamAppId = 4100, Language = "polish", Translated = 13, Total = 13, Revision = 1 },
            IsOn = true,
            State = state,
        },
    };

    // Scrolls the named control to the top of the page, with what follows it below.
    private static async Task RenderAsync(UiHarness ui, string name, string top)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Find<Control>(top).BringIntoView(new Rect(0, -100, 1, ui.Window.Height - 160));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(200));
        ui.Render(name);
    }

    // A window Steam started in Game Mode, its controls hint seen before.
    private static UiHarness GameModeWindow()
    {
        var state = TestStates.Idle();
        var ui = new UiHarness(state with { Settings = state.Settings with { ControlsHintShown = true } },
            context: context => context with { Frame = WindowFrame.Screen });
        ui.SizeToDeck();
        return ui;
    }
}
