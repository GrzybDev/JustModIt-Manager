using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>The calm state shown instead of the pages while the window waits for the agent or for its update.</summary>
public sealed partial class ConnectingView : UserControl
{
    private ShellViewModel? _shell;
    private string? _title;

    /// <summary>Creates the view, which shows the shell it is given as its data.</summary>
    public ConnectingView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _shell?.PropertyChanged -= OnShellChanged;
        _shell = DataContext as ShellViewModel;
        _shell?.PropertyChanged += OnShellChanged;
        _title = _shell?.ConnectingTitle;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ConnectingTitle))
        {
            ShowTitle();
        }
        else if (e.PropertyName == nameof(ShellViewModel.IsSlowToConnect) && _shell?.IsSlowToConnect == true)
        {
            FocusStartAgent();
        }
    }

    // The button takes the focus as it comes, so a controller's button that Steam maps to Enter presses it; its ring shows only
    // to a player who was using the keys.
    private void FocusStartAgent()
    {
        var byKeyboard = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is StyledElement focused
            && focused.Classes.Contains(":focus-visible");
        Dispatcher.UIThread.Post(() => StartAgentButton.Focus(byKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified),
            DispatcherPriority.Loaded);
    }

    // A new title fades in over the old one, so the state's change shows; with reduced motion it only changes.
    private void ShowTitle()
    {
        if (_shell?.ConnectingTitle is { } title && title != _title)
        {
            _title = title;
            if (!Motion.IsReduced)
            {
                Motion.Refresh(TitleText);
            }
        }
    }
}
