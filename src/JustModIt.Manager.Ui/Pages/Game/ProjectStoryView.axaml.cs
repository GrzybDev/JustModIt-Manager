using Avalonia.Controls;
using Avalonia.Data.Converters;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>The view of <see cref="ProjectStory"/>, which puts the people behind the project under the game's story when narrow.</summary>
public sealed partial class ProjectStoryView : UserControl
{
    /// <summary>The description's toggle: more while it is folded, less once it is open.</summary>
    public static readonly IValueConverter MoreOrLess =
        new FuncValueConverter<bool, string>(open => open ? Strings.GamePage_ShowLess : Strings.GamePage_ShowMore);

    /// <summary>A support link's label from its platform's name.</summary>
    public static readonly IValueConverter SupportText =
        new FuncValueConverter<string, string>(platform => Text.Format(Strings.GamePage_Support, ("platform", platform ?? "")));

    // Narrower than this, the story's column would be too thin beside the creators' card.
    private const double StackedWidth = 760;

    /// <summary>Creates the view.</summary>
    public ProjectStoryView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Creators.Classes.Set("stacked", e.NewSize.Width < StackedWidth);
    }
}
