using Avalonia;
using Avalonia.Controls;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Onboarding;

/// <summary>
/// A still wall of covers behind the first-run setup: rows of covers, each row shifted sideways, as many as it takes to
/// fill the wall, the covers repeating; with no covers it shows nothing.
/// </summary>
public sealed class CoverWall : Panel
{
    public static readonly StyledProperty<IReadOnlyList<string>?> CoversProperty =
        AvaloniaProperty.Register<CoverWall, IReadOnlyList<string>?>(nameof(Covers));

    private const double TileWidth = 112;
    private const double TileHeight = 168;
    private const double Gap = 14;
    private const double Top = -36;

    // Neighbouring rows start this many covers apart, so a cover never sits right next to or above itself.
    private const int RowStride = 7;

    // The rows' shifts, so that no two neighbouring rows line up.
    private static readonly double[] Shifts = [-44, -100, -16, -72, -30];

    private int _rows;
    private int _columns;

    public IReadOnlyList<string>? Covers
    {
        get => GetValue(CoversProperty);
        set => SetValue(CoversProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == CoversProperty)
        {
            Children.Clear();
            (_rows, _columns) = (0, 0);
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Covers is { Count: > 0 } covers)
        {
            var columns = (int)Math.Ceiling((Finite(availableSize.Width) - Shifts.Min()) / (TileWidth + Gap));
            var rows = (int)Math.Ceiling((Finite(availableSize.Height) - Top) / (TileHeight + Gap));
            // The wall only grows, so a window made smaller and then larger again keeps its covers where they were.
            if (columns > _columns || rows > _rows)
            {
                (_rows, _columns) = (Math.Max(rows, _rows), Math.Max(columns, _columns));
                Fill(covers);
            }
        }

        foreach (var child in Children)
        {
            child.Measure(new Size(TileWidth, TileHeight));
        }

        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < Children.Count; index++)
        {
            var (row, column) = Math.DivRem(index, _columns);
            Children[index].Arrange(new Rect(Shifts[row % Shifts.Length] + (column * (TileWidth + Gap)), Top + (row * (TileHeight + Gap)),
                TileWidth, TileHeight));
        }

        return finalSize;
    }

    private static double Finite(double length) => double.IsFinite(length) ? length : 0;

    private void Fill(IReadOnlyList<string> covers)
    {
        Children.Clear();
        for (var row = 0; row < _rows; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    ClipToBounds = true,
                    Child = new RemoteImage { Source = covers[((row * RowStride) + column) % covers.Count] },
                });
            }
        }
    }
}
