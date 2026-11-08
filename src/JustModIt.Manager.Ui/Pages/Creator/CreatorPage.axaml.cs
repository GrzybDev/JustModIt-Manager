using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The view of <see cref="CreatorPageViewModel"/>: the grid's columns from its width, the arrows between its cards, and a narrow
/// window's import button that keeps only its icon.
/// </summary>
public sealed partial class CreatorPage : UserControl
{
    private const double CardMinWidth = 220;
    private const double Gap = 14;

    // Below this width the page's title would be cut beside the header's buttons.
    private const double NarrowHeader = 720;

    /// <summary>Creates the view.</summary>
    public CreatorPage()
    {
        InitializeComponent();
        Header.SizeChanged += (_, e) => (ImportButton.IsVisible, ImportIconButton.IsVisible) =
            (e.NewSize.Width >= NarrowHeader, e.NewSize.Width < NarrowHeader);
        PageContent.SizeChanged += (_, e) =>
        {
            if (Page is { } page && e.NewSize.Width > 0)
            {
                page.Columns = CardGrid.ColumnsFor(e.NewSize.Width, CardMinWidth, Gap);
            }
        };
        // Before the list's own arrow handling, which looks for the next row and may recycle the focused one meanwhile.
        CardList.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (Page is { } page && e.Source is Control { DataContext: ProjectCard card })
            {
                e.Handled = page.Grid.MoveFocus(CardList, card, e.Key);
            }
        }, RoutingStrategies.Tunnel);
    }

    private CreatorPageViewModel? Page => DataContext as CreatorPageViewModel;
}
