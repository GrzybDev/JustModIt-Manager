using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>A game's page (<see cref="GameRoute"/>).</summary>
internal static class GameScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<GameRoute>((route, services) => new GamePageViewModel(services, route.Game));
        ui.Views.Register<GamePageViewModel>(() => new GamePage());
        ui.Views.Register<GalleryViewerViewModel>(() => new GalleryViewer());
    }
}
