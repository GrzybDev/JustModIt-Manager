using Avalonia.Controls;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The project's tab of the release: its part on its version of the game, or the part's operations, and the packages built.</summary>
public sealed partial class ReleaseTab : UserControl
{
    /// <summary>Creates the view.</summary>
    public ReleaseTab() => InitializeComponent();

    /// <summary>The title of a project without a version, as its card in the library says it.</summary>
    public static string NoVersionTitle => Text.Format(Strings.Creator_CardState, ("state", "NoVersion"), ("path", ""));
}
