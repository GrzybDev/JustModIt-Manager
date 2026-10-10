using Avalonia;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages;
using JustModIt.Manager.Ui.Shell;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui;

/// <summary>The window put together for an agent: the shared screens and the app's own, the services and the shell.</summary>
public sealed class UiSession : IDisposable
{
    private UiSession(UiComposition composition, UiServices services, ShellViewModel shell)
    {
        Composition = composition;
        Services = services;
        Shell = shell;
    }

    public UiComposition Composition { get; }

    public UiServices Services { get; }

    public ShellViewModel Shell { get; }

    /// <summary>Registers the shared screens, lets the app add its own, and creates the shell.</summary>
    /// <param name="application">The running application, whose resources and styles the app may add to.</param>
    /// <param name="context">What the process knows about the app.</param>
    /// <param name="agent">The agent the window talks to.</param>
    public static UiSession Create(Application application, UiContext context, IAgent agent)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);
        AccentPalette.From(context.AccentColor).Apply(application.Resources);
        var composition = new UiComposition(application.Resources, application.Styles);
        composition.Views.Register<ConfirmDialogViewModel>(() => new ConfirmDialogView());
        composition.Views.Register<ChoiceDialogViewModel>(() => new ChoiceDialogView());
        Screens.Register(composition);
        context.Compose?.Invoke(composition);

        UiServices? services = null;
        var navigator = new Navigator(route => composition.Pages.Create(route, services!));
        services = new UiServices
        {
            Agent = agent,
            Navigator = navigator,
            Dialogs = new DialogHost(),
            Toasts = new ToastHost(),
            Context = context,
        };
        return new UiSession(composition, services, new ShellViewModel(services, composition.FirstRun));
    }

    /// <summary>Creates the main window, showing the shell.</summary>
    public MainWindow CreateWindow() =>
        new(Shell, Composition.Views, Services.Context.Branding.CreateIcon(), Services.Context.SystemMotion);

    public void Dispose() => Shell.Dispose();
}
