using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>The catalogue, its games and its project pages (<see cref="CatalogueRoute"/> and the routes under it).</summary>
internal static class CatalogueScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var gone = new GoneProjects();
        ui.Pages.Register<CatalogueRoute>((_, services) => new CataloguePageViewModel(services));
        ui.Pages.Register<CatalogueGameRoute>((route, services) => new CatalogueGamePageViewModel(services, route.GameSlug, null, gone));
        ui.Pages.Register<CatalogueProjectRoute>((route, services) =>
            new CatalogueGamePageViewModel(services, route.GameSlug, (route.TeamSlug, route.ProjectSlug), gone));
        ui.Views.Register<CataloguePageViewModel>(() => new CataloguePage());
        ui.Views.Register<CatalogueGamePageViewModel>(() => new CatalogueGamePage());
    }
}
