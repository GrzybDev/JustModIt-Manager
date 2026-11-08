using Avalonia.Controls;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The package's build, its result and what comes next, from <see cref="BuildViewModel"/> and
/// <see cref="TestInstallViewModel"/>.
/// </summary>
public sealed partial class BuiltCard : UserControl
{
    /// <summary>Creates the view.</summary>
    public BuiltCard() => InitializeComponent();

    public static string NextOverline => Strings.Creator_Next.ToUpper(Text.Culture);
}
