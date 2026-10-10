using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The package creator (<see cref="CreatorRoute"/>).</summary>
internal static class CreatorScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<CreatorRoute>((_, _) => new PlaceholderPageViewModel(Strings.Shell_Creator, Icons.WandSparkles));
    }
}
