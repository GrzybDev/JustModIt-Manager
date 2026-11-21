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
        var game = new GameKey(StoreId.Gog, "1100000001");

        var idle = TestStates.Idle();
        AgentLinks.RoutesFor(new GameTarget("gog", "1100000001"), idle, showsCreator: false).Should()
            .Equal(new LibraryRoute(), new GameRoute(game));
        AgentLinks.RoutesFor(new CatalogueTarget("ember-hollow", "lantern-team", "full"), idle, showsCreator: false).Should()
            .Equal(new CatalogueRoute(), new CatalogueProjectRoute("ember-hollow", "lantern-team", "full"));
        AgentLinks.RoutesFor(new CatalogueTarget("ember-hollow"), idle, showsCreator: false).Should()
            .Equal(new CatalogueRoute(), new CatalogueGameRoute("ember-hollow"));
        AgentLinks.RoutesFor(new GameTarget("not a store", "1"), idle, showsCreator: false).Should().Equal(new LibraryRoute());
        AgentLinks.RoutesFor(new CreatorTarget(), idle, showsCreator: false).Should().Equal(new LibraryRoute());
        AgentLinks.RoutesFor(new CreatorTarget(), idle, showsCreator: true).Should().Equal(new CreatorRoute());

        using var ui = new UiHarness(TestStates.Idle());
        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Agent.Navigate(new GameTarget("gog", "1100000001"));
        UiHarness.Settle();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(game));
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
    }

    [AvaloniaFact]
    public async Task A_link_that_waited_for_the_window_opens_its_page_and_says_so()
    {
        // Only Game Mode, whose window Steam starts, makes a link wait for the window.
        var state = TestStates.Idle();
        using var ui = new UiHarness(state with
        {
            Games = [TestGames.Available("Ember Hollow", TestStates.Ember.StoreGameId)],
            Settings = state.Settings with { ControlsHintShown = true },
        }, context: context => context with { Frame = WindowFrame.Screen });
        ui.SizeToDeck();

        ui.Agent.Navigate(new GameTarget("steam", TestStates.Ember.StoreGameId), waited: true);
        UiHarness.Settle();

        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(TestStates.Ember));
        ui.Shell.Toasts.Items.Should().ContainSingle().Which.Message.Should().Be("Link do gry Ember Hollow czekał na otwarcie okna.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("link-waited-game");

        ui.Agent.Navigate(new SettingsTarget(), waited: true);
        ui.Agent.Navigate(new CatalogueTarget());
        UiHarness.Settle();

        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueRoute());
        ui.Shell.Toasts.Items.Select(toast => toast.Message).Should().Equal("Link do gry Ember Hollow czekał na otwarcie okna.",
            "Link czekał na otwarcie okna.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("link-waited");
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

    [Fact]
    public void A_link_the_window_opens_on_linux_leaves_the_appimages_own_variables_and_mount_behind()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux runs the window from an AppImage, whose mount goes when it exits.");
        string[] names = ["APPIMAGE", "APPDIR", "ARGV0", "OWD", "PATH"];
        var saved = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        // What the AppImage's runtime exports for the window's own process.
        Environment.SetEnvironmentVariable("APPIMAGE", "/home/player/Applications/Example App.AppImage");
        Environment.SetEnvironmentVariable("APPDIR", "/tmp/.mount_ExampleAbc123");
        Environment.SetEnvironmentVariable("ARGV0", "/home/player/Applications/Example App.AppImage");
        Environment.SetEnvironmentVariable("OWD", "/home/player");
        Environment.SetEnvironmentVariable("PATH", "/tmp/.mount_ExampleAbc123/usr/bin/:/usr/bin::/bin");
        try
        {
            var start = ShellLinks.StartOf("https://example.com/games");

            (start.FileName, start.UseShellExecute).Should().Be(("xdg-open", false));
            start.ArgumentList.Should().Equal("https://example.com/games");
            start.Environment.Keys.Should().NotContain(["APPIMAGE", "APPDIR", "ARGV0", "OWD"]);
            start.Environment["PATH"].Should().Be("/usr/bin::/bin", "a program found on the mount fails once the window exits");
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
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
