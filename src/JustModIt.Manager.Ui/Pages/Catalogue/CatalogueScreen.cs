using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>The catalogue, its games and its project pages (<see cref="CatalogueRoute"/> and the routes under it).</summary>
internal static class CatalogueScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<CatalogueRoute>((_, services) => new CataloguePageViewModel(services));
        ui.Views.Register<CataloguePageViewModel>(() => new CataloguePage());
        ui.Pages.Register<CatalogueGameRoute>((_, _) => new PlaceholderPageViewModel(Strings.Shell_Catalogue, Icons.Library));
        ui.Pages.Register<CatalogueProjectRoute>((_, _) => new PlaceholderPageViewModel(Strings.Shell_Catalogue, Icons.Library));
    }
}
