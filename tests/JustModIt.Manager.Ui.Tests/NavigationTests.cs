using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class NavigationTests
{
    [AvaloniaFact]
    public async Task A_detail_page_goes_on_top_and_every_way_back_returns_to_the_page_as_it_was()
    {
        using var ui = new UiHarness(TestStates.Idle());
        var library = ui.Shell.Navigator.Current;

        ui.Shell.Navigator.Navigate(new GameRoute(TestStates.Ember));
        UiHarness.Settle();
        ui.Find<Button>("BackButton").IsVisible.Should().BeTrue();
        ui.Shell.Primary[0].IsActive.Should().BeTrue("a game's page belongs to the player's games");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("navigation-game-page");

        ui.Click(ui.Find<Button>("BackButton"));
        ui.Shell.Navigator.Current.Should().BeSameAs(library);

        ui.Shell.Navigator.Navigate(new UndetectedGamesRoute());
        ui.Press(Key.Left, RawInputModifiers.Alt);
        ui.Shell.Navigator.Current.Should().BeSameAs(library);

        ui.Shell.Navigator.Navigate(new GameRoute(TestStates.Ember));
        ui.Window.MouseDown(new(600, 400), MouseButton.XButton1);
        ui.Window.MouseUp(new(600, 400), MouseButton.XButton1);
        UiHarness.Settle();
        ui.Shell.Navigator.Current.Should().BeSameAs(library);
        ui.Shell.Navigator.CanGoBack.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Sidebar_destinations_keep_their_page_and_drop_the_pages_on_the_way_back()
    {
        var games = new List<DisposablePage>();
        using var ui = new UiHarness(TestStates.Idle(), compose: app => app.Pages.Register<GameRoute>((_, _) =>
        {
            games.Add(new DisposablePage());
            return games[^1];
        }));
        var navigator = ui.Shell.Navigator;
        var library = navigator.Current;
        navigator.Navigate(new GameRoute(TestStates.Ember));

        navigator.Navigate(new SettingsRoute());
        navigator.CanGoBack.Should().BeFalse();
        navigator.Navigate(new LibraryRoute());

        navigator.Current.Should().BeSameAs(library);
        games.Should().ContainSingle().Which.IsDisposed.Should().BeTrue();
        navigator.Pages.Should().HaveCount(2, "the player's games and the settings stay for as long as the window lives");
    }

    [AvaloniaFact]
    public void A_link_opens_its_place_on_top_of_its_list_and_never_more_than_that()
    {
        var game = new GameKey(StoreId.Gog, "1207658924");

        AgentLinks.RoutesFor(new GameTarget("gog", "1207658924"), creatorMode: false).Should()
            .Equal(new LibraryRoute(), new GameRoute(game));
        AgentLinks.RoutesFor(new CatalogueTarget("ember-hollow", "lantern-team", "full"), creatorMode: false).Should()
            .Equal(new CatalogueRoute(), new CatalogueGameRoute("ember-hollow"), new CatalogueProjectRoute("ember-hollow", "lantern-team", "full"));
        AgentLinks.RoutesFor(new CatalogueTarget("ember-hollow"), creatorMode: false).Should()
            .Equal(new CatalogueRoute(), new CatalogueGameRoute("ember-hollow"));
        AgentLinks.RoutesFor(new GameTarget("not a store", "1"), creatorMode: false).Should().Equal(new LibraryRoute());
        AgentLinks.RoutesFor(new CreatorTarget(), creatorMode: false).Should().Equal(new LibraryRoute());
        AgentLinks.RoutesFor(new CreatorTarget(), creatorMode: true).Should().Equal(new CreatorRoute());

        using var ui = new UiHarness(TestStates.Idle());
        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Agent.Navigate(new GameTarget("gog", "1207658924"));
        UiHarness.Settle();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(game));
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
    }

    [AvaloniaFact]
    public void An_app_puts_its_own_page_and_view_in_place_of_a_shared_screens()
    {
        using var ui = new UiHarness(TestStates.Idle(), compose: app =>
        {
            app.Pages.Register<LibraryRoute>((_, _) => new OwnLibrary());
            app.Views.Register<OwnLibrary>(() => new TextBlock { Name = "OwnLibraryView" });
        });

        ui.Shell.Navigator.Current.Should().BeOfType<OwnLibrary>();
        ui.Find<TextBlock>("OwnLibraryView").DataContext.Should().BeOfType<OwnLibrary>();
    }

    [AvaloniaFact]
    public void Every_route_has_a_page()
    {
        using var ui = new UiHarness(TestStates.Idle(creatorMode: true));
        Route[] routes =
        [
            new LibraryRoute(), new GameRoute(TestStates.Ember), new UndetectedGamesRoute(), new CatalogueRoute(),
            new CatalogueGameRoute("ember-hollow"), new CatalogueProjectRoute("ember-hollow", "lantern-team", "full"), new CreatorRoute(),
            new SettingsRoute(), new LicensesRoute(),
        ];

        foreach (var route in routes)
        {
            ui.Shell.Navigator.Navigate(route);
            ui.Shell.Navigator.Current!.Title.Should().NotBeNullOrWhiteSpace();
        }
    }

    private sealed class OwnLibrary : PageViewModel
    {
        public override string Title => "Own";
    }

    private sealed class DisposablePage : PageViewModel, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public override string Title => "Game";

        public void Dispose() => IsDisposed = true;
    }
}
