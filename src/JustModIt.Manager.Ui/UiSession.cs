using Avalonia;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages;
using JustModIt.Manager.Ui.Shell;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui;

/// <summary>The window put together for an agent: the shared screens and the app's own, the services and the shell.</summary>
public sealed class UiSession : IDisposable
{
    private readonly IDisposable? _owned;
    private MainWindow? _window;

    private UiSession(UiComposition composition, UiServices services, ShellViewModel shell, IDisposable? owned)
    {
        Composition = composition;
        Services = services;
        Shell = shell;
        _owned = owned;
    }

    public UiComposition Composition { get; }

    public UiServices Services { get; }

    public ShellViewModel Shell { get; }

    /// <summary>Registers the shared screens, lets the app add its own, and creates the shell.</summary>
    /// <param name="application">The running application, whose resources and styles the app may add to.</param>
    /// <param name="context">What the process knows about the app.</param>
    /// <param name="agent">The agent the window talks to.</param>
    /// <param name="images">Loads the site's images; null for the image cache in the app's cache folder.</param>
    /// <param name="links">Opens addresses outside the window; null for the system's shell.</param>
    /// <param name="clock">The time the pages tell dates against; null for the system's clock.</param>
    /// <param name="pickers">The pickers of folders and files; null for the window's own.</param>
    public static UiSession Create(Application application, UiContext context, IAgent agent, IImageLoader? images = null,
        IExternalLinks? links = null, TimeProvider? clock = null, IFilePickers? pickers = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);
        AccentPalette.From(context.AccentColor).Apply(application.Resources);
        var composition = new UiComposition(application.Resources, application.Styles);
        composition.Views.Register<ConfirmDialogViewModel>(() => new ConfirmDialogView());
        composition.Views.Register<ChoiceDialogViewModel>(() => new ChoiceDialogView());
        composition.Views.Register<UncleanExitDialogViewModel>(() => new UncleanExitDialogView());
        Screens.Register(composition);
        context.Compose?.Invoke(composition);

        var cache = images is null ? ImageCache.For(context) : null;
        UiServices? services = null;
        UiSession? session = null;
        var navigator = new Navigator(route => composition.Pages.Create(route, services!));
        services = new UiServices
        {
            Agent = agent,
            Navigator = navigator,
            Dialogs = new DialogHost(),
            Toasts = new ToastHost(),
            Images = images ?? cache!,
            Context = context,
            Links = links ?? new ShellLinks(context.Loggers.CreateLogger<ShellLinks>()),
            Clock = clock ?? TimeProvider.System,
            Pickers = pickers ?? new StorageProviderPickers(() => session?._window),
        };
        session = new UiSession(composition, services, new ShellViewModel(services, composition.FirstRun), cache);
        return session;
    }

    /// <summary>Creates the main window, showing the shell.</summary>
    public MainWindow CreateWindow() =>
        _window = new(Shell, Composition.Views, Services.Context.Branding.CreateIcon(), Services.Context.SystemMotion);

    public void Dispose()
    {
        Shell.Dispose();
        _owned?.Dispose();
    }
}
