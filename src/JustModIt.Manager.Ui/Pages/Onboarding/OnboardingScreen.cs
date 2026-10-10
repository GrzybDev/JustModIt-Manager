namespace JustModIt.Manager.Ui.Pages.Onboarding;

/// <summary>The first-run setup (<see cref="UiComposition.FirstRun"/>), which the window shows until it is completed.</summary>
internal static class OnboardingScreen
{
    /// <summary>Registers the setup and the views of its steps.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Register(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.FirstRun = services => new OnboardingViewModel(services);
        ui.Views.Register<OnboardingViewModel>(() => new OnboardingView());
        ui.Views.Register<WelcomeStep>(() => new WelcomeStepView());
        ui.Views.Register<AutomationStep>(() => new AutomationStepView());
        ui.Views.Register<BackgroundStep>(() => new BackgroundStepView());
        ui.Views.Register<CreatorStep>(() => new CreatorStepView());
        ui.Views.Register<ReadyStep>(() => new ReadyStepView());
    }
}
