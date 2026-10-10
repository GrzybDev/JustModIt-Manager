using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Undetected;

/// <summary>What detection found, for a missing game (<see cref="UndetectedGamesRoute"/>).</summary>
internal static class UndetectedGamesScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<UndetectedGamesRoute>((_, _) => new PlaceholderPageViewModel(Strings.Shell_PageUndetected, Icons.CircleHelp));
    }
}
