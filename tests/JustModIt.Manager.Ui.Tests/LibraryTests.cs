using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class LibraryTests
{
    private static readonly GameView Failed = FailedUpdate("Glass Orchard", "5100");
    private static readonly GameView Repair = NeedsRepair("Saltmarsh: Lantern Wake", "5200");
    private static readonly GameView Update = UpdateAvailable("Saltmarsh: Iron Tide", "5300");
    private static readonly GameView Teams = Choice("Saltmarsh: Low Tide", "5400");
    private static readonly GameView Installing = Busy(Available("Vesper Lake", "5500"),
        Job(Available("Vesper Lake", "5500"), JobKind.Install, JobStage.Installing));
    private static readonly GameView Current = Installed("Iron Meridian", "5600", version: 1);
    private static readonly GameView StoreUpdating = NotReady("Saltmarsh: Hollow Crown", "5700", GameInstallState.Updating);
    private static readonly GameView Gone = Orphaned("Quiet Harbor", "5800", OrphanReason.GameNotDetected);

    private static AppStateView Library(params GameView[] more) =>
        State([Failed, Repair, Update, Teams, Installing, Current, StoreUpdating, .. more]);

    [AvaloniaFact]
    public async Task The_panel_puts_what_needs_the_player_first_and_its_button_starts_everything_that_can_start_now()
    {
        using var ui = new UiHarness(Library());
        var page = Page(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("library");

        page.TodoRows.Select(row => (row.Name, row.Kind)).Should().Equal(
            (Failed.Name, TodoKind.Failure), (Repair.Name, TodoKind.Repair), (Update.Name, TodoKind.Update), (Teams.Name, TodoKind.Choice));
        page.TodoRows[0].Line.Detail.Should().Be("Za mało miejsca na dysku D: (potrzeba 1,6 GB, wolne 921,6 MB).");
        page.GridItems.OfType<GameCardViewModel>().Select(card => card.Name).Should().Equal(Current.Name, StoreUpdating.Name, Installing.Name);
        page.BatchText.Should().Be("Napraw i zaktualizuj");
        page.BatchCount.Should().Be(2);
        ui.Find<Button>("BatchButton").GetValue(ToolTip.TipProperty).Should()
            .Be($"{Repair.Name}: naprawa{Environment.NewLine}{Update.Name}: aktualizacja do v2");
        ui.Unnamed().Should().BeEmpty("every control a player can reach has a name a screen reader reads");

        ui.Click(ui.Find<Button>("BatchButton"));
        ui.Agent.Requests.Should().Equal(new RepairProject(Repair.Installs[0].Id), new UpdateProject(Update.Installs[0].Id));

        ui.Agent.Requests.Clear();
        ui.Click(RowAction(ui, Failed.Name));
        ui.Agent.Requests.Should().Equal(new UpdateProject(Failed.Installs[0].Id));
        ui.Click(RowAction(ui, Teams.Name));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(Teams.Key), "choosing among teams happens on the game's page");

        ui.Shell.Navigator.GoBackCommand.Execute(null);
        ui.Push(Library().Offline());
        page.BatchText.Should().Be("Napraw", "an update has to wait for the connection, a repair runs from the saved package");
        page.TodoRows.Single(row => row.Kind == TodoKind.Update).Line.Warning.Should().Be("Ruszy po połączeniu");
        page.OfflineTitle.Should().Be("Brak połączenia z Bazą Spolszczeń");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("library-offline");
    }

    [AvaloniaFact]
    public void An_update_row_tells_what_the_update_downloads()
    {
        var update = Update with
        {
            Installs = [Update.Installs[0] with { Status = Update.Installs[0].Status with { UpdateSize = 46_871_347 } }],
        };
        using var ui = new UiHarness(State(update));

        Page(ui).TodoRows.Single().Line.Detail.Should().EndWith(Formats.FileSize(46_871_347), "an update downloads only what the install lacks");
    }

    [AvaloniaFact]
    public async Task Removing_an_install_asks_first_and_a_long_panel_unfolds_in_place()
    {
        var more = Enumerable.Range(0, 4).Select(index => NeedsRepair($"Ashfall {index + 1}", $"590{index}")).ToArray();
        using var ui = new UiHarness(Library([Gone, .. more]));
        var page = Page(ui);

        page.TodoRows.Should().HaveCount(LibraryPageViewModel.CollapsedTodoRows);
        page.BatchCount.Should().Be(6, "the button counts the rows the folded panel hides too");
        ui.Click(ui.Find<Button>("TodoToggle"));
        page.TodoRows.Should().HaveCount(9);
        page.TodoRows[^1].Kind.Should().Be(TodoKind.Gone);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("library-todo-expanded", height: 1400);

        ui.Click(RowAction(ui, Gone.Name));
        var dialog = ui.Shell.Dialogs.Current.Should().BeOfType<ConfirmDialogViewModel>().Subject;
        dialog.IsDanger.Should().BeTrue();
        dialog.Cancel();
        await UiHarness.WaitUntilAsync(() => !ui.Shell.Dialogs.IsOpen);
        ui.Agent.Requests.Should().BeEmpty();

        ui.Click(RowAction(ui, Gone.Name));
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).ConfirmCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.Count == 1);
        ui.Agent.Requests.Should().Equal(new UninstallProject(Gone.Installs[0].Id));
    }

    [AvaloniaFact]
    public void A_replacement_refused_for_files_its_uninstall_would_leave_leads_to_the_game_rather_than_trying_again()
    {
        var refused = Failed with
        {
            Installs =
            [
                Failed.Installs[0] with
                {
                    LastFailure = Failed.Installs[0].LastFailure! with
                    {
                        Failure = new JobFailureView(JobFailureReason.KeptFilesBlockReplacement) { Files = ["data/list.dat"] },
                    },
                },
            ],
        };
        using var ui = new UiHarness(State(refused));

        var row = Page(ui).TodoRows.Single();
        (row.Kind, row.ActionText, row.Request).Should().Be((TodoKind.Failure, "Otwórz", null));
        ui.Click(RowAction(ui, refused.Name));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(refused.Key), "the game's page says what to do first");
        ui.Agent.Requests.Should().NotContain(request => request is UpdateProject, "trying again would be refused the same way");
    }

    [AvaloniaFact]
    public void A_failure_nothing_could_try_again_from_the_list_leads_to_the_game_by_name()
    {
        var withdrawn = TestGames.Available("Ember Hollow", "4100") with
        {
            Project = null,
            LastFailure = new FailureView(JobKind.Install, JobOrigin.User, TestStates.Now.AddMinutes(-5),
                new JobFailureView(JobFailureReason.ReleaseUnavailable)),
            NeedsAttention = true,
        };
        using var ui = new UiHarness(State(withdrawn));

        var row = Page(ui).TodoRows.Single();
        (row.Kind, row.ActionText, row.Request).Should().Be((TodoKind.Failure, "Otwórz", null));
        ui.Render("library-todo-open");
        ui.Click(RowAction(ui, withdrawn.Name));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(withdrawn.Key));
    }

    [AvaloniaFact]
    public async Task Search_and_filters_narrow_the_panel_and_the_grid_and_lead_back_when_nothing_is_left()
    {
        using var ui = new UiHarness(Library());
        var page = Page(ui);

        page.Query = "saltmarsh";
        page.TodoRows.Select(row => row.Name).Should().Equal(Repair.Name, Update.Name, Teams.Name);
        page.GridItems.OfType<GameCardViewModel>().Select(card => card.Name).Should().Equal(StoreUpdating.Name);
        page.AllCount.Should().Be(1);

        page.ShowsAvailable = true;
        page.Content.Should().Be(LibraryContent.FilteredOut);
        page.EmptyMessage.Should().Be("Wśród gier pasujących do „saltmarsh” nie ma spolszczeń do zainstalowania.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("library-filtered-out");
        ui.Click(ui.Find<Button>("EmptyAction"));
        page.Filter.Should().Be(LibraryFilter.All);
        page.Query.Should().BeEmpty();

        page.Query = "zolw";
        page.Content.Should().Be(LibraryContent.NoResults);
        page.EmptyMessage.Should().Be("Żadna gra nie pasuje do „zolw”.");
        ui.Press(Key.F, RawInputModifiers.Control);
        ui.Window.FocusManager!.GetFocusedElement().Should().BeSameAs(ui.Find<TextBox>("SearchBox"));
        ui.Press(Key.Escape);
        page.Query.Should().BeEmpty();
        page.Content.Should().Be(LibraryContent.Games);

        page.Query = "ORCHARD";
        page.TodoRows.Should().ContainSingle().Which.Name.Should().Be(Failed.Name, "the search ignores case and marks");
    }

    [AvaloniaFact]
    public async Task Hidden_games_stay_out_of_the_panel_and_the_grid_until_their_filter_is_chosen()
    {
        var hidden = Teams with { Preferences = Teams.Preferences with { IsHidden = true }, IsHidden = true };
        using var ui = new UiHarness(State(Current, hidden, Repair));
        var page = Page(ui);

        page.TodoRows.Select(row => row.Name).Should().Equal(Repair.Name);
        page.GridItems.OfType<GameCardViewModel>().Select(card => card.Name).Should().Equal(Current.Name);
        page.ShowsHiddenFilter.Should().BeTrue();
        page.HiddenCount.Should().Be(1);

        ui.Click(ui.Find<RadioButton>("FilterHidden"));
        var card = page.GridItems.OfType<GameCardViewModel>().Should().ContainSingle().Subject;
        card.Line.Label.Should().Be("Ukryta");
        card.TeamName.Should().Be("2 grupy");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("library-hidden");

        ui.Push(State(hidden with { Preferences = Teams.Preferences, IsHidden = false }, Current));
        page.Filter.Should().Be(LibraryFilter.All, "the filter goes once nothing is hidden");
        page.ShowsHiddenFilter.Should().BeFalse();

        ui.Push(State(hidden));
        page.Content.Should().Be(LibraryContent.AllHidden);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("library-all-hidden");
    }

    [AvaloniaFact]
    public async Task Progress_moves_a_card_on_without_replacing_it_or_its_view()
    {
        using var ui = new UiHarness(Library());
        var page = Page(ui);
        var card = page.GridItems.OfType<GameCardViewModel>().Single(card => card.Key == Installing.Key);
        var view = ui.Find<Button>().Single(button => button.DataContext == card);

        ui.Agent.Progress(Job(Installing, JobKind.Install, JobStage.Installing, fraction: 0.81) with { Sequence = 2 });
        UiHarness.Settle();

        card.Line.Label.Should().Be("Instalowanie · 81%");
        card.Progress.Should().BeApproximately(81, 0.01);
        ui.Find<Button>().Single(button => button.DataContext == card).Should().BeSameAs(view);

        ui.Push(Library() with { Version = 9 });
        page.GridItems.OfType<GameCardViewModel>().Should().Contain(card, "a new state updates the card it had");
        ui.Find<Button>().Single(button => button.DataContext == card).Should().BeSameAs(view);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void The_grid_keeps_its_last_column_inside_the_page_and_the_keyboard_moves_through_it()
    {
        var games = Enumerable.Range(0, 8).Select(index => Installed($"Lantern Coast {index + 1}", $"610{index}")).ToArray();
        using var ui = new UiHarness(State(games));
        var page = Page(ui);
        page.Columns.Should().Be(5);
        var grid = ui.Find<ItemsControl>("GameGrid");
        var right = grid.TranslatePoint(new Point(grid.Bounds.Width, 0), ui.Window)!.Value.X;
        ui.Find<Button>().Where(button => button.DataContext is GameCardViewModel).Should().OnlyContain(
            card => card.TranslatePoint(new Point(card.Bounds.Width, 0), ui.Window)!.Value.X <= right, "the last column keeps its border");
        var first = ui.Find<Button>().First(button => button.DataContext is GameCardViewModel);
        first.Focus(NavigationMethod.Tab);

        ui.Press(Key.Right);
        FocusedCard(ui).Name.Should().Be("Lantern Coast 2");
        ui.Press(Key.Down);
        FocusedCard(ui).Name.Should().Be("Lantern Coast 7");
        ui.Press(Key.Left);
        FocusedCard(ui).Name.Should().Be("Lantern Coast 6");
        ui.Press(Key.Up);
        FocusedCard(ui).Name.Should().Be("Lantern Coast 1");

        ui.Press(Key.Enter);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(games[0].Key));
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public async Task At_any_display_scale_the_cards_end_on_whole_pixels_inside_the_grid_and_draw_their_borders_alike(double scaling)
    {
        var games = Enumerable.Range(0, 5).Select(index => Installed($"Lantern Coast {index + 1}", $"610{index}")).ToArray();
        using var ui = new UiHarness(State(games));
        ui.Window.SetRenderScaling(scaling);
        // Widths whose columns come out whole, and ones whose columns fall between pixels.
        foreach (var width in new[] { 1280.0, 1281, 1282, 1283, 1284 })
        {
            ui.Window.Width = width;
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(700));
            var grid = ui.Find<ItemsControl>("GameGrid");
            var right = grid.TranslatePoint(new Point(grid.Bounds.Width, 0), ui.Window)!.Value.X * scaling;
            var cards = ui.Find<Button>().Where(button => button.DataContext is GameCardViewModel)
                .Select(card => new Rect(card.TranslatePoint(default, ui.Window)!.Value, card.Bounds.Size) * scaling)
                .OrderBy(card => card.X)
                .ToList();

            cards.Should().HaveCount(5).And.OnlyContain(card => IsWhole(card.Left) && IsWhole(card.Right), $"cells start and end on pixels at {width} wide");
            Math.Round(cards[^1].Right).Should().BeLessThanOrEqualTo(Math.Round(right), "the last column keeps its border inside the grid");
            using var frame = ui.Window.CaptureRenderedFrame()!;
            var edges = cards.Select(card => Edges(frame, card.Left, card.Right, card.Bottom - (24 * scaling))).ToList();
            edges.Should().AllBeEquivalentTo(edges[0], "every card's border is drawn on the same pixels as the first one's");
        }

        ui.Render(FormattableString.Invariant($"library-grid-{scaling:0.00}"));
    }

    [AvaloniaFact]
    public async Task At_the_smallest_window_the_panel_keeps_its_button_and_rows_above_the_edge()
    {
        using var ui = new UiHarness(Library(), width: 960, height: 600);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("library-minimum");

        Page(ui).Columns.Should().Be(3);
        foreach (var control in new Control[] { ui.Find<Button>("BatchButton"), RowAction(ui, Update.Name), ui.Find<Button>("CheckNowButton") })
        {
            var bottom = control.TranslatePoint(new Point(0, control.Bounds.Height), ui.Window)!.Value;
            bottom.Y.Should().BeLessThan(600);
            bottom.X.Should().BeLessThan(960);
        }
    }

    [AvaloniaFact]
    public async Task Until_the_stores_and_the_site_were_read_the_page_never_says_there_are_no_games()
    {
        var state = State() with { Library = new LibraryView(), Catalogue = new CatalogueView() };
        using var ui = new UiHarness(state);
        var page = Page(ui);
        page.Content.Should().Be(LibraryContent.Loading);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("library-loading");

        ui.Push(State() with { Version = 5 });
        page.Content.Should().Be(LibraryContent.Empty);
        ui.Find<Button>("HelpRow").IsEffectivelyVisible.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("library-empty");

        ui.Click(ui.Find<Button>("HelpRow"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new UndetectedGamesRoute());
        ui.Shell.Navigator.GoBackCommand.Execute(null);

        ui.Push(State() with
        {
            Version = 6,
            Catalogue = new CatalogueView { State = Core.Catalog.CatalogueState.Failed, Failure = Core.Catalog.CatalogueErrorReason.Offline },
        });
        page.Content.Should().Be(LibraryContent.FoundOffline);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("library-found-offline");
    }

    [AvaloniaFact]
    public async Task Every_card_says_in_one_line_what_its_game_is_doing()
    {
        var store = NotReady("Saltmarsh: Hollow Crown", "7100", GameInstallState.Updating);
        var drive = NotReady("Saltmarsh: Low Tide", "7900", GameInstallState.Offline, GameStateReason.LibraryOffline);
        var waiting = Busy(Installed("Glass Orchard", "7300", version: 1),
            Job(Installed("Glass Orchard", "7300"), JobKind.Update, JobStage.Waiting, null, JobWaitReason.GameRunning));
        var queued = Busy(Installed("Saltmarsh: Lantern Wake", "7400"), Job(Installed("Saltmarsh: Lantern Wake", "7400"), JobKind.Repair, JobStage.Queued, null));
        var downloading = Busy(Installed("Saltmarsh: Iron Tide", "7500"), Job(Installed("Saltmarsh: Iron Tide", "7500"), JobKind.Update, JobStage.Downloading, 0.18));
        var removing = Busy(Installed("Iron Meridian", "7600"), Job(Installed("Iron Meridian", "7600"), JobKind.Uninstall, JobStage.Uninstalling, null));
        var adult = Installed("Vesper Lake", "7700", version: 1) with { IsAdult = true };
        var bare = Available("Lantern Coast", "7800", Offer("Lantern Coast") with { Cover = "" });
        using var ui = new UiHarness(State(store, drive, waiting, queued, downloading, removing, adult, bare, Current), height: 1500);
        var page = Page(ui);

        string Line(GameView game)
        {
            var line = page.GridItems.OfType<GameCardViewModel>().Single(card => card.Key == game.Key).Line;
            return string.Join(" / ", new[] { line.Label, line.Detail }.Where(part => part is not null));
        }

        Line(store).Should().Be("Gra nie jest gotowa / Steam aktualizuje grę");
        Line(drive).Should().Be("Gra nie jest gotowa / Dysk z grą jest niedostępny");
        Line(waiting).Should().Be("Czeka na zamknięcie gry / Potem aktualizacja spolszczenia");
        Line(queued).Should().Be("W kolejce");
        Line(downloading).Should().Be("Pobieranie · 18%");
        Line(removing).Should().Be("Odinstalowywanie…");
        Line(adult).Should().Be("Zainstalowane · v1");
        Line(bare).Should().Be("Gotowe do instalacji");
        ui.Images.Blurred.Should().ContainSingle().Which.Should().Be(adult.Project!.Cover, "an adult cover is veiled while the setting asks for it");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(700));
        ui.Render("library-cards");
    }

    private static LibraryPageViewModel Page(UiHarness ui) => (LibraryPageViewModel)ui.Shell.Navigator.Current!;

    private static bool IsWhole(double pixels) => Math.Abs(pixels - Math.Round(pixels)) < 0.001;

    // The pixels across a card's left and right edges on one row, with two of the page and two of the card on each side.
    private static uint[] Edges(WriteableBitmap frame, double left, double right, double row)
    {
        using var pixels = frame.Lock();
        var line = new uint[pixels.Size.Width];
        var bytes = new byte[line.Length * 4];
        System.Runtime.InteropServices.Marshal.Copy(pixels.Address + ((int)Math.Round(row) * pixels.RowBytes), bytes, 0, bytes.Length);
        Buffer.BlockCopy(bytes, 0, line, 0, bytes.Length);
        var (start, end) = ((int)Math.Round(left), (int)Math.Round(right));
        return [.. line[(start - 2)..(start + 3)], .. line[(end - 3)..(end + 2)]];
    }

    private static Button RowAction(UiHarness ui, string game) =>
        ui.Find<Button>().First(button => button.Classes.Contains("rowAction") && button.IsVisible
            && button.DataContext is TodoRowViewModel row && row.Name == game);

    private static GameCardViewModel FocusedCard(UiHarness ui) =>
        (GameCardViewModel)((Control)ui.Window.FocusManager!.GetFocusedElement()!).DataContext!;
}
