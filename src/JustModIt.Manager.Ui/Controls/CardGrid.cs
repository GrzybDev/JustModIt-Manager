using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>A row of a grid of cards, so a virtualising list can lay the grid out row by row.</summary>
public sealed class CardRow
{
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>The grid's column count, which every row lays its items out in.</summary>
    public int Columns { get; init; }
}

/// <summary>
/// A grid of cards laid out as rows of a virtualising list, which keeps the rows and cards that stay when it changes, so
/// their views keep their state and do not enter again.
/// </summary>
public sealed class CardGrid
{
    private List<object> _items = [];

    public ObservableCollection<CardRow> Rows { get; } = [];

    /// <summary>The cards in the grid's order.</summary>
    public IReadOnlyList<object> Items => _items;

    /// <summary>How many cards fit in a row.</summary>
    public int Columns { get; private set; } = 5;

    /// <summary>The column count that fits a width, never fewer than two.</summary>
    /// <param name="width">The width the grid has.</param>
    /// <param name="cardMinWidth">The narrowest a card may be.</param>
    /// <param name="gap">The space between two cards.</param>
    public static int ColumnsFor(double width, double cardMinWidth, double gap) => Math.Max(2, (int)((width + gap) / (cardMinWidth + gap)));

    /// <summary>Shows these cards, in this order.</summary>
    /// <param name="items">The cards.</param>
    public void Show(IReadOnlyList<object> items)
    {
        _items = [.. items];
        LayOut();
    }

    /// <summary>Lays the cards out in a new number of columns.</summary>
    /// <param name="columns">How many cards fit in a row.</param>
    /// <returns>Whether the count changed.</returns>
    public bool SetColumns(int columns)
    {
        if (columns == Columns)
        {
            return false;
        }

        Columns = columns;
        Rows.Clear();
        LayOut();
        return true;
    }

    /// <summary>
    /// Moves the keyboard focus from a card to the one an arrow key points to, bringing a row that is not realised yet into
    /// view first; the focus lands on the card's first button.
    /// </summary>
    /// <param name="list">The list that shows <see cref="Rows"/>.</param>
    /// <param name="item">The card that has the focus.</param>
    /// <param name="key">The key pressed.</param>
    /// <returns>Whether the focus moved.</returns>
    public bool MoveFocus(ItemsControl list, object item, Key key)
    {
        var index = Enumerable.Range(0, _items.Count).FirstOrDefault(at => _items[at] == item, -1);
        var step = key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -Columns,
            Key.Down => Columns,
            _ => 0,
        };
        var target = index + step;
        if (index < 0 || step == 0 || target < 0 || target >= _items.Count)
        {
            return false;
        }

        // A row the list holds already is only focused, as scrolling to it first could recycle the focused card's row.
        var row = target / Columns;
        if (list.ContainerFromIndex(row) is null)
        {
            list.ScrollIntoView(row);
            list.UpdateLayout();
        }

        if (list.ContainerFromIndex(row) is not { } container
            || container.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault()?.ContainerFromIndex(target % Columns) is not { } cell
            || cell.GetVisualDescendants().OfType<Button>().FirstOrDefault() is not { } button)
        {
            return false;
        }

        button.Focus(NavigationMethod.Directional);
        button.BringIntoView();
        return true;
    }

    /// <summary>Gives a page of a grid its keyboard: Ctrl+F reaches its search, Escape empties it and the arrows move between cards.</summary>
    /// <param name="page">The page, which takes the shortcut only while it is the one shown.</param>
    /// <param name="search">The page's search box.</param>
    /// <param name="list">The list that shows <see cref="Rows"/>.</param>
    /// <param name="grid">The page's grid; null before the page has its view model.</param>
    /// <param name="clearSearch">Empties the search, and tells whether it held anything.</param>
    public static void ConnectKeys(Control page, TextBox search, ItemsControl list, Func<CardGrid?> grid, Func<bool> clearSearch)
    {
        TopLevel? top = null;
        void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && page.IsEffectivelyVisible && search.IsEffectivelyVisible)
            {
                search.Focus(NavigationMethod.Tab);
                search.SelectAll();
                e.Handled = true;
            }
        }

        page.AttachedToVisualTree += (_, _) =>
        {
            top = TopLevel.GetTopLevel(page);
            top?.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
        };
        page.DetachedFromVisualTree += (_, _) =>
        {
            top?.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
            top = null;
        };
        // Before the list's own arrow handling, which looks for the next row and may recycle the focused one meanwhile.
        list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (grid() is { } cards && e.Source is Control { DataContext: { } item })
            {
                e.Handled = cards.MoveFocus(list, item, e.Key);
            }
        }, RoutingStrategies.Tunnel);
        // The window's Escape, which closes dialogs, marks the key handled on its way down, so the box listens to it anyway.
        search.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && clearSearch())
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// Makes a collection hold the items in order, removing, moving and adding as few as it can, so an item that stays keeps
    /// its controls and their focus.
    /// </summary>
    /// <param name="target">The collection a view shows.</param>
    /// <param name="desired">The items it should hold.</param>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : notnull
    {
        // Counted, since equal items may repeat, such as two store facts alike.
        var wanted = desired.CountBy(item => item).ToDictionary();
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (wanted.GetValueOrDefault(target[index]) > 0)
            {
                wanted[target[index]]--;
            }
            else
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            if (index < target.Count && Equals(target[index], item))
            {
                continue;
            }

            var at = index + 1;
            while (at < target.Count && !Equals(target[at], item))
            {
                at++;
            }

            if (at < target.Count)
            {
                target.Move(at, index);
            }
            else
            {
                target.Insert(index, item);
            }
        }
    }

    private void LayOut()
    {
        var chunks = _items.Chunk(Columns).ToList();
        for (var index = 0; index < chunks.Count; index++)
        {
            if (index == Rows.Count)
            {
                Rows.Add(new CardRow { Columns = Columns });
            }

            Sync(Rows[index].Items, chunks[index]);
        }

        while (Rows.Count > chunks.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }
}
