using Avalonia.Controls;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>The view of <see cref="LibraryPageViewModel"/>: the grid's columns from its width, and the keyboard's ways around.</summary>
public sealed partial class LibraryPage : UserControl
{
    private const double CardMinWidth = 168;
    private const double Gap = 24;

    /// <summary>Creates the view.</summary>
    public LibraryPage()
    {
        InitializeComponent();
        PageContent.SizeChanged += (_, e) => SetColumns(e.NewSize.Width);
        CardGrid.ConnectKeys(this, SearchBox, GameGrid, () => Page?.Grid, () =>
        {
            var hadQuery = Page is { Query.Length: > 0 };
            Page?.Query = "";
            return hadQuery;
        });
    }

    private LibraryPageViewModel? Page => DataContext as LibraryPageViewModel;

    private void SetColumns(double width)
    {
        if (Page is { } page && width > 0)
        {
            page.Columns = CardGrid.ColumnsFor(width, CardMinWidth, Gap);
        }
    }
}
