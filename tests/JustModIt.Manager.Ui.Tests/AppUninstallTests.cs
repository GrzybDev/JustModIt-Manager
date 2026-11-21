using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class AppUninstallTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly AppUninstallView Offer = new(Path.Join(Home, "Applications", "ExampleApp.AppImage"),
        Path.Join(Home, ".local", "share", "ExampleVendor", "ExampleApp"), Path.Join(Home, ".cache", "ExampleVendor", "ExampleApp"),
        Path.Join(Home, ".local", "state", "ExampleVendor", "ExampleApp", "logs"));

    [AvaloniaFact]
    public async Task Uninstalling_asks_with_what_goes_and_stays_shows_each_step_and_ends_on_a_view_that_closes_the_window()
    {
        var state = TestStates.Idle() with { AppUninstall = Offer, Games = [Installed("Ember Hollow", "4100"), Installed("Quiet Harbor", "4200"),
            Installed("Saltmarsh", "4300")] };
        using var ui = await OpenAsync(state);
        ui.Find<SettingRow>("UninstallAppRow").Message.Should().Be(
            "Usuwa program, jego wpis w menu, uruchamianie z systemem i obsługę linków. Dane aplikacji i zainstalowane spolszczenia zostają.");
        await ui.RenderBothAsync("settings-uninstall-app", () => ui.Find<TextBlock>("AboutApp").BringIntoView());

        var dialog = await AskAsync(ui);
        dialog.Title.Should().Be("Odinstalować aplikację Example App?");
        dialog.Removed.Select(line => line.Text).Should().Equal("Program", "Wpis w menu aplikacji i ikony", "Uruchamianie razem z systemem",
            "Otwieranie linków ze strony w aplikacji");
        dialog.Removed[0].Path.Should().Be(Wrapped("~", "Applications", "ExampleApp.AppImage"));
        dialog.Kept.Select(line => line.Text).Should().Equal("Dane aplikacji: ustawienia, zapisy instalacji, pobrane paczki i logi",
            "Spolszczenia w 3 grach i kopie oryginalnych plików");
        ui.Render("uninstall-confirm");
        ui.Click(ui.Find<Button>("UninstallGoToAppData"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.IsOpen.Should().BeFalse();
        ui.Find<Button>("UninstallAll").IsFocused.Should().BeTrue("uninstalling every project first puts the originals back");

        await AskAsync(ui);
        ui.Click(ui.Find<Button>("UninstallConfirm"));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Autostart));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Integration));
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Agent.Requests.OfType<UninstallApp>().Should().ContainSingle();
        Lines(ui).Should().Equal("Wyłączono uruchamianie razem z systemem", "Usunięto wpis w menu, ikony i obsługę linków", "Usuwanie programu",
            "Kończenie pracy w tle");
        ui.Shell.Dialogs.IsOpen.Should().BeTrue("the agent takes the steps whatever the window does, so the dialog waits for its end");
        ui.Render("uninstall-running");

        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Program));
        ui.Agent.Link = AgentLink.Connecting;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        (ui.Shell.IsUninstalled, ui.Shell.IsConnecting).Should().Be((true, false), "the agent left as it should, so nothing waits for it");
        ui.Shell.Uninstall.Uninstalled!.Folders.Should().Equal(Path.Join("~", ".local", "share", "ExampleVendor", "ExampleApp"),
            Path.Join("~", ".cache", "ExampleVendor", "ExampleApp"), Path.Join("~", ".local", "state", "ExampleVendor", "ExampleApp"));
        ui.Shell.Uninstall.Uninstalled.Installs.Should().Be("Spolszczenia w 3 grach też zostały. Po ponownej instalacji aplikacja zajmie się nimi jak dotąd.");
        await ui.RenderBothAsync("uninstalled");

        ui.Click(ui.Find<Button>("CloseUninstalled"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Window.IsVisible.Should().BeFalse("the app is gone, so closing its window asks nothing");
    }

    [AvaloniaFact]
    public async Task A_failed_step_says_why_and_what_came_back_and_the_uninstall_tries_again_from_the_start()
    {
        using var ui = await OpenAsync(TestStates.Idle() with { AppUninstall = Offer });
        var dialog = await AskAsync(ui);
        dialog.Kept.Select(line => (line.Text, line.Path)).Should().Equal(
            ("Ustawienia i zapisy instalacji", Wrapped("~", ".local", "share", "ExampleVendor", "ExampleApp")),
            ("Pobrane paczki, obrazy i dane katalogu", Wrapped("~", ".cache", "ExampleVendor", "ExampleApp")),
            ("Logi", Wrapped("~", ".local", "state", "ExampleVendor", "ExampleApp", "logs")));
        ui.Render("uninstall-confirm-nothing-installed");

        ui.Click(ui.Find<Button>("UninstallConfirm"));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Autostart));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Integration));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Program,
            new AppUninstallFailure(AppUninstallProblem.AccessDenied, Path.GetDirectoryName(Offer.ProgramFile), Restored: true)));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        dialog.Message.Should().Be("Nie udało się usunąć pliku programu, więc wszystko wróciło na miejsce. Aplikacja działa dalej.");
        Lines(ui).Should().Equal("Wyłączono uruchamianie razem z systemem", "Usunięto wpis w menu, ikony i obsługę linków",
            "Nie udało się usunąć programu", "Przywrócono wpis w menu, ikony, obsługę linków i uruchamianie razem z systemem");
        dialog.Steps[2].Reason.Should().Be($"Brak uprawnień do zapisu w folderze {Wrapped("~", "Applications")}.");
        ui.Render("uninstall-failed-program");
        ui.Click(ui.Find<Button>("UninstallShowFolder"));
        ui.Links.Folders.Should().Equal(Path.GetDirectoryName(Offer.ProgramFile));

        ui.Click(ui.Find<Button>("UninstallRetry"));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Autostart, new AppUninstallFailure(AppUninstallProblem.NoAnswer,
            Restored: true)));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Agent.Requests.OfType<UninstallApp>().Should().HaveCount(2);
        dialog.Message.Should().Be("Nie udało się wyłączyć uruchamiania razem z systemem, więc nic nie zostało usunięte. Aplikacja działa dalej.");
        Lines(ui).Should().Equal("Nie udało się wyłączyć uruchamiania razem z systemem", "Usuwanie wpisu w menu, ikon i obsługi linków",
            "Usuwanie programu");
        dialog.Steps[0].Reason.Should().Be("Menedżer usług systemu (systemd) nie odpowiedział w ciągu 15 sekund.");
        ui.Render("uninstall-failed-autostart");

        ui.Click(ui.Find<Button>("UninstallCancel"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.IsOpen.Should().BeFalse();
        ui.Shell.IsUninstalled.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Closing_the_window_while_the_app_uninstalls_waits_for_the_end_and_then_asks_nothing()
    {
        using var ui = await OpenAsync(TestStates.Idle() with { AppUninstall = Offer });
        await AskAsync(ui);
        ui.Click(ui.Find<Button>("UninstallConfirm"));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Autostart));

        ui.Window.Close();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Window.IsVisible.Should().BeTrue("the agent takes the steps whatever the window does");
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Integration));
        ui.Agent.UninstallProgress(new AppUninstallProgress(AppUninstallStep.Program));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));

        ui.Window.IsVisible.Should().BeFalse("the app is gone, so there is nothing to keep running in the background");
        ui.Agent.Requests.OfType<ExitApp>().Should().BeEmpty("the agent leaves by itself once the app is gone");
    }

    [AvaloniaFact]
    public async Task The_row_waits_for_the_jobs_and_is_not_offered_where_the_system_uninstalls_the_app()
    {
        var game = Installed("Ember Hollow", "4100");
        var state = TestStates.Idle() with { AppUninstall = Offer, Games = [game] };
        using var ui = await OpenAsync(state.With(Job(game, JobKind.Update, JobStage.Downloading)));

        ui.Find<SettingRow>("UninstallAppRow").Message.Should().Be("Aplikację można odinstalować, gdy nie trwa żadne zadanie.");
        ui.Find<Button>("UninstallApp").IsEffectivelyEnabled.Should().BeFalse("leaving the agent would stop the job halfway");
        ui.Find<TextBlock>("AboutApp").BringIntoView();
        ui.Render("settings-uninstall-app-busy");
        ui.Push(state with { Version = 9, PendingBackupMoves = 1 });
        ui.Find<Button>("UninstallApp").IsEffectivelyEnabled.Should().BeFalse();

        ui.Push(state with { Version = 10 });
        ui.Find<Button>("UninstallApp").IsEffectivelyEnabled.Should().BeTrue();
        ui.Push(state with { Version = 11, AppUninstall = null });
        ui.Find<SettingRow>("UninstallAppRow").IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task The_apps_menu_entry_opens_the_settings_with_the_question_whether_the_window_starts_for_it_or_runs_already()
    {
        using var ui = new UiHarness(TestStates.Idle() with { AppUninstall = Offer }, launch: new UninstallAppTarget());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new SettingsRoute());
        ui.Shell.Dialogs.Current.Should().BeOfType<AppUninstallDialogViewModel>();

        ui.Shell.Dialogs.CancelCurrent();
        ui.Shell.Navigator.Navigate(new LibraryRoute());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Agent.Navigate(new UninstallAppTarget());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new SettingsRoute());
        ui.Shell.Dialogs.Current.Should().BeOfType<AppUninstallDialogViewModel>();
        ui.Agent.Requests.OfType<UninstallApp>().Should().BeEmpty("the question still asks");
    }

    private static async Task<UiHarness> OpenAsync(AppStateView state)
    {
        var ui = new UiHarness(state, launch: new SettingsTarget());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        return ui;
    }

    private static async Task<AppUninstallDialogViewModel> AskAsync(UiHarness ui)
    {
        ui.Click(ui.Find<Button>("UninstallApp"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        return ui.Shell.Dialogs.Current.Should().BeOfType<AppUninstallDialogViewModel>().Subject;
    }

    private static IEnumerable<string> Lines(UiHarness ui) => ((AppUninstallDialogViewModel)ui.Shell.Dialogs.Current!).Steps.Select(line => line.Text);

    // A path a sentence names may wrap after each of its folders.
    private static string Wrapped(params string[] parts) => Path.Join(parts).Replace(Path.DirectorySeparatorChar.ToString(),
        Path.DirectorySeparatorChar + "​", StringComparison.Ordinal);
}
