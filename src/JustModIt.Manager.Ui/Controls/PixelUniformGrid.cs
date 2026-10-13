using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A uniform grid whose cells start and end on whole device pixels at any display scale, so their borders stay crisp and
/// the last column ends exactly where the grid does.
/// </summary>
public sealed class PixelUniformGrid : UniformGrid
{
    // The stock grid rounds the cell size once and steps by it, so the rounding error adds up column by column and the last
    // cell spills past the grid. Rounding each edge where it falls keeps every cell within a pixel of its share instead.
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (rows, columns, first) = Shape();
        if (rows == 0 || columns == 0)
        {
            return finalSize;
        }

        var (columnSpacing, rowSpacing) = (ColumnSpacing, RowSpacing);
        var width = Math.Max((finalSize.Width - (columns - 1) * columnSpacing) / columns, 0);
        var height = Math.Max((finalSize.Height - (rows - 1) * rowSpacing) / rows, 0);
        var scale = LayoutHelper.GetLayoutScale(this);
        double Snap(double value) => UseLayoutRounding ? LayoutHelper.RoundLayoutValue(value, scale) : value;

        var cell = first;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            var left = cell % columns * (width + columnSpacing);
            var top = cell / columns * (height + rowSpacing);
            var (x, y) = (Snap(left), Snap(top));
            child.Arrange(new Rect(x, y, Snap(left + width) - x, Snap(top + height) - y));
            cell++;
        }

        return finalSize;
    }

    // The rows and columns the stock grid measured with: a count left at zero follows from the visible cells.
    private (int Rows, int Columns, int First) Shape()
    {
        var first = FirstColumn < Columns ? FirstColumn : 0;
        var cells = first + Children.Count(child => child.IsVisible);
        var (rows, columns) = (Rows, Columns);
        if (rows == 0)
        {
            if (columns == 0)
            {
                rows = columns = (int)Math.Ceiling(Math.Sqrt(cells));
            }
            else
            {
                rows = (cells + columns - 1) / columns;
            }
        }
        else if (columns == 0)
        {
            columns = (cells + rows - 1) / rows;
        }

        return (rows, columns, first);
    }
}
