using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The package creator (<see cref="CreatorRoute"/>).</summary>
internal static class CreatorScreen
{
    /// <summary>Registers the screen's pages and views.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Pages.Register<CreatorRoute>((_, services) => new CreatorPageViewModel(services));
        ui.Views.Register<CreatorPageViewModel>(() => new CreatorPage());
        ui.Views.Register<GameStepViewModel>(() => new GameStepView());
        ui.Views.Register<FilesStepViewModel>(() => new FilesStepView());
        ui.Views.Register<ReviewStepViewModel>(() => new PendingStepView());
        ui.Views.Register<PackageStepViewModel>(() => new PendingStepView());
    }
}
