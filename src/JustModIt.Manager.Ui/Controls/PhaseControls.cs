using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A control drawn from a phase that a looping style animation moves from 0 to 1 (Theme/Motion.axaml), so the loop runs
/// only while the window lets loops run and costs one redraw of this control per frame.
/// </summary>
public abstract class PhaseControl : Control
{
    public static readonly StyledProperty<double> PhaseProperty = AvaloniaProperty.Register<PhaseControl, double>(nameof(Phase));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<PhaseControl, IBrush?>(nameof(Foreground));

    static PhaseControl() => AffectsRender<PhaseControl>(PhaseProperty, ForegroundProperty);

    public double Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }
}

/// <summary>Stands in for content that is still loading: a block that a soft highlight sweeps across.</summary>
public sealed class Skeleton : PhaseControl
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<Skeleton, IBrush?>(nameof(Background));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<Skeleton, CornerRadius>(nameof(CornerRadius), new CornerRadius(6));

    private const double BandShare = 0.6;

    private static readonly IBrush Highlight = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.5),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
        },
    }.ToImmutable();

    static Skeleton() => AffectsRender<Skeleton>(BackgroundProperty, CornerRadiusProperty);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var shape = new RoundedRect(bounds, CornerRadius);
        context.DrawRectangle(Background, null, shape);
        if (Phase is <= 0 or >= 1 || bounds.Width <= 0)
        {
            return;
        }

        var band = bounds.Width * BandShare;
        var left = (-band) + (Phase * (bounds.Width + band));
        using (context.PushClip(shape))
        {
            context.DrawRectangle(Highlight, null, new Rect(left, 0, band, bounds.Height));
        }
    }
}

/// <summary>The moving segment of an indeterminate progress bar: a third of the track crossing it once per phase.</summary>
public sealed class ProgressStripe : PhaseControl
{
    private const double SegmentShare = 0.3;

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width * SegmentShare;
        var left = (-width) + (Phase * (Bounds.Width + width));
        var height = Bounds.Height;
        context.DrawRectangle(Foreground, null, new RoundedRect(new Rect(left, 0, width, height), height / 2));
    }
}

/// <summary>A round progress indicator: an arc for a known share, or a turning quarter while the share is unknown.</summary>
public sealed class ProgressRing : PhaseControl
{
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<ProgressRing, double?>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 3);

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Track));

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, ThicknessProperty, TrackProperty);
        WidthProperty.OverrideDefaultValue<ProgressRing>(20);
        HeightProperty.OverrideDefaultValue<ProgressRing>(20);
    }

    /// <summary>From 0 to 1; null while the share is unknown.</summary>
    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var radius = (size - Thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Track is { } track)
        {
            context.DrawEllipse(null, new Pen(track, Thickness), center, radius, radius);
        }

        var (start, sweep) = Value is { } value ? (-90.0, Math.Clamp(value, 0, 1) * 360) : ((Phase * 360) - 90, 90.0);
        if (sweep <= 0 || Foreground is not { } brush)
        {
            return;
        }

        var pen = new Pen(brush, Thickness, lineCap: PenLineCap.Round);
        if (sweep >= 360)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        context.DrawGeometry(null, pen, Arc(center, radius, start, sweep));
    }

    private static StreamGeometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        static Point At(Point center, double radius, double degrees) =>
            new(center.X + (radius * Math.Cos(degrees * Math.PI / 180)), center.Y + (radius * Math.Sin(degrees * Math.PI / 180)));

        var geometry = new StreamGeometry();
        using var arc = geometry.Open();
        arc.BeginFigure(At(center, radius, startDegrees), isFilled: false);
        arc.ArcTo(At(center, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180,
            SweepDirection.Clockwise);
        arc.EndFigure(isClosed: false);
        return geometry;
    }
}
