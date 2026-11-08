using Avalonia.Controls;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The view of <see cref="PackageStepViewModel"/>.</summary>
public sealed partial class PackageStepView : UserControl
{
    /// <summary>Creates the view.</summary>
    public PackageStepView() => InitializeComponent();

    public static string NextOverline => Strings.Creator_Next.ToUpper(Text.Culture);
}
