using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>A text whose parts that a search matched, ignoring case and diacritics as the search does, are marked.</summary>
public sealed class MarkedText : TextBlock
{
    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<MarkedText, string?>(nameof(Value));

    /// <summary>The player's search; empty marks nothing.</summary>
    public static readonly StyledProperty<string?> QueryProperty = AvaloniaProperty.Register<MarkedText, string?>(nameof(Query));

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Query
    {
        get => GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == QueryProperty)
        {
            Mark();
        }
    }

    private void Mark()
    {
        var text = Value ?? "";
        var parts = SearchText.Find(text, Query ?? "");
        if (parts.Count == 0)
        {
            Inlines = null;
            Text = text;
            return;
        }

        var accent = this.TryFindResource("AccentColor", ActualThemeVariant, out var found) && found is Color color ? color : Colors.Red;
        var mark = new ImmutableSolidColorBrush(accent, 0.3);
        var inlines = new InlineCollection();
        var at = 0;
        foreach (var (start, length) in parts)
        {
            if (start > at)
            {
                inlines.Add(new Run(text[at..start]));
            }

            inlines.Add(new Run(text.Substring(start, length)) { Background = mark, Foreground = Brushes.White });
            at = start + length;
        }

        if (at < text.Length)
        {
            inlines.Add(new Run(text[at..]));
        }

        Inlines = inlines;
    }
}
