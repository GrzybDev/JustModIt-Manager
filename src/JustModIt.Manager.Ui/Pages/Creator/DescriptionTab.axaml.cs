using Avalonia.Controls;
using Avalonia.Data.Converters;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The format and the metadata of the package, from <see cref="PackageDescriptionViewModel"/>.</summary>
public sealed partial class DescriptionTab : UserControl
{
    /// <summary>The name of the button that removes an author, from the author's name.</summary>
    public static readonly IValueConverter RemoveAuthorName =
        new FuncValueConverter<string, string>(author => Text.Format(Strings.Creator_RemoveAuthor, ("name", author ?? "")));

    /// <summary>Creates the view.</summary>
    public DescriptionTab() => InitializeComponent();

    public static string FormatOverline => Strings.Creator_FormatChosen.ToUpper(Text.Culture);
}
