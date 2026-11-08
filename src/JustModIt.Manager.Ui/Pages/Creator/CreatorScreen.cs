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
        CreatorPageViewModel? creator = null;
        ui.Pages.Register<CreatorRoute>((_, services) => creator ??= new CreatorPageViewModel(services));
        ui.Pages.Register<CreatorProjectRoute>((_, services) => (creator ??= new CreatorPageViewModel(services)).OpenPage!);
        ui.Views.Register<ProjectPageViewModel>(() => new ProjectPage());
        ui.Views.Register<CreatorPageViewModel>(() => new CreatorPage());
        ui.Views.Register<GameStepViewModel>(() => new GameStepView());
        ui.Views.Register<FilesStepViewModel>(() => new FilesStepView());
        ui.Views.Register<ReviewViewModel>(() => new ReviewStepView());
        ui.Views.Register<PackageStepViewModel>(() => new PackageStepView());
    }
}
