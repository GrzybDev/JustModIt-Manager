using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>A game's page (<see cref="GameRoute"/>).</summary>
internal static class GameScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<GameRoute>((route, services) => new PlaceholderPageViewModel(
            services.Agent.State.Games.FirstOrDefault(game => game.Key == route.Game)?.Name ?? Strings.Shell_PageGame, Icons.Gamepad));
    }
}
