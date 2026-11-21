using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace JustModIt.Manager.Ui.Overlays;

internal sealed partial class AppUninstallDialogView : UserControl
{
    private AppUninstallDialogViewModel? _dialog;
    private bool _byKeyboard;

    /// <summary>Creates the dialog's view.</summary>
    public AppUninstallDialogView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnClicked);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _dialog?.PropertyChanged -= OnDialogChanged;
        _dialog = DataContext as AppUninstallDialogViewModel;
        _dialog?.PropertyChanged += OnDialogChanged;
    }

    private void OnClicked(object? sender, RoutedEventArgs e) =>
        _byKeyboard = e.Source is Button button && button.Classes.Contains(":focus-visible");

    // The buttons go while the steps run, so a failure gives the focus to its way out, its ring shown only to a player who
    // was using the keyboard, as the dialog's opening does.
    private void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppUninstallDialogViewModel.IsFailed) && _dialog is { IsFailed: true })
        {
            Dispatcher.UIThread.Post(() => UninstallCancel.Focus(_byKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified),
                DispatcherPriority.Loaded);
        }
    }
}
