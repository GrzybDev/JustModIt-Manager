using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Catalogue;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestCatalogue;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CataloguePagesTests
{
    private static readonly AppStateView Fresh = TestGames.State(TestGames.Installed("Ember Hollow", "4100", offer: Offer("Ember Hollow", TestGames.Lanterns)));

    [AvaloniaFact]
    public async Task A_project_of_a_game_not_on_this_computer_leads_to_its_store_and_says_where_it_will_show()
    {
        var state = Fresh with { Library = Fresh.Library with { Stores = [new StoreView("gog") { IsFound = true }] } };
        using var ui = Open(state, new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));
        var page = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("catalogue-project", height: 1500);

        page.Mode.Should().Be(CatalogueGameMode.Project);
        page.Primary.Should().Be("Zobacz grę na GOG", "the store found on this computer comes first");
        page.Released.Should().Be("Wersja 1.0 · Wydano 15 dni temu");
        page.Features.Should().Be("Składniki: Lektor · Polskie tekstury");
        page.Stores.Select(store => (store.Name, store.IsLink)).Should().Equal(("Steam", true), ("GOG", false));
        (page.ShowsAbsent, page.HasNote, page.ShowsStatusBox).Should().Be((true, false, false));
        page.Story.ShowsFilms.Should().BeTrue();
        ui.Find<Border>().Where(badge => badge.Classes.Contains("play") && badge.IsEffectivelyVisible)
            .GroupBy(badge => (GalleryTile)badge.DataContext!).Should().OnlyContain(tile => tile.Key.Item.IsVideo && tile.Count() == 1,
                "a film shows one play button and a picture beside it, both on large tiles, none");
        ui.Unnamed().Should().BeEmpty("every control a player can reach has a name a screen reader reads");

        ui.Click(ui.Find<Button>("PrimaryButton"));
        ui.Click(ui.Find<Button>("SiteButton"));
        ui.Links.Opened.Should().Equal(new Uri("https://store.example.com/gog/glass-orchard"), new Uri("https://example.com/games/glass-orchard/night-owls"));
        ui.Click(ui.Find<Button>("StatusChip"));
        page.IsStatusOpen.Should().BeTrue();
        page.StatusDescription.Should().StartWith("Spolszczenie jest skończone");
        ui.Click(ui.Find<Button>().Single(button => button.Content is "Gra jest zainstalowana, a jej nie widać?"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new UndetectedGamesRoute(ShellSection.Catalogue));
        ui.Shell.Primary.Single(item => item.IsActive).Section.Should().Be(ShellSection.Catalogue, "the panel belongs to where it was opened from");
    }

    [AvaloniaFact]
    public async Task A_game_on_this_computer_leads_to_the_players_games_and_a_project_without_a_file_explains_its_status()
    {
        using var ui = Open(Fresh, new CatalogueProjectRoute("ember-hollow", "lantern-team", "ember-hollow"));
        var ember = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-mine", height: 1300);

        (ember.Primary, ember.PrimaryOpensGame).Should().Be(("Otwórz w „Moich grach”", true));
        ember.Note.Should().Be("Gra jest na tym komputerze. Instalacja i aktualizacje spolszczenia są w „Moich grach”.");
        ember.Features.Should().Be("Warianty: Pełna · Tylko napisy");
        ui.Click(ui.Find<Button>("PrimaryButton"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(EmberKey));

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("iron-meridian", "lantern-team", "iron-meridian"));
        var meridian = await LoadedAsync(ui);
        meridian.HasPrimary.Should().BeFalse("there is nothing to install yet");
        (meridian.ShowsStatusBoxAbove, meridian.StatusOpensExplanation).Should().Be((true, false), "the status explains itself where the button would be");
        meridian.Released.Should().Be("Ogłoszono 17 dni temu");
        meridian.Note.Should().Be("Gra jest na tym komputerze. Gdy grupa wyda spolszczenie, pojawi się w „Moich grach”.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-announced", height: 1100);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("vesper-lake", "paper-cranes", "vesper-lake"));
        var vesper = await LoadedAsync(ui);
        (vesper.ProgressKind, vesper.ProgressPercent, vesper.ProgressValue).Should().Be(("Tłumaczenie", "40%", "21 516 z 54 460 wyrazów"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-progress", height: 1100);
    }

    [AvaloniaFact]
    public async Task A_game_of_several_projects_sets_them_side_by_side_and_each_column_opens_its_page()
    {
        using var ui = Open(Fresh, new CatalogueGameRoute("saltmarsh--low-tide"));
        var page = await LoadedAsync(ui);
        await UiHarness.WaitUntilAsync(() => page.ChoicesText.Contains("Steam", StringComparison.Ordinal));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-compare", height: 1000);

        page.Mode.Should().Be(CatalogueGameMode.Comparison);
        page.TeamsLine.Should().Be("2 spolszczenia: Lantern Team i Paper Cranes");
        page.ChoicesText.Should().Be("Każde przygotowała inna grupa. Wspólne wersje gry: Steam.");
        page.Choices.Select(choice => (choice.Team, choice.Status, choice.ProgressText)).Should().Equal(
            ("Lantern Team", "Ukończone", ""), ("Paper Cranes", "Korekta", "64%"));
        page.Choices[0].Version.Should().Be("Wersja 1.0");
        page.ShowsAbsent.Should().BeTrue("the completed project would bring the game to the player's games");
        page.HasBanner.Should().BeFalse("the page speaks for several teams, so no team's banner heads it");
        (ui.Images.Backdrops[^1].Recipe.Artwork, ui.Images.Backdrops[^1].Recipe.Banner).Should().Be(("", ""), "only the glow of the game's cover lies behind it");

        ui.Click(ui.Find<Button>().Single(button => button.DataContext is ProjectChoice { Team: "Paper Cranes" }));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueProjectRoute("saltmarsh--low-tide", "paper-cranes", "saltmarsh--low-tide"));
        var cranes = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-early", height: 900);
        (cranes.Primary, cranes.ShowsStatusBoxBelow).Should().Be(("Zobacz grę na Steam", true), "an early project shows how far it is after its buttons");
        cranes.ShowsAbsent.Should().BeFalse("a project still at work does not bring its game to the player's games");
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueGameRoute("saltmarsh--low-tide"));
    }

    [AvaloniaFact]
    public async Task A_game_here_that_the_players_games_lack_gets_no_red_button_and_projects_without_a_file_promise_nothing()
    {
        using var ui = Open(Fresh, null, listing: WithoutFiles);
        var answer = Answer(WithoutFiles);
        ui.Agent.Answer = request => request is GetCatalogueGame { GameSlug: "saltmarsh--low-tide" }
            ? new CatalogueGameResult(CatalogueGameView.From(Listing.FindGame("saltmarsh--low-tide")!) with { Detected = [TestGames.SteamKey("4800")] })
            : answer(request);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("saltmarsh--low-tide", "paper-cranes", "saltmarsh--low-tide"));
        var cranes = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-here", height: 900);
        (cranes.HasPrimary, cranes.LeadsWithSite, cranes.Note).Should().Be((false, true, "Gra jest na tym komputerze."),
            "the player has the game, and a project still at work leaves it out of the player's games, so only the site's page leads on");

        ui.Shell.Navigator.Navigate(new CatalogueGameRoute("ashen-reach"));
        var ashen = await LoadedAsync(ui);
        await UiHarness.WaitUntilAsync(() => ashen.ChoicesText.Contains("Steam", StringComparison.Ordinal));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-compare-planned", height: 900);
        (ashen.IsComparison, ashen.ShowsAbsent).Should().Be((true, false), "neither project has a file that would bring the game to the player's games");
    }

    [AvaloniaFact]
    public async Task The_players_games_are_promised_only_for_the_projects_they_offer_or_hold()
    {
        var lanterns = Offer("Saltmarsh: Low Tide", TestGames.Lanterns);
        var saltmarsh = TestGames.Available("Saltmarsh: Low Tide", "4800", lanterns);
        var meridian = TestGames.Installed("Iron Meridian", "4700", offer: Offer("Iron Meridian", TestGames.Lanterns)) with { Projects = [], Project = null };
        var ashen = TestGames.Installed("Ashen Reach", "4810", offer: Offer("Ashen Reach", TestGames.Owls)) with { Projects = [], Project = null };
        using var ui = Open(TestGames.State(saltmarsh, meridian, ashen), null, listing: WithoutFiles);
        var answer = Answer(WithoutFiles);
        var here = new Dictionary<string, GameKey> { ["saltmarsh--low-tide"] = saltmarsh.Key, ["ashen-reach"] = ashen.Key };
        ui.Agent.Answer = request => request is GetCatalogueGame { GameSlug: var slug } && here.TryGetValue(slug, out var key)
            ? new CatalogueGameResult(CatalogueGameView.From(WithoutFiles.FindGame(slug)!) with { Detected = [key] })
            : answer(request);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("saltmarsh--low-tide", "paper-cranes", "saltmarsh--low-tide"));
        var proofreading = await LoadedAsync(ui);
        (proofreading.HasPrimary, proofreading.Note).Should().Be((false, "Gra jest na tym komputerze."),
            "the player's games offer the game only the completed project, not this one with its file");

        ui.Shell.Navigator.Navigate(new CatalogueGameRoute("saltmarsh--low-tide"));
        var comparison = await LoadedAsync(ui);
        await UiHarness.WaitUntilAsync(() => comparison.ChoicesText.Contains("Steam", StringComparison.Ordinal));
        (comparison.Primary, comparison.Note).Should().Be(("Otwórz w „Moich grach”",
            "Gra jest na tym komputerze. Instalacja i aktualizacje spolszczenia są w „Moich grach”."),
            "the player's games offer one of the projects, which has a file, though they leave nothing to choose");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-compare-offered", height: 1000);
        ui.Push(TestGames.State(saltmarsh with { Projects = [lanterns, Offer("Saltmarsh: Low Tide", TestGames.Cranes)], Project = null }, meridian, ashen) with
        {
            Version = 2,
        });
        (comparison.Primary, comparison.Note).Should().Be(("Otwórz w „Moich grach”",
            "Gra jest na tym komputerze. W „Moich grach” można wybrać jedno ze spolszczeń i je zainstalować."));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-compare-mine", height: 1000);
        ui.Click(ui.Find<Button>("PrimaryButton"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(saltmarsh.Key));

        ui.Shell.Navigator.Navigate(new CatalogueGameRoute("ashen-reach"));
        var held = await LoadedAsync(ui);
        await UiHarness.WaitUntilAsync(() => held.ChoicesText.Contains("Steam", StringComparison.Ordinal));
        (held.HasPrimary, held.Note).Should().Be((false, "Gra jest na tym komputerze. Instalacja i aktualizacje spolszczenia są w „Moich grach”."),
            "the installed project keeps its updates there, but with no file among the projects nothing leads there in red");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-compare-held", height: 900);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("saltmarsh--low-tide", "lantern-team", "saltmarsh--low-tide"));
        var completed = await LoadedAsync(ui);
        (completed.Primary, completed.Note).Should().Be(("Otwórz w „Moich grach”",
            "Gra jest na tym komputerze. Instalacja i aktualizacje spolszczenia są w „Moich grach”."));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Click(ui.Find<Button>("PrimaryButton"));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(saltmarsh.Key));

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("iron-meridian", "lantern-team", "iron-meridian"));
        var installed = await LoadedAsync(ui);
        (installed.HasPrimary, installed.Note).Should().Be((false, "Gra jest na tym komputerze. Instalacja i aktualizacje spolszczenia są w „Moich grach”."),
            "the project is installed, so it keeps its updates there though no longer on offer and without a file");
    }

    [AvaloniaFact]
    public async Task A_broken_link_opens_its_game_and_a_broken_game_the_catalogue()
    {
        using var ui = Open(Fresh, null);

        ui.Agent.Navigate(new CatalogueTarget("glass-orchard", "lost-team", "lost-project"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.CurrentRoute == new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueRoute(), "the broken pages gave way to the game's one project");

        ui.Agent.Navigate(new CatalogueTarget("lost-game", "lost-team", "lost-project"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.CurrentRoute == new CatalogueRoute());
        ui.Shell.Navigator.CanGoBack.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task An_adult_game_asks_for_the_age_first_and_unveils_its_pictures_for_good_on_request()
    {
        using var ui = Open(Fresh, null);
        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("velvet-abyss", "night-owls", "velvet-abyss"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.CurrentRoute == new CatalogueRoute());
        ui.Agent.Requests.OfType<GetProjectPage>().Should().BeEmpty("nothing of the game is read before the answer");

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("velvet-abyss", "night-owls", "velvet-abyss"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).ConfirmCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<ConfirmAdultContent>().Any());
        var confirmed = Fresh with { Version = 4, AdultContentConfirmedUntil = TestStates.Now.AddDays(1) };
        ui.Push(confirmed);
        var page = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-adult", height: 1100);
        ui.Agent.Requests.OfType<ConfirmAdultContent>().Should().ContainSingle();
        page.IsCoverBlurred.Should().BeTrue();
        page.Story.IsBlurred.Should().BeTrue("the gallery's pictures are veiled too");
        page.HasBanner.Should().BeTrue("the banner keeps its place, veiled");
        ui.Images.Backdrops[^1].Recipe.IsBlurred.Should().BeTrue("the banner and the page's backdrop are baked veiled");

        ui.Click(ui.Find<Button>("RevealButton"));
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<RevealAdultPictures>().Any());
        ui.Agent.Requests.OfType<RevealAdultPictures>().Single().Game.Should().Be(Id("Velvet Abyss"), "the agent keeps the choice for later windows");
        ui.Push(confirmed with { Version = 5, RevealedAdultGames = [Id("Velvet Abyss")] });
        page.IsCoverBlurred.Should().BeFalse();
        UiHarness.Settle();
        ui.Images.Backdrops[^1].Recipe.IsBlurred.Should().BeFalse();
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        UiHarness.Settle();
        var catalogue = (CataloguePageViewModel)ui.Shell.Navigator.Current!;
        catalogue.IncludeAdult = true;
        await UiHarness.WaitUntilAsync(() => catalogue.Grid.Items.OfType<CatalogueCardViewModel>().Any(card => card.Name == "Velvet Abyss"));
        catalogue.Grid.Items.OfType<CatalogueCardViewModel>().Single(card => card.Name == "Velvet Abyss").IsCoverBlurred.Should().BeFalse(
            "the card shows the pictures unveiled on the game's page");
    }

    [AvaloniaFact]
    public async Task Without_the_page_saved_or_a_connection_the_project_shows_what_the_catalogue_knows_promises_nothing_and_tries_again()
    {
        using var ui = Open(Fresh, null);
        var answer = Answer();
        ui.Agent.Answer = request => request is GetProjectPage ? new ProjectPageResult { Error = Core.Catalog.CatalogueErrorReason.Offline } : answer(request);
        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("vesper-lake", "paper-cranes", "vesper-lake"));
        var page = (CatalogueGamePageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => page.IsOffline);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-offline");

        page.Name.Should().Be("Vesper Lake");
        page.Status.Should().Be("W trakcie tłumaczenia");
        page.Summary.Should().StartWith("Vesper Lake to opowieść", "the catalogue's summary stands in");

        ui.Agent.Answer = answer;
        ui.Click(ui.Find<Callout>("Offline").GetVisualDescendants().OfType<Button>().Single());
        await UiHarness.WaitUntilAsync(() => page.Story.Details is not null);
        page.IsOffline.Should().BeFalse();

        ui.Agent.Answer = request => request switch
        {
            GetProjectPage => new ProjectPageResult { Error = Core.Catalog.CatalogueErrorReason.Offline },
            GetCatalogueGame { GameSlug: "glass-orchard" } => new CatalogueGameResult(
                CatalogueGameView.From(Listing.FindGame("glass-orchard")!) with { Detected = [TestGames.SteamKey("4200")] }),
            _ => answer(request),
        };
        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));
        var here = await LoadedAsync(ui);
        here.IsOffline.Should().BeTrue();
        here.Note.Should().Be("Gra jest na tym komputerze.",
            "without the project's page nothing tells whether it is out yet, as this one is, or whether it will come to the player's games");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-offline-here");
    }

    [AvaloniaFact]
    public async Task In_the_smallest_window_the_project_keeps_its_summary_and_its_button_above_the_edge()
    {
        using var ui = Open(Fresh, new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"), width: 960, height: 600);
        var page = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("catalogue-project-minimum");

        page.IsCompact.Should().BeTrue();
        ui.Find<TextBlock>().Single(text => text.Text == page.Summary && page.HasSummary).IsEffectivelyVisible.Should().BeTrue();
        var button = ui.Find<Button>("PrimaryButton");
        button.TranslatePoint(new Point(0, button.Bounds.Height), ui.Window)!.Value.Y.Should().BeLessThan(600);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("ember-hollow", "lantern-team", "ember-hollow"));
        var ember = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        var note = ui.Find<TextBlock>().Single(text => text.Text == ember.Note && text.IsEffectivelyVisible);
        note.TranslatePoint(new Point(note.Bounds.Width, 0), ui.Window)!.Value.X.Should().BeLessThanOrEqualTo(960 - 32,
            "the note about the game on this computer wraps within the page");
    }

    [AvaloniaFact]
    public async Task A_status_that_releases_nothing_leads_to_the_store_and_an_abandoned_project_says_when_it_last_changed()
    {
        using var ui = Open(Fresh, new CatalogueProjectRoute("pale-signal", "lantern-team", "pale-signal"), listing: WithoutFiles);
        var signal = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-official", height: 900);

        (signal.HasPrimary, signal.StoreAction, signal.ShowsFacts).Should().Be((false, "Otwórz w Steam", false),
            "nothing will be released, so the store is the one way on and nothing is red");
        (signal.Note, signal.ShowsStatusBoxAbove).Should().Be(("Gra jest na tym komputerze.", true), "no project will come to the player's games");
        ui.Click(ui.Find<Button>("StoreButton"));
        ui.Links.Opened.Should().Equal(new Uri("steam://nav/games/details/4900"));

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("dust-archive", "night-owls", "dust-archive"));
        var dust = await LoadedAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("catalogue-project-abandoned", height: 900);

        (dust.StatusDate, dust.Released, dust.ShowsStatusBoxAbove).Should().Be(("Ostatnia zmiana: 17 dni temu (12 września 2026)", "", true),
            "the date stands in the status's box and not again among the facts");
    }

    [AvaloniaFact]
    public async Task A_project_the_site_no_longer_has_is_asked_for_once_and_its_game_leaves_it_out()
    {
        using var ui = Open(Fresh, null);
        var answer = Answer();
        Guid[] gone = [Listing.FindGame("glass-orchard")!.Projects.Single().Uuid, Listing.FindGame("saltmarsh--low-tide")!.Projects[0].Uuid];
        ui.Agent.Answer = request => request is GetProjectPage page && gone.Contains(page.ProjectId) ? new ProjectPageResult { IsGone = true } : answer(request);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.CurrentRoute == new CatalogueRoute());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueRoute(), "the game's only project is gone, so only the catalogue is left");
        ui.Agent.Requests.OfType<GetProjectPage>().Should().ContainSingle("the site is asked once, not by the two pages in turn");

        ui.Shell.Navigator.Navigate(new CatalogueGameRoute("saltmarsh--low-tide"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is CatalogueGamePageViewModel { IsProject: true, Story.Details: not null });
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueProjectRoute("saltmarsh--low-tide", "paper-cranes", "saltmarsh--low-tide"),
            "with one team's project gone the other one's page opens");
        ui.Agent.Requests.OfType<GetProjectPage>().Count(request => request.ProjectId == gone[1]).Should().Be(1);
    }

    [AvaloniaFact]
    public async Task A_game_the_agent_left_unanswered_is_asked_for_again_rather_than_taken_for_a_broken_link()
    {
        using var ui = Open(Fresh, null);
        var answer = Answer();
        ui.Agent.Answer = request => request is GetCatalogueGame ? throw new IOException("The agent restarts.") : answer(request);

        ui.Shell.Navigator.Navigate(new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<GetCatalogueGame>().Any());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(200));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueProjectRoute("glass-orchard", "night-owls", "glass-orchard"));

        ui.Agent.Answer = answer;
        ui.Push(Fresh with { Version = 6 });
        var page = await LoadedAsync(ui);
        page.Name.Should().Be("Glass Orchard");
    }

    private static UiHarness Open(AppStateView state, Route? route, double width = 1280, double height = 800, CatalogueListing? listing = null)
    {
        var ui = new UiHarness(state, width: width, height: height);
        ui.Agent.Answer = Answer(listing);
        ui.Shell.Navigator.Navigate(new CatalogueRoute());
        if (route is not null)
        {
            ui.Shell.Navigator.Navigate(route);
        }

        UiHarness.Settle();
        return ui;
    }

    private static async Task<CatalogueGamePageViewModel> LoadedAsync(UiHarness ui)
    {
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is CatalogueGamePageViewModel { IsLoading: false } page
            && (page.IsComparison || page.Story.Details is not null || page.IsOffline));
        return (CatalogueGamePageViewModel)ui.Shell.Navigator.Current!;
    }
}
