using Avalonia.Controls;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>The one thing to do about the app's shortcut in Steam's library: add it, add it again, try again or start Steam.</summary>
internal sealed partial class SteamShortcutButton : UserControl
{
    /// <summary>Creates the button's view.</summary>
    public SteamShortcutButton() => InitializeComponent();
}
