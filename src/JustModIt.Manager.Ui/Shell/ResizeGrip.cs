using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>An edge or a corner of a window without the system's frame, which resizes the window when dragged.</summary>
public sealed class ResizeGrip : Border
{
    /// <summary>Defines the <see cref="Edge"/> property.</summary>
    public static readonly StyledProperty<WindowEdge> EdgeProperty = AvaloniaProperty.Register<ResizeGrip, WindowEdge>(nameof(Edge));

    /// <summary>Creates a grip that takes the pointer over its whole area.</summary>
    public ResizeGrip() => Background = Brushes.Transparent;

    /// <summary>The edge or corner of the window that the grip moves.</summary>
    public WindowEdge Edge
    {
        get => GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginResizeDrag(Edge, e);
            e.Handled = true;
        }
    }
}
