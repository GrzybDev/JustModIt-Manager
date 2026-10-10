using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// Strokes a Lucide geometry (<see cref="Icons"/>) the way Lucide draws it, with round caps and joins, scaled from its
/// 24x24 grid to the control in the inherited foreground; one draw call instead of a Path in a Viewbox per icon.
/// </summary>
public class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 2);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();

    private static readonly Size DesignSize = new(24, 24);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, StrokeThicknessProperty, ForegroundProperty);
        WidthProperty.OverrideDefaultValue<Icon>(16);
        HeightProperty.OverrideDefaultValue<Icon>(16);
        IsHitTestVisibleProperty.OverrideDefaultValue<Icon>(false);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>The stroke on the 24x24 grid: Lucide's 2, or 1.5 for the window's caption buttons.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is { } data && Foreground is { } brush)
        {
            Draw(context, data, null, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
        }
    }

    /// <summary>Draws a geometry designed on the 24x24 grid, centred and scaled to the control.</summary>
    /// <param name="context">The context to draw into.</param>
    /// <param name="geometry">The geometry on the 24x24 grid.</param>
    /// <param name="fill">The fill; null for none.</param>
    /// <param name="pen">The stroke; null for none.</param>
    protected void Draw(DrawingContext context, Geometry geometry, IBrush? fill, IPen? pen)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scale = Math.Min(Bounds.Width / DesignSize.Width, Bounds.Height / DesignSize.Height);
        var offset = new Point((Bounds.Width - (DesignSize.Width * scale)) / 2, (Bounds.Height - (DesignSize.Height * scale)) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            context.DrawGeometry(fill, pen, geometry);
        }
    }
}

/// <summary>The mark of the store or launcher a game came from (<see cref="StoreIcons"/>), or Lucide's store icon.</summary>
public sealed class StoreIcon : Icon
{
    public static readonly StyledProperty<string?> StoreProperty = AvaloniaProperty.Register<StoreIcon, string?>(nameof(Store));

    static StoreIcon() => AffectsRender<StoreIcon>(StoreProperty);

    /// <summary>A store id such as <c>steam</c>, or a library source such as <c>heroic</c>.</summary>
    public string? Store
    {
        get => GetValue(StoreProperty);
        set => SetValue(StoreProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
        {
            return;
        }

        if (StoreIcons.MarkOf(Store) is { } mark)
        {
            Draw(context, mark, brush, null);
        }
        else
        {
            Draw(context, Icons.Store, null, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
        }
    }
}
