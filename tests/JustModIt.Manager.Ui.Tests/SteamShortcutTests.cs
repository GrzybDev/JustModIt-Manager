using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class SteamShortcutTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string Program = Path.Join(Home, "Applications", "ExampleApp.AppImage");

    [AvaloniaFact]
    public async Task The_settings_Steam_row_follows_the_shortcut_from_missing_to_added()
    {
        var state = TestStates.Idle() with { AppUninstall = Offer };
        using var ui = new UiHarness(state with { SteamShortcut = Shortcut(SteamShortcutState.Missing) }, launch: new SettingsTarget());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));

        Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.", null, "Dodaj do Steama"));
        ui.Find<Control>("SteamShortcutSteps").IsVisible.Should().BeFalse("Game Mode waits for the shortcut, so it is added at once");
        await ui.RenderBothAsync("steam-row-9-missing", () => Reveal(ui));
        ui.Click(ui.Find<Button>("SteamShortcutAction"));
        ui.Agent.Requests.OfType<AddToSteam>().Should().ContainSingle();

        ui.Push(state with { Version = 2, SteamShortcut = Shortcut(SteamShortcutState.Adding) });
        Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.", "Steam dodaje skrót…", "Dodawanie…"));
        ui.Find<Button>("SteamShortcutAction").IsEffectivelyEnabled.Should().BeFalse("Steam is at it");
        Reveal(ui);
        ui.Render("steam-row-10-adding");

        ui.Push(state with { Version = 3, SteamShortcut = Shortcut(SteamShortcutState.SteamNotRunning) });
        Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.",
            "Steam nie jest uruchomiony, a skrót dodaje sam Steam.", "Uruchom Steam"));
        Reveal(ui);
        ui.Render("steam-row-12-steam-not-running");
        ui.Click(ui.Find<Button>("SteamShortcutAction"));
        ui.Links.Clients.Select(client => client.Address).Should().Equal("steam://open/games");

        ui.Push(state with { Version = 4, SteamShortcut = Shortcut(SteamShortcutState.Failed) });
        Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.",
            "Skrót nie pojawił się w bibliotece Steama w ciągu 30 sekund.", "Spróbuj ponownie"));
        ui.Click(ui.Find<Button>("SteamShortcutManual"));
        ui.Find<SelectableTextBlock>().Single(file => file.Classes.Contains("file")).Text.Should().Be(Path.Join("~", "Applications",
            "ExampleApp.AppImage"), "the file is copied into Steam's file dialog as it shows");
        Reveal(ui);
        ui.Render("steam-row-14-failed");
        ui.Click(ui.Find<Button>("SteamShortcutAction"));
        ui.Agent.Requests.OfType<AddToSteam>().Should().HaveCount(2);

        ui.Push(state with { Version = 5, SteamShortcut = Shortcut(SteamShortcutState.Missing) with { HasGameSession = false } });
        Row(ui).Should().Be(("Skrót otwiera okno z biblioteki Steama, także w trybie Big Picture.", null, "Dodaj do Steama"));
        ui.Find<Control>("SteamShortcutSteps").IsVisible.Should().BeTrue("without Game Mode the shortcut may also be added by hand");
        Reveal(ui);
        ui.Render("steam-row-13-without-game-mode");

        ui.Push(state with
        {
            Version = 6,
            SteamShortcut = Shortcut(SteamShortcutState.Elsewhere) with { Other = Path.Join(Home, "Downloads", "ExampleApp.AppImage"), OtherMissing = true },
        });
        Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.",
            $"Skrót w Steamie uruchamia plik, którego już nie ma: {Wrapped("~", "Downloads", "ExampleApp.AppImage")}. Stary skrót usuwa się w Steamie.",
            "Dodaj ponownie"));
        Reveal(ui);
        ui.Render("steam-row-15-elsewhere");
        ui.Push(state with
        {
            Version = 7,
            SteamShortcut = Shortcut(SteamShortcutState.Elsewhere) with { Other = Path.Join(Home, "Downloads", "ExampleApp.AppImage") },
        });
        Row(ui).Status.Should().StartWith("Skrót w Steamie uruchamia inną kopię aplikacji:", "the copy that shortcut starts is still there");

        ui.Push(state with { Version = 8, SteamShortcut = Shortcut(SteamShortcutState.Added) });
        Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.", "Jest w bibliotece Steama. Usuwa się go w Steamie.",
            null));
        Reveal(ui);
        ui.Render("steam-row-11-added");
        ui.Click(ui.Find<Button>("UninstallApp"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.Current.Should().BeOfType<AppUninstallDialogViewModel>().Which.Kept.Select(line => line.Text).Should()
            .Contain("Skrót w bibliotece Steama; usuwa się go w Steamie", "Steam's shortcuts are Steam's to remove");
        ui.Render("steam-uninstall-keeps-shortcut");
        ui.Shell.Dialogs.CancelCurrent();

        ui.Push(state with { Version = 9 });
        ui.Find<Border>("SteamShortcutRow").IsVisible.Should().BeFalse("only the Steam behind the home folder on Linux takes the shortcut");
    }

    [AvaloniaFact]
    public async Task A_window_Steam_started_shows_the_app_in_Steams_library()
    {
        var state = TestStates.Idle() with { SteamShortcut = Shortcut(SteamShortcutState.Missing) };
        using (var ui = new UiHarness(state with { Settings = state.Settings with { ControlsHintShown = true } }, launch: new SettingsTarget(),
            context: context => context with { StartedBySteam = true, Frame = WindowFrame.Screen }))
        {
            ui.SizeToDeck();
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));

            Row(ui).Should().Be(("W trybie gry okno otwiera się tylko ze skrótu w bibliotece Steama.", "Jest w bibliotece Steama. Usuwa się go w Steamie.",
                null), "Steam started the window from the shortcut, which its file may not show yet");
            Reveal(ui);
            ui.Render("steam-row-11-game-mode");
        }

        // Big Picture starts the window from the shortcut on the desktop as well, where the app can be uninstalled.
        using var desktop = new UiHarness(state with { AppUninstall = Offer }, launch: new SettingsTarget(),
            context: context => context with { StartedBySteam = true });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        desktop.Click(desktop.Find<Button>("UninstallApp"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        desktop.Shell.Dialogs.Current.Should().BeOfType<AppUninstallDialogViewModel>().Which.Kept.Select(line => line.Text).Should()
            .Contain("Skrót w bibliotece Steama; usuwa się go w Steamie", "the shortcut that started the window stays in Steam");
    }

    private static AppUninstallView Offer => new(Program, Path.Join(Home, ".local", "share", "ExampleVendor", "ExampleApp"),
        Path.Join(Home, ".cache", "ExampleVendor", "ExampleApp"), Path.Join(Home, ".local", "state", "ExampleVendor", "ExampleApp", "logs"));

    private static SteamShortcutView Shortcut(SteamShortcutState state) => new(state, HasGameSession: true, Program);

    private static void Reveal(UiHarness ui)
    {
        ui.Find<Border>("SteamShortcutRow").BringIntoView();
        UiHarness.Settle();
    }

    // The row's description, its status and its button, each null when it shows none.
    private static (string Description, string? Status, string? Action) Row(UiHarness ui)
    {
        var status = ui.Find<StatusLine>("SteamShortcutStatus");
        var action = ui.Find<Button>("SteamShortcutAction");
        return (ui.Find<TextBlock>("SteamShortcutDescription").Text!, status.IsVisible ? status.Label : null,
            action.IsVisible ? Avalonia.Automation.AutomationProperties.GetName(action) : null);
    }

    // A path a sentence names may wrap after each of its folders.
    private static string Wrapped(params string[] parts) => Path.Join(parts).Replace(Path.DirectorySeparatorChar.ToString(),
        Path.DirectorySeparatorChar + "​", StringComparison.Ordinal);
}
