using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Catalogue;
using JustModIt.Manager.Ui.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestCatalogue;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CatalogueTests
{
    private static readonly AppStateView Fresh = TestGames.State(TestGames.Installed("Ember Hollow", "4100"));

    [AvaloniaFact]
    public async Task The_catalogue_lists_every_game_as_the_site_does_and_counts_what_each_filter_leaves()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(700));
        ui.Render("catalogue");

        Cards(page).Select(card => card.Name).Should().Equal("Ember Hollow", "Glass Orchard", "Saltmarsh: Iron Tide", "Saltmarsh: Low Tide",
            "Hollow Crown", "Lantern Coast", "Iron Meridian", "Vesper Lake", "Quiet Harbor");
        page.AllCount.Should().Be(9);
        page.StatusOptions.Select(option => (option.Name, option.Count)).Should().Equal(
            ("Ukończone", 6), ("Korekta", 2), ("W trakcie tłumaczenia", 1), ("Zapowiedziane", 1));
        page.TeamOptions.Select(option => (option.Name, option.Count)).Should().Equal(
            ("Lantern Team", 5), ("Night Owls", 2), ("Paper Cranes", 2), ("Moth Works", 1));
        page.DetectedCount.Should().Be(2);
        page.FreshnessText.Should().Be("Zaktualizowano 2 min temu");
        ui.Unnamed().Should().BeEmpty("every control a player can reach has a name a screen reader reads");

        ui.Click(ButtonOf(ui, "Glass Orchard"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        UiHarness.Settle();
        ui.Click(ButtonOf(ui, "Saltmarsh: Low Tide"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueGameRoute("saltmarsh--low-tide"), "a game of several projects opens their comparison");
    }

    [AvaloniaFact]
    public async Task Searching_marks_what_matched_counts_within_the_results_and_escape_brings_everything_back()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);

        Type(ui, "SALTMARSH lów");
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 1);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-search");

        Cards(page).Single().Name.Should().Be("Saltmarsh: Low Tide");
        ui.Agent.Requests.OfType<GetCataloguePage>().Last().Query.Text.Should().Be("SALTMARSH lów");
        page.StatusOptions.Select(option => (option.Name, option.Count)).Should().Equal(("Ukończone", 1), ("Korekta", 1));
        page.TeamOptions.Select(option => option.Name).Should().Equal("Lantern Team", "Paper Cranes");
        var marked = ui.Find<MarkedText>().First(text => text.Value == "Saltmarsh: Low Tide");
        marked.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Where(run => run.Background is not null).Select(run => run.Text)
            .Should().Equal(["Saltmarsh", "Low"], "the words matched are marked, ignoring case and diacritics");

        ui.Press(Key.Escape);
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 9);
        page.Query.Should().BeEmpty();
        page.HasFilters.Should().BeFalse("the search alone is no filter to clear");
    }

    [AvaloniaFact]
    public async Task Status_and_team_narrow_together_and_clearing_them_keeps_the_search_and_order()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        ui.Click(ui.Find<RadioButton>().Single(button => button.Content is "A–Z" && button.DataContext is CataloguePageViewModel));
        await UiHarness.WaitUntilAsync(() => Cards(page).First().Name == "Ember Hollow" && Cards(page).Last().Name == "Vesper Lake");

        ui.Click(Pill(ui, "Ukończone"));
        await UiHarness.WaitUntilAsync(() => page.AllCount == 9 && Cards(page).Count() == 6);
        ui.Click(Pill(ui, "Night Owls"));
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 2);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-filters");

        Cards(page).Select(card => card.Name).Should().Equal("Glass Orchard", "Saltmarsh: Iron Tide");
        page.AllCount.Should().Be(2, "the statuses count with the team chosen");
        page.TeamOptions.Single(option => option.Name == "Lantern Team").Count.Should().Be(3, "the teams count with the status chosen");
        ui.Find<Button>("ClearInCard").IsEffectivelyVisible.Should().BeTrue();

        ui.Click(ui.Find<Button>("ClearInCard"));
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 9);
        var last = ui.Agent.Requests.OfType<GetCataloguePage>().Last();
        (last.Query.StatusUuid, last.Query.TeamUuid, last.Query.Order).Should().Be((null, null, CatalogueOrder.Alphabetical));
        page.HasFilters.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Clearing_the_filters_stands_beside_more_filters_as_its_pair()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        ui.Click(Pill(ui, "Ukończone"));
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 6);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-filter-actions");

        var clear = ui.Find<Button>("ClearInCard");
        var more = ui.Find<Button>("MoreToggle");
        var (clearText, moreText) = (Label(clear), Label(more));
        (clear.Bounds.Height, clear.Foreground, clearText.FontSize, clearText.FontWeight)
            .Should().Be((more.Bounds.Height, more.Foreground, moreText.FontSize, moreText.FontWeight));
        Baseline(ui, clearText).Should().BeApproximately(Baseline(ui, moreText), 0.5, "their words stand on one line");
        Lead(clear).Bounds.Size.Should().Be(Lead(more).Bounds.Size, "each leads with an icon of the same size");

        static TextBlock Label(Button button) => button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible);
        static Icon Lead(Button button) => button.GetVisualDescendants().OfType<Icon>().Single(icon => icon.IsEffectivelyVisible);
        static double Baseline(UiHarness ui, TextBlock text) =>
            text.TranslatePoint(new Point(0, text.TextLayout.TextLines[0].Baseline), ui.Window)!.Value.Y;
    }

    [AvaloniaFact]
    public async Task An_empty_result_says_whether_the_name_or_the_filters_are_to_blame_and_offers_the_way_back()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);

        Type(ui, "sunken lagoon");
        await UiHarness.WaitUntilAsync(() => page.Content == CatalogueContent.NoResults);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-no-results");
        page.EmptyTitle.Should().Be("Nic nie pasuje do „sunken lagoon”");
        page.ShowsTeams.Should().BeFalse("no team has a game to show");
        page.AllCount.Should().Be(0);

        Type(ui, "");
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 9);
        ui.Click(Pill(ui, "Ukończone"));
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 6);
        Type(ui, "vesper");
        await UiHarness.WaitUntilAsync(() => page.Content == CatalogueContent.FilteredOut);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-filtered-out");
        page.EmptyMessage.Should().Be("Bez filtrów pasuje 1 spolszczenie.");
        ui.Find<Button>("ClearInCard").IsEffectivelyVisible.Should().BeFalse("the empty state offers it below instead");

        ui.Click(ui.Find<Button>("NothingAction"));
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 1);
        page.Query.Should().Be("vesper");
    }

    [AvaloniaFact]
    public async Task On_this_computer_shows_the_games_found_here_and_their_bar_leads_to_the_players_game()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        ui.Clock.Advance(TimeSpan.FromDays(20));

        ui.Click(ui.Find<ToggleButton>("Mine"));
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 2);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-mine");
        ui.Find<TextBlock>().Where(text => text.DataContext is CatalogueCardViewModel card && text.Text == card.Moved && text.IsEffectivelyVisible)
            .Should().HaveCount(2).And.OnlyContain(text => !text.TextLayout.TextLines.Any(line => line.HasCollapsed),
                "a card's date, here a month old, is never cut short");

        ui.Agent.Requests.OfType<GetCataloguePage>().Last().DetectedOnly.Should().BeTrue();
        var ember = Cards(page).Single(card => card.Name == "Ember Hollow");
        var meridian = Cards(page).Single(card => card.Name == "Iron Meridian");
        (ember.OpensGame, meridian.OpensGame, meridian.AwaitsProject).Should().Be((true, false, true),
            "a game waiting for its project is not among the player's games yet");
        var bar = ui.Find<Button>().Single(button => button.Classes.Contains("cardBar") && button.IsEffectivelyVisible);
        bar.GetValue(Avalonia.Automation.AutomationProperties.NameProperty).Should().Be("Masz tę grę – otwórz Ember Hollow w „Moich grach”");

        ui.Click(bar);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(EmberKey));
    }

    [AvaloniaFact]
    public void A_card_tells_what_its_project_last_did_by_its_status_as_the_sites_cards_do()
    {
        var card = new CatalogueCardViewModel(Id("Quiet Bell"));
        void Show(ProjectStatusView status, int announcedDaysAgo, int changedDaysAgo, int? completedDaysAgo = null) => card.Update(
            new CatalogueGameView(card.Id, "quiet-bell", "Quiet Bell")
            {
                Projects =
                [
                    new CatalogueProjectView(Id("Quiet Bell project"), "quiet-bell", "Quiet Bell", TestGames.Lanterns)
                    {
                        Status = status,
                        FirstPublishedAt = TestStates.Now.AddDays(-announcedDaysAgo),
                        UpdatedAt = TestStates.Now.AddDays(-changedDaysAgo),
                        CompletedAt = completedDaysAgo is { } days ? DateOnly.FromDateTime(TestStates.Now.AddDays(-days).Date) : null,
                    },
                ],
                Detected = [MeridianKey],
            }, "", Fresh, new FakeTimeProvider(TestStates.Now));

        Show(new ProjectStatusView(Id("Zapowiedziane"), "Zapowiedziane") { ShowsProgress = true, HoldsReleasesBack = true }, 17, 3);
        (card.Moved, card.AwaitsProject).Should().Be(("Ogłoszono 17 dni temu", true), "an announcement edited since still tells when it came");
        Show(new ProjectStatusView(Id("Korekta"), "Korekta") { ShowsProgress = true }, 17, 17);
        card.Moved.Should().Be("Zmieniono 17 dni temu", "a project at work tells its last change, even one made the day it was announced");
        var official = new ProjectStatusView(Id("Wydane oficjalne"), "Wydane oficjalne") { IsCompleted = true, HoldsReleasesBack = true };
        Show(official, 17, 3, completedDaysAgo: 10);
        (card.Moved, card.IsDetectedOnly, card.AwaitsProject).Should().Be(("Ukończono 10 dni temu", true, false),
            "the game's makers ship that work, so it tells when it was finished and waits for nothing");
        Show(official, 17, 3);
        card.Moved.Should().Be("Zmieniono 3 dni temu", "without the day it was finished it tells its last change");
    }

    [AvaloniaFact]
    public async Task Adult_games_show_only_once_the_player_confirmed_being_an_adult_and_their_covers_stay_veiled()
    {
        using var ui = Open(Fresh);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        ui.Click(ui.Find<Button>("MoreToggle"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("catalogue-more");

        ui.Click(ui.Find<ToggleButton>("AdultPill"));
        var dialog = ui.Shell.Dialogs.Current.Should().BeOfType<ConfirmDialogViewModel>().Subject;
        page.IncludeAdult.Should().BeFalse("the pill waits for the answer");
        dialog.ConfirmCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<ConfirmAdultContent>().Any());
        ui.Push(Fresh with { Version = 3, AdultContentConfirmedUntil = TestStates.Now.AddDays(1) });
        await UiHarness.WaitUntilAsync(() => page.IncludeAdult && Cards(page).Any(card => card.Name == "Velvet Abyss"));

        Cards(page).Single(card => card.Name == "Velvet Abyss").IsCoverBlurred.Should().BeTrue("blurring adult covers is on by default");
        ui.Images.Blurred.Should().Contain(TestGames.Cover("velvet-abyss"));
    }

    [AvaloniaFact]
    public async Task The_head_says_how_fresh_the_catalogue_is_and_a_catalogue_never_saved_explains_itself()
    {
        var loading = Fresh with { Catalogue = new CatalogueView { State = CatalogueState.Refreshing } };
        using var ui = Open(loading);
        var page = Page(ui);
        page.Content.Should().Be(CatalogueContent.Loading);
        page.FreshnessText.Should().Be("Wczytywanie katalogu…");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-loading");

        ui.Push(Fresh.Offline());
        await UiHarness.WaitUntilAsync(() => page.HasAnswer && page.ShowsBanner);
        (page.BannerTitle, page.BannerText).Should().Be(("Brak połączenia z Bazą Spolszczeń.", "Widoczny jest katalog zapisany 53 min temu."));
        page.Freshness.Should().Be(CatalogueFreshness.None, "the banner says where the games come from");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-offline");
        ui.Click(ui.Find<Border>("Banner").FindDescendantOfType<Button>()!);
        ui.Agent.Requests.OfType<CheckNow>().Should().ContainSingle();

        ui.Push(Fresh with { Version = 8, Catalogue = Fresh.Catalogue with { IsUnderMaintenance = true } });
        page.BannerTitle.Should().Be("Trwa przerwa techniczna Bazy Spolszczeń.", "the site says so though it still answers");

        ui.Push(Fresh with { Version = 9, Catalogue = new CatalogueView { State = CatalogueState.Failed, Failure = CatalogueErrorReason.Offline } });
        page.Content.Should().Be(CatalogueContent.Unavailable);
        page.ShowsFilters.Should().BeFalse();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("catalogue-unavailable");
    }

    [AvaloniaFact]
    public async Task Scrolling_to_the_end_of_the_grid_asks_for_the_next_page()
    {
        using var ui = Open(Fresh, Many(130));
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        Cards(page).Should().HaveCount(CataloguePageViewModel.PageSize, "the first answer brings one page");

        var scroller = ui.Find<ScrollViewer>().Single(viewer => viewer.Name == "Scroller" && viewer.DataContext is CataloguePageViewModel);
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() >= 2 * CataloguePageViewModel.PageSize);

        ui.Agent.Requests.OfType<GetCataloguePage>().Select(request => request.Offset).Should().StartWith([0, CataloguePageViewModel.PageSize]);
        Cards(page).Select(card => card.Name).Should().OnlyHaveUniqueItems();
    }

    [AvaloniaFact]
    public async Task A_refresh_keeps_every_game_the_grid_held_though_the_agent_answers_in_shorter_slices()
    {
        using var ui = Open(Fresh, Many(300));
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        while (Cards(page).Count() < 300)
        {
            var shown = Cards(page).Count();
            page.LoadMore();
            await UiHarness.WaitUntilAsync(() => Cards(page).Count() > shown);
        }

        ui.Agent.Requests.Clear();
        ui.Push(Fresh with { Version = 5, Library = Fresh.Library with { ScannedAt = TestStates.Now } });
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<GetCataloguePage>().Count() == 2);
        UiHarness.Settle();

        ui.Agent.Requests.OfType<GetCataloguePage>().Select(request => (request.Offset, request.Count)).Should().Equal((0, 300), (MaxSlice, 100));
        Cards(page).Select(card => card.Name).Should().HaveCount(300).And.OnlyHaveUniqueItems();
    }

    [AvaloniaFact]
    public async Task What_the_agent_left_unanswered_is_asked_for_again_with_its_next_state()
    {
        using var ui = new UiHarness(Fresh);
        var answer = Answer();
        ui.Agent.Answer = request => request is GetCataloguePage or GetCatalogueFilters ? throw new IOException("The agent restarts.") : answer(request);
        ui.Shell.Navigator.Navigate(new CatalogueRoute());
        UiHarness.Settle();
        var page = Page(ui);
        page.Content.Should().Be(CatalogueContent.Loading);

        ui.Agent.Answer = answer;
        ui.Push(Fresh with { Version = 7 });
        await UiHarness.WaitUntilAsync(() => Cards(page).Count() == 9);
        page.StatusOptions.Should().NotBeEmpty("the filters are asked for again too");
    }

    [AvaloniaFact]
    public async Task In_the_smallest_window_the_search_keeps_its_line_and_the_first_covers_show()
    {
        using var ui = Open(Fresh, width: 960, height: 600);
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.HasAnswer);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("catalogue-minimum");

        page.Columns.Should().Be(3);
        page.IsCompact.Should().BeTrue();
        ui.Find<TextBlock>().Single(text => text.Classes.Contains("subtitle") && text.IsEffectivelyVisible).TextLayout.TextLines
            .Should().NotContain(line => line.HasCollapsed, "the subtitle is read whole");
        var card = ButtonOf(ui, "Ember Hollow");
        card.TranslatePoint(new Point(0, 0), ui.Window)!.Value.Y.Should().BeLessThan(480, "the top of the first covers shows above the edge");

        card.Focus(NavigationMethod.Tab);
        ui.Press(Key.Down);
        ((CatalogueCardViewModel)((Control)ui.Window.FocusManager!.GetFocusedElement()!).DataContext!).Name.Should().Be("Saltmarsh: Low Tide");
        ui.Press(Key.F, RawInputModifiers.Control);
        ui.Window.FocusManager!.GetFocusedElement().Should().Be(SearchBox(ui));
    }

    private static UiHarness Open(AppStateView state, CatalogueListing? listing = null, double width = 1280, double height = 800)
    {
        var ui = new UiHarness(state, width: width, height: height);
        ui.Agent.Answer = Answer(listing);
        ui.Shell.Navigator.Navigate(new CatalogueRoute());
        UiHarness.Settle();
        return ui;
    }

    private static CataloguePageViewModel Page(UiHarness ui) => (CataloguePageViewModel)ui.Shell.Navigator.Current!;

    private static Button ButtonOf(UiHarness ui, string game) =>
        ui.Find<Button>().First(button => button.Classes.Contains("cardLink") && button.IsEffectivelyVisible
            && button.DataContext is CatalogueCardViewModel card && card.Name == game);

    private static ToggleButton Pill(UiHarness ui, string name) =>
        ui.Find<ToggleButton>().First(pill => pill.DataContext is FilterOption option && option.Name == name);

    private static TextBox SearchBox(UiHarness ui) =>
        ui.Find<TextBox>().Single(box => box.Name == "SearchBox" && box.DataContext is CataloguePageViewModel);

    private static void Type(UiHarness ui, string text)
    {
        SearchBox(ui).Focus();
        SearchBox(ui).Text = text;
        UiHarness.Settle();
    }

    private static IEnumerable<CatalogueCardViewModel> Cards(CataloguePageViewModel page) => page.Grid.Items.OfType<CatalogueCardViewModel>();
}
