using Avalonia;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Diagnostics;
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
    private readonly IRunningSteam? _steam;
    private MainWindow? _window;

    private UiSession(UiComposition composition, UiServices services, ShellViewModel shell, IDisposable? owned, IRunningSteam? steam)
    {
        Composition = composition;
        Services = services;
        Shell = shell;
        _owned = owned;
        _steam = steam;
    }

    public UiComposition Composition { get; }

    public UiServices Services { get; }

    public ShellViewModel Shell { get; }

    /// <summary>Registers the shared screens, lets the app add its own, and creates the shell.</summary>
    /// <param name="application">The running application, whose resources and styles the app may add to.</param>
    /// <param name="context">What the process knows about the app.</param>
    /// <param name="agent">The agent the window talks to.</param>
    /// <param name="images">Loads the site's images; null for the image cache in the app's cache folder.</param>
    /// <param name="links">Opens addresses outside the window, except in Game Mode, where Steam does; null for the system's shell.</param>
    /// <param name="clock">The time the pages tell dates against; null for the system's clock.</param>
    /// <param name="pickers">The pickers of folders and files; null for the window's own.</param>
    /// <param name="errorReports">Sends crash reports; null for the DSN the app was built with.</param>
    /// <param name="clipboard">Takes text the player pastes elsewhere; null for the window's own clipboard.</param>
    /// <param name="steam">Takes the links and the keyboard of a window in Game Mode; null for the Steam behind the home folder.</param>
    public static UiSession Create(Application application, UiContext context, IAgent agent, IImageLoader? images = null,
        IExternalLinks? links = null, TimeProvider? clock = null, IFilePickers? pickers = null, IErrorReports? errorReports = null,
        ITextClipboard? clipboard = null, IRunningSteam? steam = null)
    {
        AccentPalette.From(context.AccentColor).Apply(application.Resources);
        var composition = new UiComposition(application.Resources, application.Styles);
        composition.Views.Register<ConfirmDialogViewModel>(() => new ConfirmDialogView());
        composition.Views.Register<ChoiceDialogViewModel>(() => new ChoiceDialogView());
        composition.Views.Register<UncleanExitDialogViewModel>(() => new UncleanExitDialogView());
        composition.Views.Register<ErrorReportExampleViewModel>(() => new ErrorReportExampleView());
        composition.Views.Register<AppUninstallDialogViewModel>(() => new AppUninstallDialogView());
        composition.Views.Register<ControlsHintDialogViewModel>(() => new ControlsHintDialogView());
        Screens.Register(composition);
        context.Compose?.Invoke(composition);

        var cache = images is null ? ImageCache.For(context) : null;
        var gameModeSteam = context.InGameMode ? steam ?? new RunningSteam(context.Loggers.CreateLogger<RunningSteam>()) : null;
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
            Links = gameModeSteam is not null
                ? new SteamLinks(gameModeSteam)
                : links ?? new ShellLinks(context.Loggers.CreateLogger<ShellLinks>()),
            Clock = clock ?? TimeProvider.System,
            Pickers = pickers ?? new StorageProviderPickers(() => session?._window),
            Clipboard = clipboard ?? new WindowClipboard(() => session?._window),
            ErrorReports = errorReports
                ?? new ErrorReports(ErrorReportTarget.Parse(context.ErrorReportsDsn), $"{context.Site.ProductName}/{context.Version}"),
        };
        session = new UiSession(composition, services, new ShellViewModel(services, composition.FirstRun), cache, gameModeSteam);
        return session;
    }

    /// <summary>Creates the main window, showing the shell, whose text fields open Steam's keyboard in Game Mode.</summary>
    public MainWindow CreateWindow()
    {
        _window = new(Shell, Composition.Views, Services.Context.Branding.CreateIcon(), Services.Context.SystemMotion,
            Services.Context.WindowFrames);
        if (_steam is not null)
        {
            SteamKeyboard.Attach(_window, _steam);
        }

        return _window;
    }

    public void Dispose()
    {
        Shell.Dispose();
        _owned?.Dispose();
    }
}
