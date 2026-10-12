using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
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
        LayOut(force: false);
    }

    /// <summary>Lays the cards out in a new number of columns.</summary>
    /// <param name="columns">How many cards fit in a row.</param>
    /// <returns>Whether the count changed.</returns>
    public bool SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == Columns)
        {
            return false;
        }

        Columns = columns;
        LayOut(force: true);
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
        ArgumentNullException.ThrowIfNull(list);
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

    /// <summary>Makes a collection hold the items in order, keeping those that stay.</summary>
    /// <param name="target">The collection a view shows.</param>
    /// <param name="desired">The items it should hold.</param>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var at = target.IndexOf(item);
            if (at < 0)
            {
                target.Insert(index, item);
            }
            else if (at != index)
            {
                target.Move(at, index);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void LayOut(bool force)
    {
        var chunks = _items.Chunk(Columns).ToList();
        if (force || Rows.Count > 0 && Rows[0].Columns != Columns)
        {
            Rows.Clear();
        }

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
