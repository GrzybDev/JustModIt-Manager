using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>The view of <see cref="LibraryPageViewModel"/>: the grid's columns from its width, and the keyboard's ways around.</summary>
public sealed partial class LibraryPage : UserControl
{
    private const double CardMinWidth = 168;
    private const double Gap = 24;

    private TopLevel? _top;

    /// <summary>Creates the view.</summary>
    public LibraryPage()
    {
        InitializeComponent();
        PageContent.SizeChanged += (_, e) => SetColumns(e.NewSize.Width);
        // Before the list's own arrow handling, which looks for the next row and may recycle the focused one meanwhile.
        GameGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        // The window's Escape, which closes dialogs, marks the key handled on its way down, so the box listens to it anyway.
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private LibraryPageViewModel? Page => DataContext as LibraryPageViewModel;

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

    private void SetColumns(double width)
    {
        if (Page is { } page && width > 0)
        {
            page.Columns = CardGrid.ColumnsFor(width, CardMinWidth, Gap);
        }
    }

    // Ctrl+F reaches the search wherever the focus is, as long as this page is the one shown.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && IsEffectivelyVisible && SearchBox.IsVisible)
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
            page.Query = "";
            e.Handled = true;
        }
    }

    // Arrows move between the cards as they lie in the grid.
    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (Page is { } page && e.Source is Control { DataContext: { } item })
        {
            e.Handled = page.Grid.MoveFocus(GameGrid, item, e.Key);
        }
    }
}
