using System.ComponentModel;
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
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>
/// The window: its own title bar, the sidebar and the pages. It follows the agent's requests to come to the front and
/// to close, tells the agent whether it is in front and the page shown when it comes back, and lets looping animations run
/// only while it can be seen.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel? _shell;
    private readonly ISystemMotion? _systemMotion;
    private readonly IWindowFrames? _frames;
    private readonly double _smallestHeight;
    private WindowFrame _frame;
    private (PixelPoint Position, Size Size)? _keptFrame;
    private bool _closeDecided;

    /// <summary>Creates the window without a shell, as the XAML designer does.</summary>
    public MainWindow() => InitializeComponent();

    /// <summary>Creates the window showing a shell.</summary>
    /// <param name="shell">The shell to show.</param>
    /// <param name="views">The views of the pages and dialogs.</param>
    /// <param name="icon">The window's icon; null for the system's default.</param>
    /// <param name="systemMotion">The system's wish for reduced motion, read whenever the window comes to the front; null for none.</param>
    /// <param name="frames">The system's frame around windows, which the smallest size makes up for; null for none.</param>
    public MainWindow(ShellViewModel shell, IDataTemplate views, WindowIcon? icon, ISystemMotion? systemMotion, IWindowFrames? frames)
        : this()
    {
        _shell = shell;
        _systemMotion = systemMotion;
        _frames = frames;
        _smallestHeight = MinHeight;
        UseFrame(shell.Frame);
        shell.PropertyChanged += OnShellChanged;
        ScalingChanged += OnScalingChanged;
        DataTemplates.Add(views);
        Icon = icon;
        DataContext = shell;
        RemoteImage.SetLoader(this, shell.Images);
        shell.Agent.BringToFrontRequested += OnBringToFront;
        shell.Agent.ShutdownHandler = CloseForAgentAsync;
        Motion.ReducedChanged += OnReducedChanged;
        // A window the system maps behind another, as focus-stealing prevention does, hears nothing until it gets the focus.
        Classes.Add("inactive");
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
        Dispatcher.UIThread.Post(() =>
        {
            _closeDecided = true;
            Close();
        }, DispatcherPriority.Background);
        return true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Signing out or shutting down the system does not wait for an answer.
        if (_shell is null || _closeDecided || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
        {
            return;
        }

        e.Cancel = true;
        _ = DecideCloseAsync(_shell);
    }

    private async Task DecideCloseAsync(ShellViewModel shell)
    {
        if (await shell.CloseFlow.DecideAsync().ConfigureAwait(true))
        {
            _closeDecided = true;
            Close();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateMotion();
        FitSmallestSize();
        if (_frame == WindowFrame.Screen)
        {
            FillScreen();
        }
        else
        {
            FitScreen();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Motion.ReducedChanged -= OnReducedChanged;
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellChanged;
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
                ShowEdges();
            }

            _keptFrame = null;
            UpdateMotion();
        }
        else if (change.Property == IsVisibleProperty)
        {
            UpdateMotion();
        }
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        if (_keptFrame is { } kept && FrameSize is { } frame && frame != ClientSize)
        {
            _keptFrame = null;
            KeepFrame(kept, new Size(frame.Width - ClientSize.Width, frame.Height - ClientSize.Height));
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
        Classes.Set("inactive", false);
        // The system setting may have changed while the window was in the background.
        Motion.SetReduced(_systemMotion?.IsReduced() == true);
        _shell?.Agent.ReportActivity(true);
        _shell?.Navigator.Current?.OnWindowActivated();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Classes.Set("inactive", true);
        _shell?.Agent.ReportActivity(false);
    }

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
        var loops = !Motion.IsReduced && IsVisible && WindowState != WindowState.Minimized;
        Classes.Set("reducedMotion", Motion.IsReduced);
        Classes.Set("loops", loops);
        Motion.SetLoops(this, loops);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Frame) && _shell is not null)
        {
            SwitchFrame(_shell.Frame);
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        FitSmallestSize();
        ShowEdges();
    }

    // The window's outer frame stays where it is, so nothing jumps or slips under a panel: the pages give the system's title
    // bar its height, or take it back once the window draws its own. How large the system's frame is shows only once the
    // system drew it, which the next resize reports.
    private void SwitchFrame(WindowFrame frame)
    {
        (PixelPoint Position, Size Size)? kept = IsVisible && WindowState == WindowState.Normal
            ? (Position, FrameSize ?? ClientSize)
            : null;
        UseFrame(frame);
        _keptFrame = frame == WindowFrame.System ? kept : null;
        if (frame != WindowFrame.System && kept is { } frameless)
        {
            KeepFrame(frameless, default);
        }
    }

    private void KeepFrame((PixelPoint Position, Size Size) kept, Size borders)
    {
        (Width, Height) = ClientSizeWithin(kept.Size, borders, new Size(MinWidth, MinHeight));
        Position = kept.Position;
    }

    /// <summary>
    /// The size of the window's content inside an outer frame whose borders the system draws, which never goes under the
    /// window's smallest size, so the window grows instead.
    /// </summary>
    /// <param name="outer">The window's outer frame.</param>
    /// <param name="borders">How much wider and taller the system's frame makes the window.</param>
    /// <param name="smallest">The window's smallest content.</param>
    internal static Size ClientSizeWithin(Size outer, Size borders, Size smallest) =>
        new(Math.Max(smallest.Width, outer.Width - borders.Width), Math.Max(smallest.Height, outer.Height - borders.Height));

    // Avalonia sets the window manager's decoration hints again on a shown window, so the frame changes at once.
    private void UseFrame(WindowFrame frame)
    {
        _frame = frame;
        Classes.Set("frameless", frame == WindowFrame.Frameless);
        switch (frame)
        {
            case WindowFrame.System or WindowFrame.Frameless:
                ExtendClientAreaToDecorationsHint = false;
                WindowDecorations = frame == WindowFrame.System ? WindowDecorations.Full : WindowDecorations.None;
                CaptionButtons.IsVisible = frame == WindowFrame.Frameless;
                break;
            // Nothing in gamescope brings a minimised window back, and it shows every window full screen anyway, so the bar
            // names the controls and the way out instead.
            case WindowFrame.Screen:
                ExtendClientAreaToDecorationsHint = false;
                CaptionButtons.IsVisible = false;
                GameModeBar.IsVisible = true;
                CanResize = false;
                Screens.Changed += (_, _) => FillScreen();
                break;
        }

        ShowEdges();
    }

    // A maximised or full-screen window has no edges to drag; the outline is one device pixel at any display scale.
    private void ShowEdges()
    {
        FrameEdges.IsVisible = _frame == WindowFrame.Frameless && WindowState is not (WindowState.Maximized or WindowState.FullScreen);
        Outline.BorderThickness = new Thickness(1 / RenderScaling);
    }

    // Without the system's frame nothing else moves the window by its title bar or maximises it there.
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_frame != WindowFrame.Frameless || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
        }
        else
        {
            BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    // gamescope resizes no window for a full-screen or maximised state and centres a larger one, whose edges then lie off its
    // X screen, out of the pointer's reach, while it still shows the whole window scaled; so the window takes the screen's
    // bounds itself, again whenever docking changes the screen.
    private void FillScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
        {
            var size = screen.Bounds.Size.ToSize(screen.Scaling);
            Position = screen.Bounds.Position;
            Width = size.Width;
            Height = size.Height;
        }
    }

    // A window larger than its screen's working area would leave its bottom or its edge out of reach, since the system
    // centres it without shrinking it.
    private void FitScreen()
    {
        if (WindowState == WindowState.Normal && (Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
        {
            var size = PixelSize.FromSize(FrameSize ?? ClientSize, DesktopScaling);
            if (size.Width > screen.WorkingArea.Width || size.Height > screen.WorkingArea.Height)
            {
                WindowState = WindowState.Maximized;
            }
        }
    }

    // The system keeps a window at least its smallest size plus a title bar, which this window draws inside its own content.
    private void FitSmallestSize()
    {
        if (_frames is not null && TryGetPlatformHandle() is { } handle)
        {
            MinHeight = _smallestHeight - _frames.TitleBarHeight(handle.Handle) / RenderScaling;
        }
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void ToggleMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
