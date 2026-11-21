using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>The status card at the foot of the sidebar, whose texts fade in anew whenever its subject changes.</summary>
public sealed partial class StatusCard : UserControl
{
    private StatusCardViewModel? _status;

    /// <summary>Creates the card, which shows the status card view model it is given as its data.</summary>
    public StatusCard() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _status?.PropertyChanged -= OnStatusChanged;
        _status?.ActionFocusRequested -= OnActionFocusRequested;
        _status = DataContext as StatusCardViewModel;
        _status?.PropertyChanged += OnStatusChanged;
        _status?.ActionFocusRequested += OnActionFocusRequested;
    }

    // Once the pages are back, so the button is there to take it; a focus given back opens no tooltip.
    private void OnActionFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => StatusAction.Focus(), DispatcherPriority.Loaded);

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusCardViewModel.Title))
        {
            Motion.Refresh(Texts);
        }
    }
}
