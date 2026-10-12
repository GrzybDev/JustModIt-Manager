using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>The settings (<see cref="SettingsRoute"/>).</summary>
internal static class SettingsScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<SettingsRoute>((_, services) => new SettingsPageViewModel(services));
        ui.Views.Register<SettingsPageViewModel>(() => new SettingsPage());
    }
}
