using Avalonia.Controls;
using Avalonia.Data.Converters;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The view of <see cref="PackageStepViewModel"/>.</summary>
public sealed partial class PackageStepView : UserControl
{
    /// <summary>The name of the button that removes an author, from the author's name.</summary>
    public static readonly IValueConverter RemoveAuthorName =
        new FuncValueConverter<string, string>(author => Text.Format(Strings.Creator_RemoveAuthor, ("name", author ?? "")));

    /// <summary>Creates the view.</summary>
    public PackageStepView() => InitializeComponent();

    public static string FormatOverline => Strings.Creator_FormatChosen.ToUpper(Text.Culture);

    public static string NextOverline => Strings.Creator_Next.ToUpper(Text.Culture);

    public static string VersionPrefix => Strings.Creator_VersionPrefix;
}
