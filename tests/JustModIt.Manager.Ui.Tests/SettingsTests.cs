using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Settings;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class SettingsTests
{
    private const string BackupFolder = @"E:\Game backups";

    [AvaloniaFact]
    public async Task Every_change_is_sent_at_once_with_the_other_settings_kept_and_quick_changes_never_take_each_other_back()
    {
        var state = State(settings => settings with { OnboardingStep = 4, PreferredTeams = [Lanterns.Id], AutoInstallProjects = false });
        using var ui = await OpenAsync(state);
        // The agent publishes a state of its own before it answers, still with the settings it had before.
        ui.Agent.Answer = request =>
        {
            if (request is ChangeSettings)
            {
                ui.Agent.Push(state with { Version = ui.Agent.State.Version + 1 });
            }

            return null;
        };

        ui.Click(Toggle(ui, Strings.Settings_AutoInstall));
        ui.Click(Toggle(ui, Strings.Settings_StartWithSystem));
        ui.Click(ui.Find<RadioButton>("CloseExit"));

        var sent = ui.Agent.Requests.OfType<ChangeSettings>().Select(change => change.Settings).ToList();
        sent.Should().HaveCount(3);
        sent[2].Should().BeEquivalentTo(state.Settings with
        {
            AutoInstallProjects = true,
            StartWithSystem = !state.Settings.StartWithSystem,
            CloseBehavior = CloseBehavior.Exit,
        }, "each change starts from the one sent before, and what the page does not show stays as the agent has it");
        Toggle(ui, Strings.Settings_AutoInstall).IsChecked.Should().BeTrue("a state made before the agent took a change does not take it back");

        ui.Push(state with { Version = ui.Agent.State.Version + 1, Settings = sent[2] with { CreatorMode = true } });
        Toggle(ui, Strings.Settings_CreatorMode).IsChecked.Should().BeTrue("a change made elsewhere shows here too");
        Toggle(ui, Strings.Settings_StartWithSystem).IsChecked.Should().Be(sent[2].StartWithSystem);
        ui.Unnamed().Should().BeEmpty();
        ui.Render("settings", height: 3200);
    }

    [AvaloniaFact]
    public async Task A_change_the_agent_refuses_shows_the_agents_setting_again()
    {
        using var ui = await OpenAsync(State());
        ui.Agent.Answer = request => request is ChangeSettings ? throw new RequestFailedException(ErrorCode.InvalidRequest) : null;

        var toggle = Toggle(ui, Strings.Settings_Notifications);
        var before = toggle.IsChecked;
        ui.Click(toggle);
        UiHarness.Settle();

        toggle.IsChecked.Should().Be(before);
        ui.Shell.Toasts.Items.Should().ContainSingle(toast => toast.Title == Strings.Library_RequestFailedTitle);
    }

    [AvaloniaFact]
    public async Task Another_folder_for_backups_is_picked_checked_and_taken_and_a_refused_one_says_why()
    {
        using var ui = await OpenAsync(State(settings => settings with { CustomBackupFolder = @"E:\Old backups" }));
        ui.Pickers.Folder = BackupFolder;
        ui.Agent.Answer = request => request is ValidateBackupFolder { Folder: BackupFolder }
            ? new BackupFolderChecked(BackupFolderProblem.InsideGameFolder)
            : null;

        ui.Click(ui.Find<RadioButton>("BackupsInFolder"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Pickers.Asked.Should().Equal([Strings.Settings_PickBackupFolderTitle], "the tile always opens the picker, also with a folder remembered");
        ui.Agent.Requests.OfType<ValidateBackupFolder>().Select(check => check.Folder).Should().Equal(BackupFolder);
        ui.Agent.Requests.OfType<ChangeSettings>().Should().BeEmpty("a folder the agent refuses is never taken");
        ui.Find<RadioButton>("BackupsInGameFolder").IsChecked.Should().BeTrue();
        Page(ui).Backups.FolderProblem.Should().Be(Text.Format(Strings.Settings_BackupFolderProblem, ("problem", "InsideGameFolder")));

        ui.Agent.Answer = request => request is ValidateBackupFolder ? new BackupFolderChecked(AvailableBytes: 871_878_361_088) : null;
        ui.Click(ui.Find<RadioButton>("BackupsInFolder"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Agent.Requests.OfType<ChangeSettings>().Single().Settings.Should().Match<SettingsView>(settings =>
            settings.BackupLocation == BackupLocation.CustomFolder && settings.CustomBackupFolder == BackupFolder);
        Page(ui).Backups.FreeSpace.Should().Be("Wolne miejsce na dysku E: 812,0\u00A0GB");
        Page(ui).Backups.FolderProblem.Should().BeNull();
        ui.Find<Button>("PickBackupFolder").IsEffectivelyVisible.Should().BeTrue("the folder taken shows with a way to pick another");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("settings-backups-folder", height: 1500);

        ui.Agent.Answer = request => request is ValidateBackupFolder ? new BackupFolderChecked(BackupFolderProblem.Missing) : null;
        Page(ui).Backups.Load();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Find<Control>("FolderProblem").IsEffectivelyVisible.Should().BeTrue("the folder taken is checked again whenever the page shows");
        ui.Click(ui.Find<RadioButton>("BackupsInGameFolder"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Find<Control>("FolderProblem").IsVisible.Should().BeFalse("the folder's problem goes with the folder");
    }

    [AvaloniaFact]
    public async Task Backups_left_in_the_earlier_place_are_moved_game_by_game_and_the_moving_stops_after_the_running_one()
    {
        var games = new[] { Installed("Ember Hollow", "4100"), Installed("Quiet Harbor", "4200"), Installed("Saltmarsh", "4300") };
        var state = State(settings => settings with { BackupLocation = BackupLocation.AppData }, games);
        using var ui = await OpenAsync(state);
        Page(ui).Backups.Elsewhere.Should().Be(Text.Format(Strings.Settings_BackupsElsewhere, ("count", 3),
            ("place", Strings.Settings_PlaceGameFolders)));
        Page(ui).Backups.Usage.Should().BeNull("the backups are not all where the setting says");

        ui.Click(ui.Find<Button>("MoveBackups"));
        ui.Agent.Requests.OfType<MoveBackups>().Should().ContainSingle();

        var job = TestStates.Job(JobKind.MoveBackups, JobStage.Installing, fraction: 0.4) with { GameName = "Ember Hollow" };
        ui.Push(state with { Version = 5, Jobs = [job], PendingBackupMoves = 2 });
        var backups = Page(ui).Backups;
        backups.IsMoving.Should().BeTrue();
        backups.Elsewhere.Should().BeNull("the backups are on their way");
        backups.MovingDetail.Should().Be("Ember Hollow · 1 z 3 gier");
        backups.MovingPercent.Should().Be("40%");
        ui.Agent.Progress(job with { Fraction = 0.7, Sequence = 2 });
        backups.MovingPercent.Should().Be("70%");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-backups-moving", height: 1500);

        ui.Push(state with { Version = 6, Jobs = [job with { Game = games[1].Key, GameName = "Quiet Harbor" }], PendingBackupMoves = 1 });
        backups.MovingDetail.Should().Be("Quiet Harbor · 2 z 3 gier");
        backups.Usage.Should().BeNull("the backups are on their way");
        ui.Click(ui.Find<Button>("StopMoving"));
        ui.Agent.Requests.OfType<StopMovingBackups>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task Backups_in_a_folder_that_is_gone_name_the_games_they_hold_back_and_are_not_offered_to_move_elsewhere()
    {
        const string folder = @"F:\Backups";
        var gone = Unreachable(Installed("Ember Hollow", "4100", state: MyGameState.BackupsUnavailable), folder);
        // A running game is told first, but its originals are out of reach all the same.
        var running = Unreachable(NotReady("Saltmarsh", "4300", GameInstallState.Running, GameStateReason.GameRunning), folder);
        var state = State(settings => settings with { BackupLocation = BackupLocation.CustomFolder, CustomBackupFolder = folder },
            gone, Installed("Quiet Harbor", "4200"), running);
        using var ui = await OpenAsync(state);
        var backups = Page(ui).Backups;

        backups.Unavailable.Should().Be(Text.Format(Strings.Settings_BackupsUnavailable, ("folder", folder), ("count", 2),
            ("games", "Ember Hollow, Saltmarsh")));
        backups.Usage.Should().BeNull();
        ui.Find<Control>("BackupsUnavailable").BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("settings-backups-unavailable", height: 1500);

        ui.Push(state with { Version = 2, Settings = state.Settings with { BackupLocation = BackupLocation.GameFolder } });
        backups.Unavailable.Should().BeNull();
        backups.StuckElsewhere.Should().Be(Text.Format(Strings.Settings_BackupsElsewhereUnavailable, ("count", 2),
            ("place", Text.Format(Strings.Settings_PlaceFolder, ("folder", folder)))));
        ui.Find<Button>("MoveBackups").IsEffectivelyVisible.Should().BeFalse("backups out of reach cannot be moved");
        backups.Usage.Should().BeNull();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("settings-backups-stuck", height: 1500);
    }

    [AvaloniaTheory]
    [InlineData(@"E:\")]
    [InlineData(@"E:\Game backups")]
    public async Task Backups_already_in_the_picked_folder_are_in_place_also_in_a_drives_root(string folder)
    {
        var game = Installed("Ember Hollow", "4100");
        game = game with
        {
            Installs = [game.Installs[0] with
            {
                BackupLocation = BackupLocation.CustomFolder,
                BackupDirectory = Path.Combine(folder, "ExampleApp", "steam-4100", Guid.Empty.ToString("N")),
            }],
        };
        using var ui = await OpenAsync(State(settings => settings with
        {
            BackupLocation = BackupLocation.CustomFolder,
            CustomBackupFolder = folder,
        }, game));
        var backups = Page(ui).Backups;

        backups.Elsewhere.Should().BeNull("the agent moves nothing that is in the folder already");
        ui.Find<Control>("BackupsElsewhere").IsVisible.Should().BeFalse();
        backups.Usage.Should().Be(Text.Format(Strings.Settings_BackupsKept, ("count", 1)));
    }

    [AvaloniaFact]
    public async Task The_apps_data_shows_what_it_takes_and_its_cache_is_cleared_only_while_no_job_runs()
    {
        var measured = new AppDataUsage(@"C:\Users\Example\AppData\Local\ExampleVendor\ExampleApp", 317_718_528, 224_395_264, 90_177_536,
            3_355_443);
        var state = State();
        using var ui = await OpenAsync(state, answer: request => request switch
        {
            GetAppDataUsage => measured,
            ClearCache => Clear(),
            _ => null,
        });

        Message? Clear()
        {
            measured = measured with { Total = 4_194_304, Downloads = 0, ImagesAndCatalogue = 838_861 };
            return null;
        }

        var data = Page(ui).Data;
        await UiHarness.WaitUntilAsync(() => data.IsMeasured);
        data.Usage.Should().Be("Folder danych aplikacji na dysku C: zajmuje 303,0\u00A0MB.");
        (data.Downloads, data.ImagesAndCatalogue, data.Logs).Should().Be(("214,0\u00A0MB", "86,0\u00A0MB", "3,2\u00A0MB"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-app-data", height: 3200);

        var answer = ui.Agent.Answer;
        ui.Agent.Answer = request => request is OpenFolder ? new FolderToOpen(@"D:\Example Data") : answer(request);
        ui.Click(ui.Find<Button>("OpenDataFolder"));
        ui.Agent.Requests.OfType<OpenFolder>().Should().Equal(new OpenFolder(AppFolder.Data));
        ui.Links.Folders.Should().Equal(@"D:\Example Data");

        ui.Push(state.With(TestStates.Job()));
        ui.Find<Button>("ClearCache").IsEffectivelyEnabled.Should().BeFalse("a job may be about to use a package");
        data.ClearText.Should().Be(Strings.Settings_ClearCacheBusy);
        ui.Push(state with { Version = 3 });
        ui.Click(ui.Find<Button>("ClearCache"));
        await UiHarness.WaitUntilAsync(() => data.Downloads == "0\u00A0B");

        ui.Agent.Requests.OfType<ClearCache>().Should().ContainSingle();
        data.ClearText.Should().Be("Pamięć podręczna została wyczyszczona. Zwolniono 299,2\u00A0MB.");
        data.Usage.Should().Be("Folder danych aplikacji na dysku C: zajmuje 4,0\u00A0MB.");
    }

    [AvaloniaFact]
    public async Task Every_project_is_uninstalled_only_after_a_confirmation_that_names_what_happens()
    {
        var ember = Installed("Ember Hollow", "4100");
        var harbor = Installed("Quiet Harbor", "4200");
        // A running game is told first, but the agent leaves its install alone all the same, since its originals are out of reach.
        var running = Unreachable(NotReady("Saltmarsh", "4300", GameInstallState.Running, GameStateReason.GameRunning), @"F:\Backups");
        var state = State(settings => settings, ember, harbor, running);
        using var ui = await OpenAsync(state, answer: request => request is GetAppDataUsage
            ? new AppDataUsage(@"C:\Users\Example\AppData\Local\ExampleVendor\ExampleApp", 317_718_528, 224_395_264, 90_177_536, 3_355_443)
            : null);
        var data = Page(ui).Data;
        data.UninstallText.Should().Be("Oryginalne pliki zostaną przywrócone w 2 grach. Uruchomione gry poczekają na zamknięcie.");

        ui.Click(ui.Find<Button>("UninstallAll"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.Current.Should().BeOfType<ConfirmDialogViewModel>().Which.Message.Should().Be(
            "Spolszczenia zostaną odinstalowane z 2 gier, a gry odzyskają oryginalne pliki. Uruchomione gry poczekają na zamknięcie. "
            + "Automatyczne instalowanie spolszczeń w tych grach zostanie wyłączone. "
            + "1 gra zachowa spolszczenie, bo jej oryginalne pliki są teraz niedostępne.");
        ui.Render("settings-uninstall-all-confirm");
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Agent.Requests.OfType<UninstallEverything>().Should().BeEmpty();
        ui.Render("settings-uninstall-all");

        ui.Click(ui.Find<Button>("UninstallAll"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Click(ui.Find<Button>().Last(button => button.IsEffectivelyVisible && button.Content as string == Strings.Settings_UninstallAllButton));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Agent.Requests.OfType<UninstallEverything>().Should().ContainSingle();

        // A game with no other job starts its uninstall at once, so none of them waits.
        JobView[] jobs = [Job(ember, JobKind.Uninstall, JobStage.Installing), Job(harbor, JobKind.Uninstall, JobStage.Installing)];
        ui.Push(state with { Version = 2, Games = [Busy(ember, jobs[0]), Busy(harbor, jobs[1]), running], Jobs = jobs });
        data.UninstallText.Should().Be("Trwa odinstalowywanie. Zostały 2 gry.");
        ui.Find<Button>("UninstallAll").IsEffectivelyEnabled.Should().BeFalse("the uninstalls are under way");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-uninstall-all-running", height: 3200);
        var update = Job(ember, JobKind.Update, JobStage.Downloading);
        ui.Push(state with
        {
            Version = 3,
            Games = [Busy(ember, update), Busy(harbor, jobs[1]), running],
            Jobs = [update, jobs[1]],
            PendingUninstalls = 1,
        });
        data.UninstallText.Should().Be("Trwa odinstalowywanie. Zostały 2 gry.", "a game whose uninstall waits for another job is still to go");
        ui.Find<Button>("UninstallAll").IsEffectivelyEnabled.Should().BeFalse();
        ui.Push(state with { Version = 4, Games = [running] });
        data.UninstallText.Should().Be(Strings.Settings_UninstallAllUnreachable);
        ui.Find<Button>("UninstallAll").IsEffectivelyEnabled.Should().BeFalse("nothing could be put back now");
    }

    [AvaloniaFact]
    public async Task Leftover_backups_show_their_size_and_go_only_after_the_player_confirms()
    {
        var leftover = new LeftoverBackup(Guid.NewGuid(), SteamKey("4400"), "Tidewatch", @"E:\Game backups\ExampleApp\steam-4400", 39_845_888);
        var listed = new List<LeftoverBackup> { leftover };
        using var ui = await OpenAsync(State(), answer: request => request switch
        {
            GetLeftoverBackups => new LeftoverBackups([.. listed]),
            RemoveLeftoverBackups => Remove(),
            _ => null,
        });

        Message? Remove()
        {
            listed.Clear();
            return null;
        }

        var backups = Page(ui).Backups;
        backups.Leftovers.Should().ContainSingle().Which.Detail.Should().Be("38,0\u00A0MB · E:\\Game backups\\ExampleApp\\steam-4400");
        var remove = ui.Find<Button>().Single(button => AutomationProperties.GetName(button) == "Usuń kopie gry Tidewatch");
        ui.Click(remove);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("settings-leftover-confirm");
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Agent.Requests.OfType<RemoveLeftoverBackups>().Should().BeEmpty();

        ui.Click(remove);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Click(ui.Find<Button>().Last(button => button.IsEffectivelyVisible && button.Content as string == Strings.Settings_RemoveLeftover));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Agent.Requests.OfType<RemoveLeftoverBackups>().Single().RecordIds.Should().Equal(leftover.RecordId);
        backups.HasLeftovers.Should().BeFalse("the list is read again once they are gone");
    }

    [AvaloniaFact]
    public async Task Kept_copies_are_listed_with_the_backups_of_games_that_are_gone_and_offer_steam_s_check()
    {
        var leftovers = KeptCopies();
        using var ui = await OpenAsync(State(), answer: request => request switch
        {
            GetLeftoverBackups => new LeftoverBackups([.. leftovers]),
            _ => null,
        });
        var backups = Page(ui).Backups;
        var rows = backups.Leftovers.ToDictionary(row => row.Backup.GameName);

        var updated = rows["Ember Hollow"];
        updated.Detail.Should().Be("Ember Hollow PL · 1 plik · 96,0\u00A0KB");
        updated.Lead.Should().Be("Gra dostała aktualizację, gdy spolszczenie „Ember Hollow PL” było zainstalowane, więc odinstalowanie zostawiło "
            + "pliki zmieniane przez jego oprogramowanie bez zmian i zachowało kopie ich oryginałów.");
        updated.Advice.Should().Be("Sprawdzenie spójności plików gry w Steam przywróci jej własne pliki; potem kopie można usunąć.");
        (updated.IsAdvice, updated.AreFilesShown).Should().Be((false, false));
        var doubted = rows["Glass Orchard"];
        (doubted.IsAdvice, doubted.HasCheckFiles).Should().Be((true, false), "a doubted original only advises, and GOG has no link");
        doubted.Advice.Should().Be("Sprawdzenie spójności plików gry w GOG przywróci jej własne pliki; potem kopie można usunąć.");
        rows["Tidewatch"].IsKept.Should().BeFalse();

        ui.Find<ItemsControl>("Leftovers").BringIntoView();
        UiHarness.Settle();
        var toggle = ui.Find<Button>().First(button => button.DataContext == updated && button.Classes.Contains("filesToggle"));
        AutomationProperties.GetName(toggle).Should().Be("Pokaż pliki");
        ui.Click(toggle);
        updated.AreFilesShown.Should().BeTrue();
        AutomationProperties.GetName(toggle).Should().Be("Ukryj pliki");
        ui.Find<PathList>().Single(list => list.IsEffectivelyVisible).Paths.Should().Equal("data/list.dat");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-kept");
        ui.Unnamed().Should().BeEmpty();

        ui.Click(ui.Find<Button>().Single(button => button.DataContext == updated && button.Classes.Contains("keptCheck")));
        ui.Links.Opened.Should().Equal(new Uri("steam://validate/4100"));

        ui.Click(ui.Find<Button>().Single(button => AutomationProperties.GetName(button) == "Usuń kopie gry Ember Hollow"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Message.Should().Be("Kopie (96,0\u00A0KB) zostaną usunięte. Najlepiej po "
            + "sprawdzeniu spójności plików gry w Steam albo gdy gra działa poprawnie.");
        ui.Render("settings-kept-confirm");
        ui.Shell.Dialogs.CancelCurrent();
    }

    [AvaloniaFact]
    public async Task Kept_copies_keep_their_note_and_steam_s_check_whole_in_the_smallest_window()
    {
        using var ui = await OpenAsync(State(), width: 960, height: 600, answer: request => request switch
        {
            GetLeftoverBackups => new LeftoverBackups([.. KeptCopies()]),
            _ => null,
        });
        var updated = Page(ui).Backups.Leftovers.Single(row => row.Backup.GameName == "Ember Hollow");
        updated.AreFilesShown = true;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var check = ui.Find<Button>().Single(button => button.DataContext == updated && button.Classes.Contains("keptCheck"));
        check.BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(200));

        check.TranslatePoint(new Point(check.Bounds.Width, 0), ui.Window)!.Value.X.Should().BeLessThan(960 - 16);
        ui.Render("settings-kept-minimum");
    }

    [AvaloniaFact]
    public async Task Preferred_teams_are_added_from_the_catalogue_and_reordered_with_buttons_keys_or_the_pointer()
    {
        var state = State(settings => settings with { PreferredTeams = [Lanterns.Id, Owls.Id] }) with
        {
            Catalogue = new CatalogueView { FetchedAt = TestStates.Now },
        };
        using var ui = await OpenAsync(state, answer: request => request is GetCatalogueFilters
            ? new CatalogueFiltersResult(new CatalogueFiltersView
            {
                Teams = [new(Lanterns.Id, Lanterns.Name), new(Owls.Id, Owls.Name), new(Cranes.Id, Cranes.Name)],
            })
            : null);
        var teams = Page(ui).Teams;
        await UiHarness.WaitUntilAsync(() => teams.CanAdd);
        teams.Rows.Select(row => row.Name).Should().Equal(Lanterns.Name, Owls.Name);

        teams.Query = "paper";
        UiHarness.Settle();
        teams.Matches.Should().ContainSingle().Which.Name.Should().Be(Cranes.Name);
        ui.Click(ui.Find<Button>().Single(button => AutomationProperties.GetName(button) == teams.Matches[0].AddName));
        LastTeams(ui).Should().Equal(Lanterns.Id, Owls.Id, Cranes.Id);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-teams", height: 1500);

        ui.Click(ui.Find<Button>().Single(button => AutomationProperties.GetName(button) == teams.Rows[2].MoveUpName));
        LastTeams(ui).Should().Equal(Lanterns.Id, Cranes.Id, Owls.Id);

        var lanterns = Row(ui, Lanterns.Name);
        lanterns.Focus(NavigationMethod.Tab);
        ui.Press(Key.Down, RawInputModifiers.Alt);
        LastTeams(ui).Should().Equal(Cranes.Id, Lanterns.Id, Owls.Id);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(50));
        Row(ui, Lanterns.Name).IsFocused.Should().BeTrue("the moved team keeps the keyboard");

        var grip = Row(ui, Owls.Name).GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("grip"));
        var from = grip.TranslatePoint(new(grip.Bounds.Width / 2, grip.Bounds.Height / 2), ui.Window)!.Value;
        var top = Row(ui, Cranes.Name).TranslatePoint(new(10, 4), ui.Window)!.Value;
        ui.Window.MouseDown(from, MouseButton.Left);
        ui.Window.MouseMove(new(from.X, top.Y));
        ui.Window.MouseUp(new(from.X, top.Y), MouseButton.Left);
        UiHarness.Settle();
        LastTeams(ui).Should().Equal(Owls.Id, Cranes.Id, Lanterns.Id);

        var cranes = (TeamRow)Row(ui, Cranes.Name).DataContext!;
        ui.Click(ui.Find<Button>().Single(button => AutomationProperties.GetName(button) == cranes.RemoveName));
        LastTeams(ui).Should().Equal(Owls.Id, Lanterns.Id);
    }

    [AvaloniaFact]
    public async Task Folders_added_by_hand_show_what_was_found_in_them_and_can_be_taken_off_the_list()
    {
        var game = Available("Ember Hollow", "4100") with
        {
            Detected = new DetectedGameView { InstallDirectory = @"D:\GOG Games\Ember Hollow", LibraryId = "gog-folder-1" },
        };
        var state = State(settings => settings, game) with
        {
            Library = new LibraryView { ScannedAt = TestStates.Now, AddedFolders = [new LibraryFolderView("gog", @"D:\GOG Games")] },
        };
        var report = new DetectionReportView(
        [
            new StoreDetectionView("gog")
            {
                IsFound = true,
                Libraries = [new LibraryDetectionView("gog-folder-1", "gog-folder") { Path = @"D:\GOG Games", IsAdded = true, GameCount = 2 }],
            },
        ]);
        using var ui = await OpenAsync(state, answer: request => request is GetDetectionReport ? new DetectionReportResult(report) : null);

        var row = Page(ui).Folders.Rows.Should().ContainSingle().Which;
        row.Detail.Should().Be("Folder gier z GOG · 2 gry, w tym ze spolszczeniem: 1");
        ui.Click(ui.Find<Button>().Single(button => AutomationProperties.GetName(button) == row.RemoveName));
        ui.Agent.Requests.OfType<RemoveLibraryFolder>().Should().Equal(new RemoveLibraryFolder("gog", @"D:\GOG Games"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("settings-folders", height: 3200);
    }

    [AvaloniaFact]
    public async Task Error_reports_go_without_asking_only_once_the_player_turns_them_on_and_the_page_says_what_they_hold()
    {
        using (var withoutPlace = await OpenAsync(State()))
        {
            withoutPlace.Find<Control>("ErrorReports").IsVisible.Should().BeFalse("a build without a place for reports offers none");
        }

        using var ui = new UiHarness(State(), launch: new SettingsTarget(), errorReports: true);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        var toggle = ui.Find<ToggleSwitch>("SendErrorReports");
        toggle.IsChecked.Should().BeFalse("reports wait for the player's consent");
        ui.Find<Button>("ErrorReportExample").BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-error-reports");

        ui.Click(toggle);
        ui.Agent.Requests.OfType<ChangeSettings>().Should().ContainSingle().Which.Settings.SendErrorReports.Should().BeTrue();
        ui.Click(ui.Find<Button>("ErrorReportExample"));
        var example = ui.Shell.Dialogs.Current.Should().BeOfType<ErrorReportExampleViewModel>().Subject;
        example.Fields.Select(field => field.Value).Should().Contain(["Example App 0.1.0", "okno"]);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-error-reports-example");
        ui.Click(ui.Find<Button>("CloseExample"));
        (await example.Result).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Test_versions_come_as_updates_only_once_the_player_turns_them_on()
    {
        var state = State() with { Update = new AppUpdateView { State = AppUpdateState.UpToDate, CurrentVersion = "0.1.0" } };
        using var ui = await OpenAsync(state);
        var toggle = ui.Find<ToggleSwitch>("UpdatePrereleases");
        toggle.IsChecked.Should().BeFalse("a player takes the stable versions unless they ask for test ones");

        ui.Click(toggle);
        ui.Agent.Requests.OfType<ChangeSettings>().Should().ContainSingle().Which.Settings.UpdatePrereleases.Should().BeTrue();
        ui.Push(state with { Version = 2, Update = state.Update with { State = AppUpdateState.Unavailable } });
        toggle.IsEffectivelyEnabled.Should().BeFalse("a copy that does not update itself has nothing to switch");
    }

    [AvaloniaFact]
    public async Task The_about_card_follows_the_apps_update_and_leads_to_the_site_the_code_a_report_the_logs_and_the_licences()
    {
        var state = State() with
        {
            Update = new AppUpdateView { State = AppUpdateState.UpToDate, CurrentVersion = "0.1.0", CheckedAt = TestStates.Now.AddHours(-3) },
        };
        using var ui = await OpenAsync(state, context => context with
        {
            SourceAddress = new Uri("https://code.example.com/example-app"),
            IssuesAddress = new Uri("https://code.example.com/example-app/issues/new"),
        });
        var page = Page(ui);
        page.UpdateText.Should().Be("Aktualizacje są sprawdzane co 4 godziny, ostatnio dziś o 11:58.");
        ui.Click(ui.Find<Button>("CheckForUpdates"));
        ui.Agent.Requests.OfType<CheckNow>().Should().ContainSingle();

        ui.Push(state with { Version = 3, Update = state.Update with { State = AppUpdateState.Downloading, Version = "0.2.0", Progress = 64 } });
        page.UpdateText.Should().Be("Pobieranie wersji 0.2.0 · 64%");
        ui.Find<Button>("CheckForUpdates").IsEffectivelyEnabled.Should().BeFalse("a download is already under way");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-about-downloading", height: 3200);
        ui.Push(state with { Version = 4, Update = state.Update with { State = AppUpdateState.Ready, Version = "0.2.0" } });
        ui.Find<Button>("CheckForUpdates").IsVisible.Should().BeFalse("the sidebar's card restarts into a ready update");
        ui.Push(state with { Version = 5, Update = state.Update with { State = AppUpdateState.Failed } });
        page.UpdateText.Should().Be(Strings.Settings_UpdateFailed);
        ui.Find<ToggleSwitch>("AutoUpdateApp").IsEffectivelyEnabled.Should().BeTrue();
        ui.Push(state with { Version = 6, Update = state.Update with { State = AppUpdateState.Unavailable } });
        ui.Find<ToggleSwitch>("AutoUpdateApp").IsEffectivelyEnabled.Should().BeFalse("a copy that does not update itself has nothing to switch");
        ui.Push(state with { Version = 7, Update = state.Update with { State = AppUpdateState.Failed } });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-about-failed", height: 3200);

        ui.Click(ui.Find<Button>("OpenSite"));
        ui.Click(ui.Find<Button>("OpenSource"));
        ui.Click(ui.Find<Button>("ReportProblem"));
        ui.Links.Opened[0].Should().Be(new Uri("https://example.com/"));
        ui.Links.Opened[1].Should().Be(new Uri("https://code.example.com/example-app"));
        var report = Uri.UnescapeDataString(ui.Links.Opened[2].Query);
        report.Should().StartWith("?template=bug_report.yml&version=0.1.0&system=").And.NotContain(Environment.UserName,
            "a report carries the app's and the system's version and nothing else");

        var answer = ui.Agent.Answer;
        ui.Agent.Answer = request => request is OpenFolder ? new FolderToOpen(@"D:\Example Data\logs") : answer(request);
        ui.Click(ui.Find<Button>("OpenLogs"));
        ui.Agent.Requests.OfType<OpenFolder>().Should().Equal(new OpenFolder(AppFolder.Logs));
        ui.Links.Folders.Should().Equal(@"D:\Example Data\logs");
        ui.Click(ui.Find<Button>("OpenLicenses"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LicensesRoute());
    }

    [AvaloniaFact]
    public async Task The_about_card_names_the_site_its_catalogue_comes_from_only_in_the_apps_own_words()
    {
        var state = State() with { Catalogue = new CatalogueView { SiteName = "Example Site" } };
        var shared = TestApp.SharedPolish();
        // An app is named after the sites it serves, so the words of the apps' names stand for the sites' names too.
        var names = Directory.EnumerateFiles(Path.Combine(TestApp.RepositoryRoot, "src"), "Product.props", SearchOption.AllDirectories)
            .SelectMany(file => XDocument.Load(file).Descendants("Product").Select(product => product.Value))
            .SelectMany(name => name.Split(' ')).ToList();
        names.Should().NotBeEmpty();
        new[] { TextCatalog.Base, shared }.Should().AllSatisfy(texts => texts[nameof(Strings.Settings_AboutText)].Should()
            .NotContainAny(names).And.NotContain("{", "a shared text names no app or site and leaves no place for one"));

        Text.Use(shared);
        try
        {
            using var generic = await OpenAsync(state);
            generic.Find<TextBlock>("AboutApp").Text.Should().Be(shared[nameof(Strings.Settings_AboutText)]).And.NotContain("Example Site");
        }
        finally
        {
            Text.Use(TestApp.PolishApp());
        }

        using var ui = await OpenAsync(state);
        ui.Find<TextBlock>("AboutApp").Text.Should().Be(TestApp.PolishApp()[nameof(Strings.Settings_AboutText)])
            .And.NotBe(shared[nameof(Strings.Settings_AboutText)], "an app may say whose it is in texts of its own");
    }

    [AvaloniaFact]
    public async Task A_ready_update_shows_what_it_changes_in_a_dialog_from_the_notes_it_carries()
    {
        const string notes = "## Nowe funkcje\n\n- Ustawienia pokazują, ile miejsca zajmują dane aplikacji.\n"
            + "- Pamięć podręczną można wyczyścić.\n\n[Kod źródłowy tej wersji](https://code.example.com/example-app/tree/v0.2.0)";
        var ready = new AppUpdateView { State = AppUpdateState.Ready, CurrentVersion = "0.1.0", Version = "0.2.0" };
        using var ui = await OpenAsync(State() with { Update = ready });
        ui.Find<Button>("WhatsNew").IsVisible.Should().BeFalse("an update without notes has nothing to show");

        ui.Push(State() with { Version = 2, Update = ready with { ReleaseNotes = notes } });
        var whatsNew = ui.Find<Button>("WhatsNew");
        whatsNew.Content.Should().Be("Co nowego w 0.2.0");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("settings-about-ready", height: 3200);
        ui.Click(whatsNew);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ReleaseNotesDialogViewModel { Notes: not null });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("settings-release-notes");

        var dialog = (ReleaseNotesDialogViewModel)ui.Shell.Dialogs.Current!;
        dialog.Title.Should().Be("Co nowego w 0.2.0");
        dialog.OpenLinkCommand.Execute(ui.Find<ProseText>().Single(text => text.Links.Count > 0).Links[0]);
        ui.Links.Opened.Should().Equal(new Uri("https://code.example.com/example-app/tree/v0.2.0"));
        ui.Click(ui.Find<Button>("CloseNotes"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.Current.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task Every_primary_action_stays_in_reach_in_the_smallest_window()
    {
        using var ui = await OpenAsync(State(), width: 960, height: 600);

        ui.Find<RadioButton>("CloseAsk").Bounds.Width.Should().BeGreaterThan(150, "three tiles still fit side by side");
        ui.Find<Button>("CheckForUpdates").BringIntoView();
        UiHarness.Settle();
        ui.Find<Button>("CheckForUpdates").IsEffectivelyVisible.Should().BeTrue();
        ui.Unnamed().Should().BeEmpty();
    }

    // The sections enter one after another; a click lands where a section ends up once it has entered.
    private static async Task<UiHarness> OpenAsync(AppStateView state, Func<UiContext, UiContext>? context = null,
        Func<Message, Message?>? answer = null, double width = 1280, double height = 800)
    {
        var ui = new UiHarness(state, width: width, height: height, launch: new SettingsTarget(), context: context, answer: answer);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        return ui;
    }

    private static AppStateView State(Func<SettingsView, SettingsView>? settings = null, params GameView[] games)
    {
        var idle = TestStates.Idle();
        return idle with { Settings = settings is null ? idle.Settings : settings(idle.Settings), Games = games };
    }

    private static GameView Unreachable(GameView game, string folder) => game with
    {
        Installs = [game.Installs[0] with
        {
            BackupLocation = BackupLocation.CustomFolder,
            BackupDirectory = $@"{folder}\ExampleApp\steam-{game.Key.StoreGameId}\{Guid.Empty:N}",
            BackupsUnavailable = true,
        }],
    };

    private static SettingsPageViewModel Page(UiHarness ui) => (SettingsPageViewModel)ui.Shell.Navigator.Current!;

    // A forgotten install's backups, and copies two uninstalls kept: one after a game update, one of doubted originals on GOG.
    private static List<LeftoverBackup> KeptCopies() =>
    [
        new(Guid.NewGuid(), SteamKey("4400"), "Tidewatch", @"E:\Game backups\ExampleApp\steam-4400", 39_845_888)
        {
            Cover = new Uri(Cover("tidewatch")),
        },
        new(Guid.NewGuid(), SteamKey("4100"), "Ember Hollow", @"D:\Games\Ember Hollow\.example-app\backups\1", 98_304)
        {
            GameDirectory = @"D:\Games\Ember Hollow",
            Cover = new Uri(Cover("ember-hollow")),
            Kept = new KeptFilesSummary(KeptReason.GameUpdatedSinceInstall, ["data/list.dat"], 1, "Ember Hollow PL", TestStates.Now.AddDays(-1)),
        },
        new(Guid.NewGuid(), new GameKey(StoreId.Gog, "1207658930"), "Glass Orchard", @"D:\Games\Glass Orchard\.example-app\backups\2",
            1_468_006)
        {
            GameDirectory = @"D:\Games\Glass Orchard",
            Kept = new KeptFilesSummary(KeptReason.OriginalUnknown, ["bin/loader.cfg", "data/cache/list.dat"], 2, "Glass Orchard PL",
                TestStates.Now.AddDays(-2)),
        },
    ];

    private static ToggleSwitch Toggle(UiHarness ui, string name) =>
        ui.Find<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == name);

    private static Border Row(UiHarness ui, string team) =>
        ui.Find<Border>().Single(border => border.Classes.Contains("teamRow") && border.DataContext is TeamRow row && row.Name == team);

    private static IReadOnlyList<Guid> LastTeams(UiHarness ui) => ui.Agent.Requests.OfType<ChangeSettings>().Last().Settings.PreferredTeams;
}
