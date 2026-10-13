using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The view of <see cref="ReviewStepViewModel"/>, where a step of an advanced project is dragged by its grip to another place.</summary>
public sealed partial class ReviewStepView : UserControl
{
    public static readonly IValueConverter IsDanger = new FuncValueConverter<RowTone, bool>(tone => tone == RowTone.Danger);

    public static readonly IValueConverter IsWarning = new FuncValueConverter<RowTone, bool>(tone => tone == RowTone.Warning);

    public static readonly IValueConverter IsSuccess = new FuncValueConverter<RowTone, bool>(tone => tone == RowTone.Success);

    /// <summary>A delete step has one path, which takes the room of both.</summary>
    public static readonly IValueConverter SpanWhenAlone = new FuncValueConverter<bool, int>(hasSecond => hasSecond ? 1 : 3);

    private StepRow? _dragged;

    /// <summary>Creates the view.</summary>
    public ReviewStepView()
    {
        InitializeComponent();
        StepRows.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        StepRows.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        StepRows.AddHandler(PointerCaptureLostEvent, (_, _) => _dragged = null, RoutingStrategies.Bubble);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Border>(includeSelf: true) is { } grip && grip.Classes.Contains("grip")
            && grip.DataContext is StepRow row && e.GetCurrentPoint(StepRows).Properties.IsLeftButtonPressed)
        {
            _dragged = row;
            e.Pointer.Capture(StepRows);
            e.Handled = true;
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is { } row && DataContext is ReviewStepViewModel review && TargetAt(e.GetPosition(StepRows).Y) is { } target)
        {
            review.MoveStep(row.Index, target);
        }

        _dragged = null;
    }

    // Only the rows in view have containers, which is enough, since the pointer is over one of them.
    private int? TargetAt(double y)
    {
        var rows = StepRows.GetRealizedContainers().Select(container => (Index: StepRows.IndexFromContainer(container), container.Bounds))
            .Where(row => row.Index >= 0).OrderBy(row => row.Index).ToList();
        foreach (var (index, bounds) in rows)
        {
            if (y < bounds.Top + (bounds.Height / 2))
            {
                return _dragged is { } row && index > row.Index ? index - 1 : index;
            }
        }

        return rows.Count > 0 ? rows[^1].Index : null;
    }
}
