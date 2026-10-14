using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// Opens a control's tooltip while the keyboard has brought focus to it, so a description that the pointer shows on hover
/// reaches a player who uses the keyboard too: <c>ui:ToolTipAssist.ShowOnFocus="True"</c>. Such a tooltip keeps beside its
/// control: placed to the right, it wraps within the element around marked <c>ui:ToolTipAssist.IsBoundary="True"</c>, and
/// it follows the control when the page scrolls or lays out again.
/// </summary>
public static class ToolTipAssist
{
    public static readonly AttachedProperty<bool> ShowOnFocusProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ShowOnFocus", typeof(ToolTipAssist));

    public static readonly AttachedProperty<bool> IsBoundaryProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsBoundary", typeof(ToolTipAssist));

    // Where the control stood in its window when its tooltip was last placed, while the tooltip follows it.
    private static readonly AttachedProperty<Point?> PlacedAtProperty =
        AvaloniaProperty.RegisterAttached<Control, Point?>("PlacedAt", typeof(ToolTipAssist));

    // The keyboard's focus is on the control, whose tooltip is then wanted wherever the control is in sight.
    private static readonly AttachedProperty<bool> IsFocusedByKeyboardProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsFocusedByKeyboard", typeof(ToolTipAssist));

    // Below this a tooltip may take the side it finds room on rather than wrap a word to a line.
    private const double NarrowestWidth = 160;

    static ToolTipAssist()
    {
        ShowOnFocusProperty.Changed.AddClassHandler<Control>(OnShowOnFocusChanged);
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(OnIsOpenChanged);
    }

    /// <summary>Reads whether the control's tooltip opens on keyboard focus.</summary>
    /// <param name="control">The control.</param>
    public static bool GetShowOnFocus(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ShowOnFocusProperty);
    }

    /// <summary>Sets whether the control's tooltip opens on keyboard focus.</summary>
    /// <param name="control">The control.</param>
    /// <param name="value">True to open it while the keyboard focuses the control.</param>
    public static void SetShowOnFocus(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ShowOnFocusProperty, value);
    }

    /// <summary>Reads whether the tooltips of the controls inside the element stay within it.</summary>
    /// <param name="control">The element.</param>
    public static bool GetIsBoundary(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(IsBoundaryProperty);
    }

    /// <summary>Sets whether the tooltips of the controls inside the element stay within it.</summary>
    /// <param name="control">The element.</param>
    /// <param name="value">True to keep them within its padding.</param>
    public static void SetIsBoundary(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(IsBoundaryProperty, value);
    }

    private static void OnShowOnFocusChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is true)
        {
            control.GotFocus += OnGotFocus;
            control.LostFocus += OnLostFocus;
            ToolTip.AddToolTipOpeningHandler(control, OnOpening);
        }
        else
        {
            control.GotFocus -= OnGotFocus;
            control.LostFocus -= OnLostFocus;
            ToolTip.RemoveToolTipOpeningHandler(control, OnOpening);
        }
    }

    // A click focuses the control too, and the pointer's own hover already opens the tooltip then.
    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Control control && e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional
            && ToolTip.GetTip(control) is not null)
        {
            control.SetValue(IsFocusedByKeyboardProperty, true);
            ToolTip.SetIsOpen(control, true);
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            control.ClearValue(IsFocusedByKeyboardProperty);
            ToolTip.SetIsOpen(control, false);
            StopFollowing(control);
        }
    }

    private static void OnOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is Control control)
        {
            FitBeside(control);
        }
    }

    // Opening a tooltip closes it first, so its being open, not its events, tells when to follow the control; one the
    // keyboard's focus wants is followed while closed out of sight too, so it opens again once the control is back.
    private static void OnIsOpenChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        if (!GetShowOnFocus(control))
        {
            return;
        }

        if (change.NewValue is true)
        {
            if (control.GetValue(PlacedAtProperty) is null)
            {
                control.LayoutUpdated += OnLayoutUpdated;
            }

            control.SetValue(PlacedAtProperty, PositionOf(control) ?? default);
        }
        else if (!control.GetValue(IsFocusedByKeyboardProperty))
        {
            StopFollowing(control);
        }
    }

    private static void StopFollowing(Control control)
    {
        if (control.GetValue(PlacedAtProperty) is not null)
        {
            control.LayoutUpdated -= OnLayoutUpdated;
            control.ClearValue(PlacedAtProperty);
        }
    }

    // An open tooltip stays where it opened, e.g. where the keyboard's focus found its control before scrolling it into
    // view; it moves after the layout pass rather than within it.
    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Control control && HasMoved(control))
        {
            Dispatcher.UIThread.Post(() => Follow(control));
        }
    }

    private static void Follow(Control control)
    {
        if (!HasMoved(control))
        {
            return;
        }

        var wanted = ToolTip.GetIsOpen(control) || control.GetValue(IsFocusedByKeyboardProperty);
        ToolTip.SetIsOpen(control, false);
        if (wanted && IsInSight(control))
        {
            ToolTip.SetIsOpen(control, true);
        }
        else if (control.GetValue(PlacedAtProperty) is not null)
        {
            control.SetValue(PlacedAtProperty, PositionOf(control) ?? default);
        }
    }

    private static bool HasMoved(Control control) =>
        control.GetValue(PlacedAtProperty) is { } placed && PositionOf(control) is { } now
        && (Math.Abs(now.X - placed.X) > 0.5 || Math.Abs(now.Y - placed.Y) > 0.5);

    private static Point? PositionOf(Control control) =>
        TopLevel.GetTopLevel(control) is { } window ? control.TranslatePoint(default, window) : null;

    private static bool IsInSight(Control control)
    {
        if (control.FindAncestorOfType<ScrollViewer>() is not { } scroller)
        {
            return true;
        }

        return control.TranslatePoint(default, scroller) is { } at && at.X >= 0 && at.Y >= 0
            && at.X + control.Bounds.Width <= scroller.Bounds.Width && at.Y + control.Bounds.Height <= scroller.Bounds.Height;
    }

    // A tooltip without room to its right would flip over the control's neighbours, so it wraps to the room there is.
    private static void FitBeside(Control control)
    {
        if (ToolTip.GetTip(control) is not ToolTip tip || ToolTip.GetPlacement(control) != PlacementMode.Right)
        {
            return;
        }

        tip.ClearValue(Layoutable.MaxWidthProperty);
        if (control.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetIsBoundary) is not { } boundary
            || TopLevel.GetTopLevel(control) is not { } window
            || boundary.TranslatePoint(new Point(boundary.Bounds.Width - PaddingOf(boundary), 0), window) is not { } end
            || control.TranslatePoint(new Point(control.Bounds.Width, 0), window) is not { } start)
        {
            return;
        }

        var room = end.X - start.X - ToolTip.GetHorizontalOffset(control);
        tip.MaxWidth = Math.Max(NarrowestWidth, Math.Min(tip.MaxWidth, room));
    }

    private static double PaddingOf(Control boundary) => boundary switch
    {
        Decorator decorator => decorator.Padding.Right,
        TemplatedControl templated => templated.Padding.Right,
        _ => 0,
    };
}
