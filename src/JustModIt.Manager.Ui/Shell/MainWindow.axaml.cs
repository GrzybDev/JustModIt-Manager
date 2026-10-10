using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>
/// The window: its own title bar, the sidebar and the pages. It follows the agent's requests to come to the front and
/// to close, tells the agent whether it is in front, and lets looping animations run only while it can be seen.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel? _shell;
    private readonly ISystemMotion? _systemMotion;

    /// <summary>Creates the window without a shell, as the XAML designer does.</summary>
    public MainWindow() => InitializeComponent();

    /// <summary>Creates the window showing a shell.</summary>
    /// <param name="shell">The shell to show.</param>
    /// <param name="views">The views of the pages and dialogs.</param>
    /// <param name="icon">The window's icon; null for the system's default.</param>
    /// <param name="systemMotion">The system's wish for reduced motion, read whenever the window comes to the front; null for none.</param>
    public MainWindow(ShellViewModel shell, IDataTemplate views, WindowIcon? icon, ISystemMotion? systemMotion)
        : this()
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(views);
        _shell = shell;
        _systemMotion = systemMotion;
        DataTemplates.Add(views);
        Icon = icon;
        DataContext = shell;
        shell.Agent.BringToFrontRequested += OnBringToFront;
        shell.Agent.ShutdownHandler = CloseForAgentAsync;
        Motion.ReducedChanged += OnReducedChanged;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        UpdateMotion();
    }

    /// <summary>Closes the window because the agent asked, once every page agrees.</summary>
    /// <param name="reason">Why the agent asks.</param>
    /// <returns>False when a page keeps the player's unsaved work and the window stays.</returns>
    public async Task<bool> CloseForAgentAsync(ShutdownReason reason)
    {
        if (_shell is not null && !await _shell.Navigator.CanCloseAsync().ConfigureAwait(true))
        {
            return false;
        }

        // The answer goes out first; closing ends the process, which the agent then waits for.
        Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        return true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateMotion();
    }

    protected override void OnClosed(EventArgs e)
    {
        Motion.ReducedChanged -= OnReducedChanged;
        if (_shell is not null)
        {
            _shell.Agent.BringToFrontRequested -= OnBringToFront;
            _shell.Agent.ShutdownHandler = null;
        }

        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;
            // The base constructor may report a state before the template's parts exist.
            if (MaximizeIcon is not null)
            {
                MaximizeIcon.Data = maximized ? Icons.Copy : Icons.Square;
                var name = maximized ? Strings.Shell_Restore : Strings.Shell_Maximize;
                ToolTip.SetTip(MaximizeButton, name);
                AutomationProperties.SetName(MaximizeButton, name);
            }

            UpdateMotion();
        }
        else if (change.Property == IsVisibleProperty)
        {
            UpdateMotion();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.XButton1 && _shell?.Navigator.GoBackCommand.CanExecute(null) == true)
        {
            _shell.Navigator.GoBackCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        // The system setting may have changed while the window was in the background.
        Motion.SetReduced(_systemMotion?.IsReduced() == true);
        _shell?.Agent.ReportActivity(true);
    }

    private void OnDeactivated(object? sender, EventArgs e) => _shell?.Agent.ReportActivity(false);

    private void OnBringToFront(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void OnReducedChanged(object? sender, EventArgs e) => UpdateMotion();

    // Loops cost nothing while nobody sees them, so they run only in a visible, unminimised window.
    private void UpdateMotion()
    {
        Classes.Set("reducedMotion", Motion.IsReduced);
        Classes.Set("loops", !Motion.IsReduced && IsVisible && WindowState != WindowState.Minimized);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
