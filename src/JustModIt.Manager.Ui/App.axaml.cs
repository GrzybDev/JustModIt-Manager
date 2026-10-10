using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui;

/// <summary>The window's application: the theme, and the window once the platform is up.</summary>
public sealed partial class App : Application
{
    private readonly UiContext? _context;
    private readonly Func<IAgent>? _connect;

    /// <summary>Creates the application with the theme alone, as tests and the designer do.</summary>
    public App()
    {
    }

    /// <summary>Creates the application of the window's process.</summary>
    /// <param name="context">What the process knows about the app and the agent.</param>
    /// <param name="connect">Connects to the agent once the platform runs, so its events can reach the UI thread.</param>
    internal App(UiContext context, Func<IAgent> connect)
    {
        _context = context;
        _connect = connect;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Motion.SetReduced(_context?.SystemMotion?.IsReduced() == true);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_context is not null && _connect is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = UiSession.Create(this, _context, _connect());
            desktop.MainWindow = session.CreateWindow();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => session.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
