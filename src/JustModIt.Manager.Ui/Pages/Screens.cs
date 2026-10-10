using JustModIt.Manager.Ui.Pages.Catalogue;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Pages.Licenses;
using JustModIt.Manager.Ui.Pages.Onboarding;
using JustModIt.Manager.Ui.Pages.Settings;
using JustModIt.Manager.Ui.Pages.Undetected;

namespace JustModIt.Manager.Ui.Pages;

/// <summary>The shared screens, each registering its pages and views in a file of its own.</summary>
internal static class Screens
{
    /// <summary>Registers every shared screen.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        PlaceholderPageViewModel.RegisterView(ui);
        OnboardingScreen.Register(ui);
        LibraryScreen.Register(ui);
        GameScreen.Register(ui);
        UndetectedGamesScreen.Register(ui);
        CatalogueScreen.Register(ui);
        CreatorScreen.Register(ui);
        SettingsScreen.Register(ui);
        LicensesScreen.Register(ui);
    }
}
