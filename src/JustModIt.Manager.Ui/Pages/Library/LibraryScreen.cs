using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>The player's games (<see cref="LibraryRoute"/>).</summary>
internal static class LibraryScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<LibraryRoute>((_, services) => new LibraryPageViewModel(services));
        ui.Views.Register<LibraryPageViewModel>(() => new LibraryPage());
    }
}
