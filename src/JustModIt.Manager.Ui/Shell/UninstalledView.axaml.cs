using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>What the window shows in place of everything else once the app is uninstalled: where its data stayed.</summary>
public sealed partial class UninstalledView : UserControl
{
    /// <summary>Creates the view, which shows the shell it is given as its data.</summary>
    public UninstalledView() => InitializeComponent();

    // What had the focus went with the pages, and closing the window is all that is left to do.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(() => CloseUninstalled.Focus(), DispatcherPriority.Loaded);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
