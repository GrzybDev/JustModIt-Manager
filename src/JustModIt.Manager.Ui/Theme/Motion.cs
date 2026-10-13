using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace JustModIt.Manager.Ui.Theme;

/// <summary>
/// The window's motion: whether it is reduced, and the entrance of content. <see cref="StaggerProperty"/> on a panel or
/// an items control brings its children in one after another; with reduced motion they only fade in, briefly.
/// </summary>
public static class Motion
{
    /// <summary>The children of a panel, or the items of an items control, enter one after another.</summary>
    public static readonly AttachedProperty<bool> StaggerProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Stagger", typeof(Motion));

    /// <summary>The control fades and rises in when it appears.</summary>
    public static readonly AttachedProperty<bool> EnterProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enter", typeof(Motion));

    /// <summary>
    /// Looping animations may run here: the window can be seen and motion is not reduced. The window sets it, and every
    /// control in it inherits it, as moving pictures that pause with it do.
    /// </summary>
    public static readonly AttachedProperty<bool> LoopsProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Loops", typeof(Motion), defaultValue: true, inherits: true);

    /// <summary>
    /// Whether a part of the window that its style fades in and out is shown. Faded out, it is no longer visible either,
    /// since a part covered or at no opacity is still drawn, and so are its loops, frame after frame.
    /// </summary>
    public static readonly AttachedProperty<bool> ShownProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Shown", typeof(Motion), defaultValue: true);

    /// <summary>The time between two staggered entrances.</summary>
    public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(40);

    /// <summary>How long one entrance takes.</summary>
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(320);

    private const int MaxStaggered = 12;
    private const double EnterOffset = 12;

    // Items realised this soon after the first of a burst (the list filling) enter staggered; items realised while the
    // list scrolls appear at once, and only a pause this long starts a new burst.
    private static readonly TimeSpan BurstWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BurstPause = TimeSpan.FromSeconds(1);

    private static readonly AttachedProperty<Burst?> BurstProperty =
        AvaloniaProperty.RegisterAttached<Control, Burst?>("Burst", typeof(Motion));

    static Motion()
    {
        StaggerProperty.Changed.AddClassHandler<Control>(OnStaggerChanged);
        EnterProperty.Changed.AddClassHandler<Control>(OnEnterChanged);
        ShownProperty.Changed.AddClassHandler<Control>(OnShownChanged);
        Animation.RegisterCustomAnimator<ITransform, TransformOperationsAnimator>();
    }

    /// <summary>Raised when <see cref="IsReduced"/> changes.</summary>
    public static event EventHandler? ReducedChanged;

    /// <summary>The system asks for reduced motion: transitions turn into short fades and loops stop.</summary>
    public static bool IsReduced { get; private set; }

    /// <summary>Reads whether a control's children enter one after another.</summary>
    /// <param name="control">A panel or an items control.</param>
    public static bool GetStagger(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(StaggerProperty);
    }

    /// <summary>Sets whether a control's children enter one after another.</summary>
    /// <param name="control">A panel or an items control.</param>
    /// <param name="value">True to stagger.</param>
    public static void SetStagger(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(StaggerProperty, value);
    }

    /// <summary>Reads whether a control enters with a fade and a rise.</summary>
    /// <param name="control">The control.</param>
    public static bool GetEnter(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(EnterProperty);
    }

    /// <summary>Sets whether a control enters with a fade and a rise.</summary>
    /// <param name="control">The control.</param>
    /// <param name="value">True to animate its entrance.</param>
    public static void SetEnter(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(EnterProperty, value);
    }

    /// <summary>Reads whether looping animations may run in a part of the window.</summary>
    /// <param name="control">The control, which inherits it from the window.</param>
    public static bool GetLoops(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(LoopsProperty);
    }

    /// <summary>Sets whether looping animations may run in a part of the window.</summary>
    /// <param name="control">The window.</param>
    /// <param name="value">True while the window can be seen and motion is not reduced.</param>
    public static void SetLoops(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(LoopsProperty, value);
    }

