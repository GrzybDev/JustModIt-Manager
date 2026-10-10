using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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
            page.Columns = Math.Max(2, (int)((width + Gap) / (CardMinWidth + Gap)));
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

    // Arrows move between the cards as they lie in the grid, bringing a row that is not realised yet into view first.
    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (Page is not { } page || e.Source is not Control source || source.DataContext is not { } item)
        {
            return;
        }

        var items = page.GridItems;
        var index = Enumerable.Range(0, items.Count).FirstOrDefault(at => items[at] == item, -1);
        var step = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -page.Columns,
            Key.Down => page.Columns,
            _ => 0,
        };
        var target = index + step;
        if (index < 0 || step == 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        var row = target / page.Columns;
        GameGrid.ScrollIntoView(row);
        GameGrid.UpdateLayout();
        if (GameGrid.ContainerFromIndex(row) is { } container
            && container.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault()?.ContainerFromIndex(target % page.Columns) is { } cell
            && cell.GetVisualDescendants().OfType<Button>().FirstOrDefault() is { } button)
        {
            button.Focus(NavigationMethod.Directional);
            button.BringIntoView();
            e.Handled = true;
        }
    }
}
