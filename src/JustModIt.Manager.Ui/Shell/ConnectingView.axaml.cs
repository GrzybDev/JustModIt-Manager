using Avalonia.Controls;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>The calm state shown instead of the pages while the window waits for the agent or for its update.</summary>
public sealed partial class ConnectingView : UserControl
{
    /// <summary>Creates the view, which shows the shell it is given as its data.</summary>
    public ConnectingView() => InitializeComponent();
}