    /// <summary>Reads whether a part of the window that fades in and out is shown.</summary>
    /// <param name="control">The part.</param>
    public static bool GetShown(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ShownProperty);
    }

    /// <summary>Shows a part of the window that fades in and out at once, or lets it go once it has faded out.</summary>
    /// <param name="control">The part.</param>
    /// <param name="value">True to show it.</param>
    public static void SetShown(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ShownProperty, value);
    }

    /// <summary>Follows the system's wish for reduced motion; the window calls it when it starts and comes to the front.</summary>
    /// <param name="reduced">Whether motion is reduced from now on.</param>
    public static void SetReduced(bool reduced)
    {
        if (IsReduced != reduced)
        {
            IsReduced = reduced;
            ReducedChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Plays a control's entrance.</summary>
    /// <param name="control">The control that appears.</param>
    /// <param name="delay">How long it waits, invisible, before it enters.</param>
    public static void Enter(Control control, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(control);
        var reduced = IsReduced;
        var animation = new Animation
        {
            Duration = reduced ? TimeSpan.FromMilliseconds(120) : EnterDuration,
            Delay = reduced ? TimeSpan.Zero : delay,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Backward,
            Children =
            {
                Frame(0, 0, reduced ? 0 : EnterOffset),
                Frame(1, 1, 0),
            },
        };
        _ = animation.RunAsync(control);
    }

    /// <summary>Fades a control in from nothing, as an image that has arrived.</summary>
    /// <param name="control">The control that appears.</param>
    public static void FadeIn(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(IsReduced ? 120 : 260),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };
        _ = animation.RunAsync(control);
    }

    /// <summary>Fades a control in anew, as when what it shows has changed.</summary>
    /// <param name="control">The control whose content changed.</param>
    public static void Refresh(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(IsReduced ? 120 : 220),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.15) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };
        _ = animation.RunAsync(control);
    }

    private static KeyFrame Frame(double cue, double opacity, double offset) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(Visual.OpacityProperty, opacity),
            new Setter(Visual.RenderTransformProperty, TransformOperations.Parse(FormattableString.Invariant($"translateY({offset}px)"))),
        },
    };

    private static void OnShownChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        control.PropertyChanged -= OnFading;
        if (change.NewValue is true)
        {
            control.IsVisible = true;
        }
        else
        {
            control.PropertyChanged += OnFading;
            HideFaded(control);
        }
    }

    private static void OnFading(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.OpacityProperty && sender is Control control)
        {
            HideFaded(control);
        }
    }

    private static void HideFaded(Control control)
    {
        if (control.Opacity <= 0)
        {
            control.PropertyChanged -= OnFading;
            control.IsVisible = false;
        }
    }

    private static void OnEnterChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is true)
        {
            control.AttachedToVisualTree += (_, _) => Enter(control, TimeSpan.Zero);
        }
    }

    private static void OnStaggerChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is not true)
        {
            return;
        }

        if (control is ItemsControl items)
        {
            items.ContainerPrepared += OnContainerPrepared;
        }
        else if (control is Panel panel)
        {
            panel.AttachedToVisualTree += (_, _) =>
            {
                for (var index = 0; index < panel.Children.Count; index++)
                {
                    Enter(panel.Children[index], StaggerStep * Math.Min(index, MaxStaggered));
                }
            };
        }
    }

    private static void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (sender is not ItemsControl items)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var burst = items.GetValue(BurstProperty);
        if (burst is null || now - burst.Last > BurstPause)
        {
            burst = new Burst(now);
            items.SetValue(BurstProperty, burst);
        }

        burst.Last = now;
        if (now - burst.Start <= BurstWindow)
        {
            Enter(e.Container, StaggerStep * Math.Min(burst.Count++, MaxStaggered));
        }
    }

    private sealed class Burst(DateTime start)
    {
        public DateTime Start { get; } = start;

        public DateTime Last { get; set; } = start;

        public int Count { get; set; }
    }
}

/// <summary>
/// Lets animations key <see cref="Visual.RenderTransform"/> with values such as <c>translateY(12px)</c>, which
/// <see cref="TransformOperationsTransition"/> already interpolates for transitions.
/// </summary>
internal sealed class TransformOperationsAnimator : InterpolatingAnimator<ITransform>
{
    public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue) =>
        TransformOperations.Interpolate(oldValue as TransformOperations ?? TransformOperations.Identity,
            newValue as TransformOperations ?? TransformOperations.Identity, progress);
}
