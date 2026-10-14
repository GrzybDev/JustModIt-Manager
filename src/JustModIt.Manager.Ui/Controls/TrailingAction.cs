using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A text that wraps, with a control such as a link right after its last word behind a separator, or at the start of a line
/// of its own when the last line has no room left for it.
/// </summary>
public sealed class TrailingAction : Panel
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<TrailingAction, string?>(nameof(Text));

    /// <summary>The control after the text, kept whole on one line.</summary>
    public static readonly StyledProperty<Control?> ActionProperty = AvaloniaProperty.Register<TrailingAction, Control?>(nameof(Action));

    /// <summary>The height of a line of the text, on whose last line the action is centred.</summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<TrailingAction, double>(nameof(LineHeight), 20);

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _separator = new() { Text = " · " };
    private bool _fits;

    static TrailingAction() => AffectsMeasure<TrailingAction>(TextProperty, ActionProperty, LineHeightProperty);

    /// <summary>Creates a text without an action.</summary>
    public TrailingAction()
    {
        Children.Add(_text);
        Children.Add(_separator);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Control? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _text.Text = Text;
        }
        else if (change.Property == LineHeightProperty)
        {
            _text.LineHeight = LineHeight;
            _separator.LineHeight = LineHeight;
        }
        else if (change.Property == ActionProperty)
        {
            if (change.OldValue is Control old)
            {
                Children.Remove(old);
            }

            if (change.NewValue is Control added)
            {
                Children.Add(added);
            }
        }
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _text.LineHeight = LineHeight;
        _separator.LineHeight = LineHeight;
        if (this.TryFindResource("TextMutedBrush", ActualThemeVariant, out var muted) && muted is IBrush brush)
        {
            _separator.Foreground = brush;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(availableSize);
        var text = _text.DesiredSize;
        if (Action is not { IsVisible: true } action)
        {
            _separator.Measure(default);
            return text;
        }

        _separator.Measure(Size.Infinity);
        action.Measure(Size.Infinity);
        var last = LastLineWidth();
        var trailing = _separator.DesiredSize.Width + action.DesiredSize.Width;
        _fits = last + trailing <= availableSize.Width;
        return _fits
            ? new Size(Math.Max(text.Width, last + trailing), Math.Max(text.Height, LineHeight))
            : new Size(Math.Max(text.Width, action.DesiredSize.Width), text.Height + Math.Max(LineHeight, action.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var text = _text.DesiredSize;
        _text.Arrange(new Rect(0, 0, finalSize.Width, text.Height));
        // Hidden rather than collapsed, since collapsing it here would ask for another measure.
        _separator.Opacity = _fits && Action is { IsVisible: true } ? 1 : 0;
        if (Action is not { IsVisible: true } action)
        {
            _separator.Arrange(default);
            return finalSize;
        }

        var size = action.DesiredSize;
        if (_fits)
        {
            var top = Math.Max(0, text.Height - LineHeight);
            var last = LastLineWidth();
            _separator.Arrange(new Rect(last, top, _separator.DesiredSize.Width, LineHeight));
            action.Arrange(new Rect(last + _separator.DesiredSize.Width, top + ((LineHeight - size.Height) / 2), size.Width, size.Height));
        }
        else
        {
            _separator.Arrange(default);
            action.Arrange(new Rect(0, text.Height + Math.Max(0, (LineHeight - size.Height) / 2), size.Width, size.Height));
        }

        return finalSize;
    }

    // The trailing spaces count, so the separator never runs into the last word.
    private double LastLineWidth() =>
        string.IsNullOrEmpty(Text) || _text.TextLayout.TextLines is not { Count: > 0 } lines ? 0 : lines[^1].WidthIncludingTrailingWhitespace;
}
