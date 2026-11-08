using Avalonia.Controls;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The package built, or an earlier one after a failed build, with the site's warnings about it, its test in the game and the
/// way to publish it, from <see cref="BuildViewModel"/> and <see cref="TestInstallViewModel"/>.
/// </summary>
public sealed partial class BuiltCard : UserControl
{
    /// <summary>Creates the view.</summary>
    public BuiltCard() => InitializeComponent();

    /// <summary>The line over a package just built.</summary>
    public static string BuiltOverline => Text.Format(Strings.Creator_BuiltHeading, ("count", 1)).ToUpper(Text.Culture);
}
