using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A uniform grid whose cells start and end on whole device pixels at any display scale, so their borders stay crisp and
/// the last column ends exactly where the grid does. It lays the cells out in rows of <see cref="UniformGrid.Columns"/>, or
/// all in one row while that is unset, and ignores <see cref="UniformGrid.FirstColumn"/>.
/// </summary>
public sealed class PixelUniformGrid : UniformGrid
{
    // The stock grid rounds the cell size once and steps by it, so the rounding error adds up column by column and the last
    // cell spills past the grid. Rounding each edge where it falls keeps every cell within a pixel of its share instead.
    protected override Size ArrangeOverride(Size finalSize)
    {
        var cells = Children.Count(child => child.IsVisible);
        var columns = Columns > 0 ? Columns : cells;
        var rows = Rows > 0 ? Rows : (cells + columns - 1) / columns;
        if (rows == 0 || columns == 0)
        {
            return finalSize;
        }

        var (columnSpacing, rowSpacing) = (ColumnSpacing, RowSpacing);
        var width = Math.Max((finalSize.Width - (columns - 1) * columnSpacing) / columns, 0);
        var height = Math.Max((finalSize.Height - (rows - 1) * rowSpacing) / rows, 0);
        var scale = LayoutHelper.GetLayoutScale(this);
        double Snap(double value) => UseLayoutRounding ? LayoutHelper.RoundLayoutValue(value, scale) : value;

        var cell = 0;
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
}
