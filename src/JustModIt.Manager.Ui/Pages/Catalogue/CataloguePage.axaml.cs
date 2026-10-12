using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private TopLevel? _top;

    /// <summary>Creates the view.</summary>
    public CataloguePage()
    {
        InitializeComponent();
        PageContent.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Scroller.ScrollChanged += (_, _) => LoadMoreNearEnd();
        // Before the list's own arrow handling, which looks for the next row and may recycle the focused one meanwhile.
        GameGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        // The window's Escape, which closes dialogs, marks the key handled on its way down, so the box listens to it anyway.
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _top?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _top = null;
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

    // Ctrl+F reaches the search wherever the focus is, as long as this page is the one shown.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && IsEffectivelyVisible && SearchBox.IsEffectivelyVisible)
        {
            SearchBox.Focus(NavigationMethod.Tab);
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Page is { Query.Length: > 0 } page)
        {
            page.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Arrows move between the cards as they lie in the grid.
    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (Page is { } page && e.Source is Control { DataContext: CatalogueCardViewModel card })
        {
            e.Handled = page.Grid.MoveFocus(GameGrid, card, e.Key);
        }
    }
}
