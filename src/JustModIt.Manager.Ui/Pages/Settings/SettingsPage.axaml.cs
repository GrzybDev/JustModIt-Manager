using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace JustModIt.Manager.Ui.Pages.Settings;

internal sealed partial class SettingsPage : UserControl
{
    /// <summary>Creates the page's view.</summary>
    public SettingsPage() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as SettingsPageViewModel)?.Uninstall.AppDataRequested += OnAppDataRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as SettingsPageViewModel)?.Uninstall.AppDataRequested -= OnAppDataRequested;
    }

    // Uninstalling every project first puts the originals back, which the app's uninstall cannot do once it is gone.
    private void OnAppDataRequested(object? sender, EventArgs e)
    {
        UninstallAll.BringIntoView();
        UninstallAll.Focus(NavigationMethod.Unspecified);
    }
}
