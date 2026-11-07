using Avalonia.Controls;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>
/// The view of <see cref="CataloguePageViewModel"/>: the grid's columns and the filters' layout from its width, the
/// keyboard's ways around, and the next page of games as the grid nears its end.
/// </summary>
public sealed partial class CataloguePage : UserControl
{
    private const double CardMinWidth = 168;
    private const double Gap = 20;

    // Narrower than this, the search takes its whole line, as the board of the smallest window shows.
    private const double CompactWidth = 760;

    // The next page is asked for this far before the grid's end, so scrolling rarely meets it.
    private const double LoadAhead = 900;

    /// <summary>Creates the view.</summary>
    public CataloguePage()
    {
        InitializeComponent();
        PageContent.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Scroller.ScrollChanged += (_, _) => LoadMoreNearEnd();
        CardGrid.ConnectKeys(this, SearchBox, GameGrid, () => Page?.Grid, () =>
        {
            var hadQuery = Page is { Query.Length: > 0 };
            if (hadQuery)
            {
                Page?.ClearSearchCommand.Execute(null);
            }

            return hadQuery;
        });
    }

    public static string Subtitle => Text.Format(Strings.Catalogue_Subtitle, ("library", Strings.Shell_Library));

    public static string OpenInLibrary => Text.Format(Strings.Catalogue_OpenInLibrary, ("library", Strings.Shell_Library));

    public static string StatusesHelpName => Strings.Catalogue_StatusesHelp;

    public static string RetryText => Strings.Library_Retry;

    private CataloguePageViewModel? Page => DataContext as CataloguePageViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Fit(PageContent.Bounds.Width);
    }

    private void Fit(double width)
    {
        if (Page is { } page && width > 0)
        {
            page.Columns = CardGrid.ColumnsFor(width, CardMinWidth, Gap);
            page.IsCompact = width < CompactWidth;
        }
    }

    private void LoadMoreNearEnd()
    {
        if (Page is { ShowsGames: true } page && Scroller.Offset.Y + Scroller.Viewport.Height >= Scroller.Extent.Height - LoadAhead)
        {
            page.LoadMore();
        }
    }
}
