using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class GameTests
{
    private static readonly ProjectOfferView Ember = Offer("Ember Hollow", twoVariants: true);

    private static readonly Guid SiteId = new("3e0c5a1d-7b2f-4c8e-9a6d-5f4e3d2c1b0a");

    [AvaloniaFact]
    public async Task Installing_takes_the_variant_chosen_under_the_line_and_shows_the_job_live_until_it_is_cancelled()
    {
        var game = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Details is not null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("game-install");

        page.Step!.Kind.Should().Be(StepKind.Install);
        page.Step.PrimaryCaption.Should().Be("Wersja 2 · 3,6\u00A0MB");
        page.NewFeatures!.Line.Should().Be("Wariant: Full");
        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(ui.Find<RadioButton>().Single(pill => pill.DataContext is FeaturePill { Key: "Subtitles only" }));
        page.Step.PrimaryCaption.Should().Be("Wersja 2 · 2,1\u00A0MB");
        page.NewFeatures.Line.Should().Be("Wariant: Subtitles only");
        ui.Agent.Requests.OfType<ChooseFeatures>().Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ChooseFeatures(game.Key, Ember.ProjectId, new Dictionary<string, bool?> { ["Subtitles only"] = true }));
        ui.Click(ui.Find<Button>("PrimaryButton"));
        ui.Agent.Requests.OfType<InstallProject>().Should().Equal(new InstallProject(game.Key, Ember.ProjectId));

        var job = Job(game, JobKind.Install, JobStage.Downloading, fraction: 0.4);
        ui.Push(State(Busy(game, job)));
        page.Step.Kind.Should().Be(StepKind.Job);
        page.Steps.Select(step => (step.Label, step.State)).Should().Equal(
            ("Pobieranie", StepState.Current), ("Sprawdzanie", StepState.Pending), ("Instalowanie", StepState.Pending));
        var band = page.Step;
        ui.Clock.Advance(TimeSpan.FromSeconds(2));
        ui.Agent.Progress(job with { Fraction = 0.6, BytesDone = 113_246_208, Sequence = 2 });
        UiHarness.Settle();
        page.Step.Should().BeSameAs(band, "progress moves the bar without building the step again");
        page.PercentText.Should().Be("60%");
        page.StageText.Should().StartWith("Pobieranie · 108,0\u00A0MB z 180,0\u00A0MB");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-installing");

        ui.Click(ui.Find<Button>("SecondaryButton"));
        ui.Agent.Requests.OfType<CancelJob>().Should().Equal(new CancelJob(job.Id));
        ui.Push(State(game) with { Version = 7 });
        page.Step.Kind.Should().Be(StepKind.Cancelled);
        page.Step.Primary.Should().Be("Zainstaluj spolszczenie", "the step that was there before the job stays offered");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-cancelled");
    }

    [AvaloniaFact]
    public void A_report_after_the_state_that_ended_its_job_brings_back_neither_its_band_nor_its_card()
    {
        var game = Available("Ember Hollow", "4100", Ember);
        var job = Job(game, JobKind.Install, JobStage.Installing, fraction: 0.9);
        using var ui = Open(State(Busy(game, job)), game);
        var page = Page(ui);
        var card = ui.Shell.Navigator.Pages.OfType<LibraryPageViewModel>().Single().GridItems.OfType<GameCardViewModel>().Single();

        ui.Push(State(Installed("Ember Hollow", "4100", offer: Ember)) with { Version = 5 });
        ui.Agent.Progress(job with { Fraction = 1, Sequence = 2 });
        UiHarness.Settle();

        page.Step!.Kind.Should().Be(StepKind.Done);
        card.ShowsProgress.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Uninstalling_asks_first_and_leaves_its_result_where_the_step_was()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game);
        var page = Page(ui);
        page.Step!.Kind.Should().Be(StepKind.Current);

        var uninstall = ui.Find<Button>().First(button => button.Classes.Contains("danger") && button.IsEffectivelyVisible);
        ui.Click(uninstall);
        ui.Shell.Dialogs.Current.Should().BeOfType<ConfirmDialogViewModel>().Which.IsDanger.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-uninstall-confirm");
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).ConfirmCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<UninstallProject>().Any());
        ui.Agent.Requests.OfType<UninstallProject>().Should().Equal(new UninstallProject(game.Installs[0].Id));

        ui.Push(State(Busy(game, Job(game, JobKind.Uninstall, JobStage.Uninstalling, fraction: 0.55))));
        page.Steps.Select(step => step.Label).Should().Equal("Sprawdzanie", "Przywracanie oryginałów");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-uninstalling");

        ui.Push(State() with { Version = 9 });
        page.IsMissing.Should().BeFalse("the page keeps what it showed once the game leaves the list");
        page.Step!.Kind.Should().Be(StepKind.Removed);
        ui.Click(ui.Find<Button>("SecondaryButton"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
    }

    [AvaloniaFact]
    public async Task An_uninstall_says_which_files_it_left_as_they_were_whether_its_end_or_the_state_comes_first()
    {
        var installed = Installed("Ember Hollow", "4100", offer: Ember);
        var available = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(State(installed), installed);
        var page = Page(ui);
        var first = Job(installed, JobKind.Uninstall, JobStage.Uninstalling);

        ui.Push(State(Busy(installed, first)) with { Version = 5 });
        ui.Agent.End(new JobEnded(first, JobOutcome.Succeeded) { KeptFiles = [new KeptFilesView(KeptReason.ChangedSinceInstall, 2)] });
        ui.Push(State(available) with { Version = 6 });

        page.Step!.Kind.Should().Be(StepKind.Removed);
        page.Step.Text.Should().Be("Przywrócono oryginalne pliki gry. 2 pliki zmienione po instalacji przez aktualizację gry zostały w wersji "
            + "z aktualizacji.");
        (page.Step.Tone, page.Step.HasCheckFiles).Should().Be((Tone.Success, false), "the files are the store's own, so nothing is to do");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("removed-2-changed-after-install");

        var second = first with { Id = Guid.NewGuid() };
        ui.Push(State(Busy(installed, second)) with { Version = 7 });
        ui.Push(State(available) with { Version = 8 });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry.");
        page.Step.Primary.Should().BeNull("closing the result shows the step that comes next");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("removed-1-nothing-left");
        ui.Agent.End(new JobEnded(second, JobOutcome.Succeeded)
        {
            KeptFiles = [new KeptFilesView(KeptReason.ChangedSinceInstall, 1), new KeptFilesView(KeptReason.BackupMissing, 5)],
        });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry poza 5 plikami, które zostały w wersji spolszczenia, bo kopie ich "
            + "oryginałów zniknęły albo są uszkodzone. Oryginalne pliki przywróci sprawdzenie spójności plików gry w Steam.",
            "the agent may tell which files stayed after the state ended the job, and a lost copy says more than a changed file");
        page.Step.Tone.Should().Be(Tone.Warning);

        ui.Click(ui.Find<Button>("DismissStep"));
        page.Step!.Kind.Should().Be(StepKind.Install);
    }

    [AvaloniaFact]
    public async Task An_uninstall_after_a_game_update_says_the_files_stay_and_offers_steam_s_check()
    {
        var installed = Installed("Ember Hollow", "4100", offer: Ember);
        var available = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(State(installed), installed);
        var page = Page(ui);
        var job = Job(installed, JobKind.Uninstall, JobStage.Uninstalling);

        ui.Push(State(Busy(installed, job)) with { Version = 5 });
        ui.Agent.End(new JobEnded(job, JobOutcome.Succeeded)
        {
            KeptFiles = [new KeptFilesView(KeptReason.GameUpdatedSinceInstall, 1), new KeptFilesView(KeptReason.OriginalUnknown, 1)],
        });
        ui.Push(State(available) with { Version = 6 });

        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry poza plikami, które oprogramowanie spolszczenia zmieniało podczas gry: "
            + "w międzyczasie gra dostała aktualizację, więc zostały bez zmian, a kopie ich oryginałów zachowano. Oryginalne pliki przywróci "
            + "sprawdzenie spójności plików gry w Steam.");
        page.Step.Tone.Should().Be(Tone.Warning);
        var check = ui.Find<Button>("CheckFilesButton");
        check.IsEffectivelyVisible.Should().BeTrue();
        AutomationProperties.GetName(check).Should().Be("Sprawdź spójność plików w Steam");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("removed-3-game-updated");
        ui.Click(check);
        ui.Links.Opened.Should().Equal(new Uri("steam://validate/4100"));

        var doubted = job with { Id = Guid.NewGuid() };
        ui.Push(State(Busy(installed, doubted)) with { Version = 7 });
        ui.Agent.End(new JobEnded(doubted, JobOutcome.Succeeded) { KeptFiles = [new KeptFilesView(KeptReason.OriginalUnknown, 2)] });
        ui.Push(State(available) with { Version = 8 });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry. Pliki zmieniane przez oprogramowanie spolszczenia mają postać sprzed "
            + "instalacji tego wydania, ale już wtedy były zmienione; oryginalne pliki przywróci sprawdzenie spójności plików gry w Steam.");
        (page.Step.Tone, page.Step.HasCheckFiles).Should().Be((Tone.Info, true), "nothing was held back, so it only advises");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("removed-4-original-unknown");
    }

    [AvaloniaFact]
    public async Task At_the_smallest_window_the_removed_step_leads_the_panel_with_steam_s_check_under_its_text()
    {
        var installed = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = new UiHarness(State(installed), width: 960, height: 600);
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(installed.Key)]);
        UiHarness.Settle();
        var page = Page(ui);
        var job = Job(installed, JobKind.Uninstall, JobStage.Uninstalling);

        ui.Push(State(Busy(installed, job)) with { Version = 5 });
        ui.Agent.End(new JobEnded(job, JobOutcome.Succeeded) { KeptFiles = [new KeptFilesView(KeptReason.GameUpdatedSinceInstall, 1)] });
        ui.Push(State(Available("Ember Hollow", "4100", Ember)) with { Version = 6 });

        page.IsCompact.Should().BeTrue();
        var check = ui.Find<Button>().Single(button => button.Name == "CheckFilesUnder" && button.IsEffectivelyVisible);
        check.TranslatePoint(new Point(0, check.Bounds.Height), ui.Window)!.Value.Y.Should().BeLessThan(600);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("removed-6-minimum");
    }

    [AvaloniaFact]
    public async Task A_game_of_another_store_is_told_its_store_s_check_without_a_button()
    {
        var installed = Installed("Ember Hollow", "4100", offer: Ember) with { Key = new GameKey(StoreId.Gog, "1207658930") };
        using var ui = Open(State(installed), installed);
        var page = Page(ui);
        var job = Job(installed, JobKind.Uninstall, JobStage.Uninstalling);

        ui.Push(State(Busy(installed, job)) with { Version = 5 });
        ui.Agent.End(new JobEnded(job, JobOutcome.Succeeded) { KeptFiles = [new KeptFilesView(KeptReason.BackupDamaged, 1)] });
        ui.Push(State(installed with { Installs = [] }) with { Version = 6 });

        page.Step!.Text.Should().EndWith("Oryginalne pliki przywróci sprawdzenie spójności plików gry w GOG.");
        page.Step.HasCheckFiles.Should().BeFalse("only Steam starts its check from a link");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Find<Button>().Where(button => button.Name is "CheckFilesButton" or "CheckFilesUnder").Should().OnlyContain(button => !button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task An_install_whose_software_changes_files_says_so_and_unfolds_them()
    {
        var game = WithRuntimeFiles(Installed("Ember Hollow", "4100", offer: Ember));
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Details is not null);
        var files = page.Installs.Single().RuntimeFiles!;
        var toggle = ui.Find<Button>("RuntimeToggle");

        ui.Find<TrailingAction>("RuntimeLine").IsEffectivelyVisible.Should().BeTrue();
        var features = ui.Find<TrailingAction>().Single(line => line.Name == "FeatureLine" && line.IsEffectivelyVisible);
        features.TranslatePoint(default, ui.Window)!.Value.Y.Should().BeLessThan(
            ui.Find<TrailingAction>("RuntimeLine").TranslatePoint(default, ui.Window)!.Value.Y, "the files line stands under the add-ons line");
        AutomationProperties.GetName(toggle).Should().Be("Pokaż pliki");
        ui.Find<PathList>().Should().OnlyContain(list => !list.IsEffectivelyVisible, "the list is folded when the page opens");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("install-line-folded");

        toggle.Focus(NavigationMethod.Tab);
        ui.Press(Key.Space);
        files.AreShown.Should().BeTrue();
        AutomationProperties.GetName(toggle).Should().Be("Ukryj pliki");
        var list = ui.Find<PathList>().Single(shown => shown.IsEffectivelyVisible);
        (list.Paths, AutomationProperties.GetName(list)).Should().Be((files.Paths, "Pliki zmieniane podczas gry"));
        ui.Push(State(game) with { Version = 2 });
        page.Installs.Single().RuntimeFiles.Should().BeSameAs(files, "a state that changes nothing else keeps the files unfolded");
        toggle.IsFocused.Should().BeTrue("the row stays, and its toggle keeps the focus");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("install-line-unfolded");

        var plain = Installed("Ember Hollow", "4100", offer: Ember);
        ui.Push(State(plain) with { Version = 3 });
        page.Installs.Single().RuntimeFiles.Should().BeNull();
        ui.Find<TrailingAction>().Where(line => line.Name == "RuntimeLine").Should().OnlyContain(line => !line.IsEffectivelyVisible,
            "an install whose software changes nothing has no line");
    }

    [AvaloniaFact]
    public async Task At_the_smallest_window_the_line_of_files_the_software_changes_wraps_with_its_toggle_whole()
    {
        var game = WithRuntimeFiles(Installed("Ember Hollow", "4100", offer: Ember));
        using var ui = new UiHarness(State(game), width: 960, height: 600);
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        Page(ui).Installs.Single().RuntimeFiles!.AreShown = true;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var list = ui.Find<PathList>().Single(shown => shown.IsEffectivelyVisible);
        list.BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));

        var toggle = ui.Find<Button>("RuntimeToggle");
        var line = ui.Find<TrailingAction>("RuntimeLine");
        toggle.TranslatePoint(new Point(toggle.Bounds.Width, 0), line)!.Value.X.Should().BeLessThanOrEqualTo(line.Bounds.Width);
        list.TranslatePoint(new Point(list.Bounds.Width, 0), ui.Window)!.Value.X.Should().BeLessThan(960);
        ui.Render("install-line-minimum");
    }

    [AvaloniaFact]
    public async Task An_install_refused_for_kept_files_says_what_comes_first_and_offers_steam_s_check()
    {
        var game = Available("Ember Hollow", "4100", Ember) with
        {
            LastFailure = new FailureView(JobKind.Install, JobOrigin.User, TestStates.Now.AddMinutes(-3),
                new JobFailureView(JobFailureReason.KeptFilesPending) { Files = ["data/list.dat"] }),
            NeedsAttention = true,
        };
        using var ui = Open(State(game), game);
        var page = Page(ui);

        page.Step!.Kind.Should().Be(StepKind.Failed);
        page.Step.Text.Should().Be("Pliki zachowane po wcześniejszym odinstalowaniu wciąż mają zawartość zostawioną przez jego oprogramowanie, "
            + "więc najpierw trzeba sprawdzić spójność plików gry w Steam.");
        page.Step.Primary.Should().NotBeNull("the install can be tried again once the files were checked");
        var check = ui.Find<Button>("CheckFilesUnder");
        check.IsEffectivelyVisible.Should().BeTrue("the step's own button stands beside it");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("kept-files-pending");
        ui.Click(check);
        ui.Links.Opened.Should().Equal(new Uri("steam://validate/4100"));
    }

    [AvaloniaFact]
    public async Task An_uninstall_whose_folder_was_gone_says_it_put_no_files_back_whether_its_end_or_the_state_comes_first()
    {
        const string forgotten = "Folderu tej instalacji już nie ma, więc nie przywrócono żadnych plików – usunięto tylko zapis instalacji "
            + "i jej kopie zapasowe.";
        var installed = Installed("Ember Hollow", "4100", offer: Ember);
        var available = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(State(installed), installed);
        var page = Page(ui);
        var first = Job(installed, JobKind.Uninstall, JobStage.Uninstalling);

        ui.Push(State(Busy(installed, first)) with { Version = 5 });
        ui.Agent.End(new JobEnded(first, JobOutcome.Succeeded) { Forgotten = true });
        ui.Push(State(available) with { Version = 6 });

        page.Step!.Kind.Should().Be(StepKind.Removed);
        page.Step.Text.Should().Be(forgotten, "the store still reports the game, in another folder");
        page.Step.Tone.Should().Be(Tone.Neutral, "no files were put back");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("removed-5-forgotten");

        var second = first with { Id = Guid.NewGuid() };
        ui.Push(State(Busy(installed, second)) with { Version = 7 });
        ui.Push(State(available) with { Version = 8 });
        ui.Agent.End(new JobEnded(second, JobOutcome.Succeeded) { Forgotten = true });
        page.Step!.Text.Should().Be(forgotten, "the agent may tell that the folder was gone after the state ended the job");
    }

    [AvaloniaFact]
    public async Task Updates_repairs_and_failures_offer_what_sees_to_them()
    {
        var update = UpdateAvailable("Ember Hollow", "4100");
        using var ui = Open(State(update), update, update.Installs[0].Project);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Details is not null);
        page.Step!.Kind.Should().Be(StepKind.Update);
        page.Step.PrimaryCaption.Should().Be("Do wersji 2 · 3,6\u00A0MB");
        ui.Click(ui.Find<Button>("ChangesToggle"));
        page.AreChangesOpen.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-update");
        ui.Click(ui.Find<Button>("PrimaryButton"));
        ui.Agent.Requests.OfType<UpdateProject>().Should().Equal(new UpdateProject(update.Installs[0].Id));

        ui.Push(State(Busy(update, Job(update, JobKind.Update, JobStage.Installing, fraction: 0.9))));
        ui.Push(State(Installed("Ember Hollow", "4100")) with { Version = 15 });
        page.Step!.Kind.Should().Be(StepKind.Done);
        page.Step.Title.Should().Be("Zaktualizowano spolszczenie");
        page.Step.PointsToNotes.Should().BeTrue("the team's notes are worth a look after an update");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-done");
        ui.Click(ui.Find<Button>().First(button => button.GetValue(ToolTip.TipProperty) is "Zamknij komunikat"));
        page.Step.Kind.Should().Be(StepKind.Current);

        ui.Push(State(update).Offline());
        page.Step.Kind.Should().Be(StepKind.Offline);
        page.Step.PrimaryCaption.Should().Be("Do wersji 2 · 3,6\u00A0MB · ruszy po połączeniu");

        var repair = NeedsRepair("Ember Hollow", "4100");
        ui.Push(State(repair) with { Version = 20 });
        page.Step.Kind.Should().Be(StepKind.Repair);
        page.Step.PrimaryRequest.Should().Be(new RepairProject(repair.Installs[0].Id));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-repair");

        var failed = FailedUpdate("Ember Hollow", "4100");
        ui.Push(State(failed) with { Version = 21 });
        page.Step.Kind.Should().Be(StepKind.Failed);
        page.Step.Text.Should().Be("Za mało miejsca na dysku D: (potrzeba 1,6\u00A0GB, wolne 921,6\u00A0MB). Wersja 1 została bez zmian.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-failed");
        ui.Click(ui.Find<Button>().First(button => button.GetValue(ToolTip.TipProperty) is "Zamknij komunikat"));
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<DismissFailure>().Any());
        ui.Agent.Requests.OfType<DismissFailure>().Should().Equal(new DismissFailure(failed.Key, failed.Installs[0].Id));
    }

    [AvaloniaFact]
    public void An_update_refused_for_files_the_uninstall_would_leave_says_to_uninstall_first()
    {
        var failed = FailedUpdate("Ember Hollow", "4100");
        failed = failed with
        {
            Installs =
            [
                failed.Installs[0] with
                {
                    LastFailure = new FailureView(JobKind.Update, JobOrigin.Automatic, TestStates.Now.AddMinutes(-3),
                        new JobFailureView(JobFailureReason.KeptFilesBlockReplacement) { Files = ["data/list.dat"] }),
                },
            ],
        };
        using var ui = Open(State(failed), failed);
        var page = Page(ui);

        page.Step!.Kind.Should().Be(StepKind.Failed);
        (page.Step.HasPrimary, page.Step.PrimaryRequest).Should().Be((false, null), "trying again would be refused the same way");
        page.Step.Text.Should().Be("Odinstalowanie zainstalowanej wersji spolszczenia zostawiłoby pliki zmienione przez jego oprogramowanie tak, "
            + "jak są, a nowe paczki zapisują te same pliki, więc najpierw trzeba odinstalować spolszczenie, potem sprawdzić spójność plików "
            + "gry w Steam i zainstalować je ponownie. Wersja 1 została bez zmian.");
        page.Step.CheckFiles.Should().BeNull("checking the files first would only bring the refusal back");
    }

    [AvaloniaFact]
    public async Task Several_teams_wait_for_the_players_choice_without_a_red_button()
    {
        var game = Choice("Saltmarsh: Low Tide", "5400");
        using var ui = Open(State(game), game);
        var page = Page(ui);

        page.Step!.Kind.Should().Be(StepKind.Choose);
        page.Step.HasPrimary.Should().BeFalse();
        page.Choices.Select(choice => choice.Team).Should().Equal("Lantern Team", "Night Owls");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-choice", height: 1100);

        ui.Click(ui.Find<Button>().Single(button => button.DataContext is ProjectChoice { Team: "Night Owls" }));
        ui.Agent.Requests.OfType<ChooseProject>().Should().Equal(new ChooseProject(game.Key, game.Projects[1].ProjectId));
    }

    [AvaloniaFact]
    public void Every_install_of_a_game_has_its_row_and_the_one_project_rule_hides_the_others()
    {
        var first = Offer("Ember Hollow", Lanterns);
        var second = Offer("Ember Hollow", Owls, status: "Wczesny dostęp", color: EarlyAccess);
        var installed = new[]
        {
            Install(first, 2, new GameStatusView(MyGameState.Installed)),
            Install(second, 1, new GameStatusView(MyGameState.UpdateAvailable) { Update = Core.Catalog.UpdateState.Available, UpdateSize = 3_774_874 }),
        };
        var generic = Available("Ember Hollow", "4100", first) with
        {
            Projects = [first, second, Offer("Ember Hollow", Cranes)],
            Installs = installed,
            Status = new GameStatusView(MyGameState.UpdateAvailable),
        };
        using var ui = Open(State(generic), generic);
        var page = Page(ui);

        page.Installs.Select(row => row.Project).Should().Equal("Ember Hollow · Lantern Team", "Ember Hollow · Night Owls");
        page.Step!.Kind.Should().Be(StepKind.Update, "the install that needs the player leads the page");
        page.ShowsChoices.Should().BeTrue("another team's project can join the game's installs");
        ui.Click(ui.Find<Button>().Single(button => button.DataContext is ProjectChoice { Team: "Paper Cranes" }));
        ui.Agent.Requests.OfType<InstallProject>().Should().ContainSingle().Which.ProjectId.Should().NotBe(first.ProjectId);

        ui.Push(State(generic with { InstallReplaces = true, Installs = [installed[0]], Status = new GameStatusView(MyGameState.Installed) }));
        page.ShowsChoices.Should().BeFalse("an app with one project per game offers no second one beside the installed");
        page.Installs.Should().ContainSingle().Which.HasProject.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task An_install_names_the_software_its_packages_carry_and_unfolds_their_licences_and_sources()
    {
        var plain = Installed("Ember Hollow", "4100", offer: Ember);
        var game = plain with
        {
            Installs =
            [
                plain.Installs[0] with
                {
                    ThirdPartyNotices =
                    [
                        new ThirdPartyNoticeView("Lantern Streamer", "GPL-3.0-or-later", "https://code.example.com/lantern-streamer"),
                        new ThirdPartyNoticeView("Example Loader", "MIT", "http://loader.example.com/source"),
                    ],
                },
            ],
        };
        using var ui = Open(State(game), game);
        var page = Page(ui);
        var licenses = page.Installs.Single().Licenses!;

        licenses.Line.Should().Be(Text.Format(Strings.GamePage_SoftwareLine, ("names", "Lantern Streamer, Example Loader")));
        ui.Find<TextBlock>().Should().Contain(text => text.IsEffectivelyVisible && text.Text == licenses.Line);
        ui.Find<TextBlock>().Should().NotContain(text => text.IsEffectivelyVisible && text.Text == "GPL-3.0-or-later",
            "the licences stay folded until the player asks for them");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-licences-folded");

        ui.Click(ui.Find<Button>("LicensesToggle"));
        licenses.Toggle.Should().Be(Text.Format(Strings.GamePage_LicensesHide, ("count", 2)));
        ui.Find<TextBlock>().Should().Contain(text => text.IsEffectivelyVisible && text.Text == "GPL-3.0-or-later");
        ui.Find<TextBlock>().Should().Contain(text => text.IsEffectivelyVisible && text.Text == "http://loader.example.com/source",
            "the window opens only https addresses, so the other one shows as text");
        ui.Unnamed().Should().BeEmpty();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-licences");
        ui.Click(ui.Find<Button>().Single(button => button.IsEffectivelyVisible
            && AutomationProperties.GetName(button) == Text.Format(Strings.GamePage_LicenseSourceName, ("name", "Lantern Streamer"))));
        ui.Links.Opened.Should().Equal(new Uri("https://code.example.com/lantern-streamer"));

        ui.Push(State(game) with { Version = 2 });
        page.Installs.Single().Licenses.Should().BeSameAs(licenses, "a state that changes nothing of the software keeps the list unfolded");
        ui.Push(State(plain) with { Version = 3 });
        page.Installs.Single().Licenses.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task A_game_that_has_the_target_language_gets_a_notice_and_the_switch_sends_the_players_choice()
    {
        var official = Available("Ember Hollow", "4100", Ember) with { OffersTargetLanguage = true };
        using var ui = Open(State(official), official);
        var page = Page(ui);
        page.ShowsOfficial.Should().BeTrue();
        page.ShowsAutomation.Should().BeFalse();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-official");

        ui.Push(State(Installed("Ember Hollow", "4100", offer: Ember)));
        page.ShowsAutomation.Should().BeTrue("updates of a project installed by hand follow the switch as anywhere");
        page.AutomationHint.Should().Be("Tak jak w ustawieniach. Nowe wersje zainstalują się same, gdy gra będzie wyłączona.");
        ui.Click(ui.Find<ToggleSwitch>("AutomationSwitch"));
        ui.Agent.Requests.OfType<SetGameAutomation>().Should().Equal(new SetGameAutomation(official.Key, AutoUpdate: false));
    }

    [AvaloniaFact]
    public async Task An_install_without_its_game_or_project_offers_only_to_be_uninstalled()
    {
        var gone = Orphaned("Ember Hollow", "4100", OrphanReason.GameNotDetected);
        using var ui = Open(State(gone), gone);
        var page = Page(ui);
        page.Step!.Kind.Should().Be(StepKind.Gone);
        page.Installs.Single().Features.Should().BeNull("an install without its game has no line of add-ons");
        page.ShowsAutomation.Should().BeFalse();
        page.GameState.Should().Be("Steam nie zgłasza już tej gry");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-gone");

        var moved = Orphaned("Ember Hollow", "4100", OrphanReason.GameMoved);
        ui.Push(State(moved with { Detected = moved.Detected! with { InstallDirectory = @"E:\Games\Ember Hollow" } }));
        page.Step.Kind.Should().Be(StepKind.Moved);
        page.Step.Text.Should().Contain("na dysku E:");

        var removed = Orphaned("Ember Hollow", "4100", OrphanReason.NotInCatalog);
        removed = removed with { Projects = [], Project = null, Installs = [removed.Installs[0] with { Project = null }] };
        ui.Push(State(removed) with { Version = 30 });
        page.Step.Kind.Should().Be(StepKind.NotInCatalogue);
        page.ShowsSavedInfo.Should().BeTrue();
        page.SavedPackage.Should().Be("ember-hollow-v2.zip");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-not-in-catalogue");
        ui.Click(ui.Find<Button>("SecondaryButton"));
        ui.Shell.Dialogs.Current.Should().BeOfType<ConfirmDialogViewModel>();
    }

    [AvaloniaFact]
    public async Task The_project_page_fills_the_page_below_and_its_links_open_outside_the_window()
    {
        const string Welcome = "https://images.example.com/attachments/welcome.webp";
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game, Ember, images =>
        {
            images.Missing.Add("https://i.ytimg.com/vi/Tr4iLer-0_1/maxresdefault.jpg");
            images.Sizes[Welcome] = new PixelSize(480, 56);
            images.Sizes["https://images.example.com/attachments/lantern.avif"] = new PixelSize(616, 200);
            images.Animations["https://images.example.com/attachments/lantern.mp4"] = [Colors.Orange, Colors.Gold];
        });
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Details is not null);

        ui.Agent.Requests.OfType<GetProjectPage>().Should().ContainSingle()
            .Which.Should().Be(new GetProjectPage(Ember.GameSlug, Ember.Team.Slug, Ember.ProjectId));
        page.Summary.Should().StartWith("Latarnik wraca");
        page.Details!.Gallery.Should().HaveCount(3);
        page.Details.Credits.Select(credit => credit.Name).Should().Equal("Ada Lantern", "Olek Tide");
        page.Story.TeamFacts.Should().Be("Grupa · 3 osoby · 12 projektów");
        ui.Images.Backdrops.Should().Contain(backdrop => backdrop.Recipe.Artwork == $"https://images.example.com/backgrounds/{Ember.GameSlug}.png");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("game-full", height: 2200);
        ui.Unnamed().Should().BeEmpty("every control a player can reach has a name a screen reader reads");
        ui.Images.Requests.Select(request => request.Address).Should().Contain(
            ["https://i.ytimg.com/vi/Tr4iLer-0_1/maxresdefault.jpg", "https://i.ytimg.com/vi/Tr4iLer-0_1/hqdefault.jpg", Welcome,
                "https://images.example.com/attachments/lantern.avif"],
            "the site shows a film's still from YouTube, the smaller one when the largest is missing, and every picture of a "
            + "description, also one opening a paragraph and a looping clip's still");
        ui.Find<Controls.RemoteImage>().Single(image => image.Source == Welcome).Bounds.Size.Should().Be(new Size(480, 56),
            "a picture in a text keeps its own proportions and width");
        var clip = ui.Find<Controls.RemoteImage>().Single(image => image.ClipSource is not null);
        clip.BringIntoView();
        await UiHarness.WaitUntilAsync(() => clip.Player is { Frames: > 2 });
        ui.Images.Opened.Select(opened => opened.Address).Distinct().Should().Equal(["https://images.example.com/attachments/lantern.mp4"],
            "a store page's looping clip plays in its still's place, its first format first");

        ui.Click(ui.Find<Button>("StatusChip"));
        page.IsStatusOpen.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-status");
        ui.ClickAt(new Point(700, 700));
        page.IsStatusOpen.Should().BeFalse("a click beside the explanation closes it");

        var link = ui.Find<Controls.Markdown.ProseText>().First(text => text.Links.Count > 0);
        page.OpenCommand.Execute(link.Links[0]);
        ui.Links.Opened.Should().Equal(new Uri("https://example.com/ember-hollow"));
        page.Story.ToggleDescriptionCommand.Execute(null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("game-description", height: 2600);
        ui.Click(ui.Find<Button>().Single(button => button.Classes.Contains("film")));
        ui.Links.Opened[^1].Should().Be(new Uri("https://www.youtube.com/watch?v=W4lkThr0ugh&t=30s"));

        ui.Click(ui.Find<Button>().First(button => button.DataContext is GalleryTile { Item.IsVideo: true }));
        ui.Links.Opened[^1].Should().Be(new Uri("https://www.youtube.com/watch?v=Tr4iLer-0_1"));
        ui.Click(ui.Find<Button>().First(button => button.DataContext is GalleryTile { Item.Caption: "Przystań" }));
        var viewer = ui.Shell.Dialogs.Current.Should().BeOfType<GalleryViewerViewModel>().Subject;
        viewer.Counter.Should().Be("2 / 2");
        ui.Press(Key.Right);
        viewer.Counter.Should().Be("1 / 2");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-gallery");
    }

    [AvaloniaFact]
    public void The_game_is_offered_to_start_only_while_no_job_runs_on_it()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game);
        var page = Page(ui);
        page.CanLaunch.Should().BeTrue();

        ui.Push(State(Busy(game, Job(game, JobKind.Update, JobStage.Installing))) with { Version = 3 });
        page.CanLaunch.Should().BeFalse("the job would wait for the game to exit");
        ui.Find<Button>().Should().NotContain(button => Equals(button.Content, Strings.GamePage_Launch) && button.IsEffectivelyVisible);

        ui.Push(State(game) with { Version = 4 });
        page.CanLaunch.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Hiding_launching_opening_the_folder_and_the_site_send_their_commands()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game);
        var page = Page(ui);

        ui.Click(ui.Find<Button>("HideButton"));
        page.LaunchCommand.Execute(null);
        page.OpenFolderCommand.Execute(null);
        page.OpenCommand.Execute(page.SitePage);
        page.OpenCommand.Execute(page.StorePage);

        ui.Agent.Requests.Should().Contain(new HideGame(game.Key, true)).And.Contain(new LaunchGame(game.Key))
            .And.Contain(new OpenFolder(AppFolder.Game, game.Key));
        ui.Links.Opened.Should().Equal(new Uri("https://example.com/games/ember-hollow/lantern-team"), new Uri("steam://nav/games/details/4100"));

        ui.Push(State(game with { IsHidden = true, Preferences = game.Preferences with { IsHidden = true } }));
        page.HideText.Should().Be("Pokaż w „Moich grach”");
    }

    [AvaloniaFact]
    public async Task A_game_no_longer_listed_says_so_and_one_on_this_computer_says_it_waits_for_a_project()
    {
        using var ui = Open(State(), Available("Ember Hollow", "4100"));
        Page(ui).IsMissing.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-missing");
        ui.Find<Button>("OpenCatalogueGame").IsEffectivelyVisible.Should().BeFalse("a game gone has nothing in the catalogue to wait for");
        ui.Click(ui.Find<Button>().Single(button => button.IsEffectivelyVisible && button.Content is "Wróć do „Moich gier”"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());

        var meridian = SteamKey("4300");
        var found = new DetectedGameView { InstallDirectory = @"D:\Games\Iron Meridian", LibraryId = "library" };
        var detected = new DetectedGameEntry(meridian, "Iron Meridian", found)
        {
            Projects = [new SiteProjectView(Guid.NewGuid(), "iron-meridian", "lantern-team")],
        };
        var copper = SteamKey("4400");
        var otherStores = new DetectedGameEntry(copper, "Copper Vale", found with { InstallDirectory = @"D:\Games\Copper Vale" })
        {
            Projects = [new SiteProjectView(Guid.NewGuid(), "copper-vale", "lantern-team")],
            Unavailable = SelectionUnavailability.OtherStores,
        };
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([detected, otherStores]) : null;
        ui.Shell.Navigator.Navigate(new GameRoute(meridian));
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasCatalogueGame);
        (page.MissingTitle, page.MissingText).Should().Be(("Ta gra czeka na spolszczenie",
            "Gra jest na tym komputerze. Gdy grupa wyda spolszczenie, pojawi się w „Moich grach”."));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-waiting");
        ui.Click(ui.Find<Button>().Single(button => button.Name == "OpenCatalogueGame" && button.IsEffectivelyVisible));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueGameRoute("iron-meridian"));

        ui.Shell.Navigator.Navigate(new GameRoute(copper));
        var unfit = Page(ui);
        await UiHarness.WaitUntilAsync(() => unfit.HasCatalogueGame);
        (unfit.MissingTitle, unfit.MissingText).Should().Be(("Ta wersja gry czeka na spolszczenie",
            "Gra jest na tym komputerze, ale spolszczenie nie ma jeszcze pliku dla tej wersji gry."),
            "a completed project made only for other versions of the game is not on its way");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("game-waiting-file");
    }

    [AvaloniaFact]
    public async Task In_english_a_game_gone_and_a_game_that_waits_name_the_games_list_in_their_texts()
    {
        var waiting = new DetectedGameEntry(SteamKey("4300"), "Iron Meridian",
            new DetectedGameView { InstallDirectory = @"D:\Games\Iron Meridian", LibraryId = "library" })
        {
            Projects = [new SiteProjectView(Guid.NewGuid(), "iron-meridian", "lantern-team")],
        };
        Text.Use(TextCatalog.Base);
        try
        {
            using var ui = Open(State(), Available("Ember Hollow", "4100"));
            Page(ui).MissingText.Should()
                .Be("This game is not in “My games” now. It may have been uninstalled, or its store stopped reporting it.");

            ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([waiting]) : null;
            ui.Shell.Navigator.Navigate(new GameRoute(waiting.Key));
            var page = Page(ui);
            await UiHarness.WaitUntilAsync(() => page.HasCatalogueGame);

            (page.MissingTitle, page.MissingText).Should().Be(("This game is waiting for a project",
                "The game is on this computer. Once the team releases the project, it shows in “My games”."));
        }
        finally
        {
            Text.Use(TestApp.PolishApp());
        }
    }

    [AvaloniaFact]
    public async Task At_the_smallest_window_the_next_step_leads_the_panel_above_the_edge()
    {
        var game = UpdateAvailable("Ember Hollow", "4100");
        using var ui = new UiHarness(State(game), width: 960, height: 600);
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        var page = Page(ui);
        page.IsCompact.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("game-minimum");

        var button = ui.Find<Button>().Single(button => button.Name == "PrimaryButton" && button.IsEffectivelyVisible);
        button.TranslatePoint(new Point(0, button.Bounds.Height), ui.Window)!.Value.Y.Should().BeLessThan(600);
    }

    [AvaloniaFact]
    public void An_adult_game_unveiled_in_the_catalogue_shows_its_cover_on_its_page_and_its_card()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember) with { IsAdult = true, SiteIds = [SiteId] };
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        page.IsCoverBlurred.Should().BeTrue();

        ui.Push(State(game) with { Version = 2, RevealedAdultGames = [SiteId] });
        page.IsCoverBlurred.Should().BeFalse("the player unveiled the game's pictures on its page in the catalogue");
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        UiHarness.Settle();
        ((LibraryPageViewModel)ui.Shell.Navigator.Current!).GridItems.OfType<GameCardViewModel>().Single().IsCoverBlurred.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task An_adult_games_page_veils_its_gallery_and_the_artwork_behind_its_head_like_its_cover()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember) with { IsAdult = true, SiteIds = [SiteId] };
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Details is not null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("game-adult", height: 2200);

        page.IsCoverBlurred.Should().BeTrue();
        ui.Find<RemoteImage>().Where(image => image.DataContext is GalleryTile).Should().NotBeEmpty().And.OnlyContain(image => image.IsBlurred,
            "the gallery's pictures are veiled like the cover");
        ui.Images.Backdrops[^1].Recipe.Should().Match<BackdropRecipe>(recipe => recipe.IsBlurred && recipe.Artwork.Length > 0,
            "the project's artwork and banner are baked veiled while adult covers are");

        ui.Push(State(game) with { Version = 2, RevealedAdultGames = [SiteId] });
        UiHarness.Settle();
        ui.Find<RemoteImage>().Where(image => image.DataContext is GalleryTile).Should().OnlyContain(image => !image.IsBlurred);
        ui.Images.Backdrops[^1].Recipe.IsBlurred.Should().BeFalse("the player unveiled the game's pictures");
    }

    private static UiHarness Open(AppStateView state, GameView game, ProjectOfferView? page = null, Action<FakeImages>? images = null)
    {
        var ui = new UiHarness(state);
        if (page is not null)
        {
            ui.Agent.Answer = TestPages.Answer(page);
        }

        images?.Invoke(ui.Images);
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        return ui;
    }

    private static GamePageViewModel Page(UiHarness ui) => (GamePageViewModel)ui.Shell.Navigator.Current!;

    private static GameView WithRuntimeFiles(GameView game) =>
        game with { Installs = [game.Installs[0] with { RuntimeFiles = ["data/videos/list.dat", "data/videos/list_original.dat"] }] };
}
