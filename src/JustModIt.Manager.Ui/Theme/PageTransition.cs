using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace JustModIt.Manager.Ui.Theme;

/// <summary>
/// Changes pages and steps: the old one fades out quickly while the new one fades in and slides into place, rising for a
/// page opened further and settling down for one gone back to; with reduced motion the two only cross-fade, briefly.
/// </summary>
public sealed class PageTransition : IPageTransition
{
    /// <summary>How long the new page takes to arrive; the old one leaves in half the time.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(280);

    /// <summary>How far the new page travels, in pixels.</summary>
    public double Offset { get; set; } = 16;

    /// <summary>Slides sideways instead of vertically, as the steps of a wizard do.</summary>
    public bool IsHorizontal { get; set; }

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var reduced = Motion.IsReduced;
        var tasks = new List<Task>(2);
        if (from is not null)
        {
            tasks.Add(Animate(reduced ? TimeSpan.FromMilliseconds(120) : Duration / 2, new CubicEaseIn(), (1, 0), (0, 0))
                .RunAsync(from, cancellationToken));
        }

        if (to is not null)
        {
            to.IsVisible = true;
            var offset = reduced ? 0 : forward ? Offset : -Offset;
            tasks.Add(Animate(reduced ? TimeSpan.FromMilliseconds(120) : Duration, new CubicEaseOut(), (0, offset), (1, 0))
                .RunAsync(to, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
        if (from is not null && !cancellationToken.IsCancellationRequested)
        {
            from.IsVisible = false;
        }
    }

    private Animation Animate(TimeSpan duration, Easing easing, (double Opacity, double Offset) start,
        (double Opacity, double Offset) end) => new()
    {
        Duration = duration,
        Easing = easing,
        FillMode = FillMode.Forward,
        Children = { Frame(0, start), Frame(1, end) },
    };

    private KeyFrame Frame(double cue, (double Opacity, double Offset) value) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(Visual.OpacityProperty, value.Opacity),
            new Setter(Visual.RenderTransformProperty, TransformOperations.Parse(
                FormattableString.Invariant($"{(IsHorizontal ? "translateX" : "translateY")}({value.Offset}px)"))),
        },
    };
}
