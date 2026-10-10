using Avalonia.Media;
using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages;

/// <summary>The page of a screen not built yet: its title over an empty state, so the window can be walked end to end.</summary>
/// <param name="title">The screen's name.</param>
/// <param name="icon">The screen's icon.</param>
public sealed class PlaceholderPageViewModel(string title, Geometry icon) : PageViewModel
{
    public override string Title { get; } = title;

    public Geometry Icon { get; } = icon;

    /// <summary>Registers the placeholder's view, which every placeholder page shares.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void RegisterView(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Views.Register<PlaceholderPageViewModel>(() => new PlaceholderPage());
    }
}
