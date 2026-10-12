using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages.Licenses;

/// <summary>The licences (<see cref="LicensesRoute"/>).</summary>
internal static class LicensesScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<LicensesRoute>((_, services) => new LicensesPageViewModel(services));
        ui.Views.Register<LicensesPageViewModel>(() => new LicensesPage());
    }
}
