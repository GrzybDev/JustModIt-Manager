using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Licenses;

/// <summary>The licences (<see cref="LicensesRoute"/>).</summary>
internal static class LicensesScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<LicensesRoute>((_, _) => new PlaceholderPageViewModel(Strings.Shell_PageLicenses, Icons.Scale));
    }
}
