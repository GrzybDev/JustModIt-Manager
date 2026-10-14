using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// Game files in a frame, one a line in a fixed-width face with the folder dimmed and the name bright; a long path wraps
/// inside the frame rather than widening the page.
/// </summary>
public sealed class PathList : Border
{
    /// <summary>The game-relative paths, with forward slashes.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> PathsProperty =
        AvaloniaProperty.Register<PathList, IReadOnlyList<string>?>(nameof(Paths));

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");

    private readonly StackPanel _lines = new() { Spacing = 2 };

    /// <summary>Creates an empty list.</summary>
    public PathList() => Child = _lines;

    public IReadOnlyList<string>? Paths
    {
        get => GetValue(PathsProperty);
        set => SetValue(PathsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathsProperty)
        {
            Rebuild();
        }
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Rebuild();
    }

    private void Rebuild()
    {
        _lines.Children.Clear();
        var secondary = Brush("TextSecondaryBrush");
        var bright = Brush("TextBrush");
        foreach (var path in Paths ?? [])
        {
            var cut = path.LastIndexOf('/') + 1;
            var text = new TextBlock { FontFamily = Mono, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap };
            text.Inlines = [new Run(path[..cut]) { Foreground = secondary }, new Run(path[cut..]) { Foreground = bright }];
            var icon = new Icon
            {
                Data = Icons.File,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 3, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = secondary,
            };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            Grid.SetColumn(text, 1);
            line.Children.Add(icon);
            line.Children.Add(text);
            _lines.Children.Add(line);
        }
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var brush) ? brush as IBrush : null;
}
