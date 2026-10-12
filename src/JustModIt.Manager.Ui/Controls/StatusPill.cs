using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A project's status on the site: a dot and its name in a pill tinted with the site's own colour for it, which is data
/// and never written into the app. Classes <c>seam</c> (on the edge of a cover) and <c>onArt</c> (on artwork) change its
/// backdrop.
/// </summary>
public sealed class StatusPill : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<StatusPill, string?>(nameof(Text));

    /// <summary>The site's colour as <c>#rrggbb</c>; anything unreadable shows as grey.</summary>
    public static readonly StyledProperty<string?> HexProperty = AvaloniaProperty.Register<StatusPill, string?>(nameof(Hex));

    public static readonly DirectProperty<StatusPill, IBrush> TintProperty =
        AvaloniaProperty.RegisterDirect<StatusPill, IBrush>(nameof(Tint), pill => pill.Tint);

    public static readonly DirectProperty<StatusPill, IBrush> StrongTintProperty =
        AvaloniaProperty.RegisterDirect<StatusPill, IBrush>(nameof(StrongTint), pill => pill.StrongTint);

    public static readonly DirectProperty<StatusPill, IBrush> LabelProperty =
        AvaloniaProperty.RegisterDirect<StatusPill, IBrush>(nameof(Label), pill => pill.Label);

    public static readonly DirectProperty<StatusPill, IBrush> EdgeProperty =
        AvaloniaProperty.RegisterDirect<StatusPill, IBrush>(nameof(Edge), pill => pill.Edge);

    private IBrush _tint = Brushes.Transparent;
    private IBrush _strongTint = Brushes.Transparent;
    private IBrush _label = Brushes.Gray;
    private IBrush _edge = Brushes.Transparent;

    /// <summary>Creates a pill, grey until it is given the site's colour.</summary>
    public StatusPill() => Update();

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Hex
    {
        get => GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    /// <summary>The site's colour at 15%, the pill's fill on a surface.</summary>
    public IBrush Tint
    {
        get => _tint;
        private set => SetAndRaise(TintProperty, ref _tint, value);
    }

    /// <summary>The site's colour at 22%, the pill's fill on a cover's edge or on artwork.</summary>
    public IBrush StrongTint
    {
        get => _strongTint;
        private set => SetAndRaise(StrongTintProperty, ref _strongTint, value);
    }

    /// <summary>The text and dot colour, readable on the fill.</summary>
    public IBrush Label
    {
        get => _label;
        private set => SetAndRaise(LabelProperty, ref _label, value);
    }

    /// <summary>The label colour at 40%, the edge of a pill on a cover.</summary>
    public IBrush Edge
    {
        get => _edge;
        private set => SetAndRaise(EdgeProperty, ref _edge, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HexProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var color = StatusColors.Parse(Hex);
        var label = StatusColors.Label(color);
        Tint = new ImmutableSolidColorBrush(color, 0.15);
        StrongTint = new ImmutableSolidColorBrush(color, 0.22);
        Label = new ImmutableSolidColorBrush(label);
        Edge = new ImmutableSolidColorBrush(label, 0.4);
    }
}

/// <summary>A filter by a project's status: a toggle tinted with the site's own colour for it, stronger and edged once chosen.</summary>
public sealed class StatusFilterPill : ToggleButton
{
    /// <summary>The site's colour as <c>#rrggbb</c>; anything unreadable shows as grey.</summary>
    public static readonly StyledProperty<string?> HexProperty = AvaloniaProperty.Register<StatusFilterPill, string?>(nameof(Hex));

    public static readonly DirectProperty<StatusFilterPill, IBrush> TintProperty =
        AvaloniaProperty.RegisterDirect<StatusFilterPill, IBrush>(nameof(Tint), pill => pill.Tint);

    public static readonly DirectProperty<StatusFilterPill, IBrush> StrongTintProperty =
        AvaloniaProperty.RegisterDirect<StatusFilterPill, IBrush>(nameof(StrongTint), pill => pill.StrongTint);

    public static readonly DirectProperty<StatusFilterPill, IBrush> LabelProperty =
        AvaloniaProperty.RegisterDirect<StatusFilterPill, IBrush>(nameof(Label), pill => pill.Label);

    public static readonly DirectProperty<StatusFilterPill, IBrush> EdgeProperty =
        AvaloniaProperty.RegisterDirect<StatusFilterPill, IBrush>(nameof(Edge), pill => pill.Edge);

    private IBrush _tint = Brushes.Transparent;
    private IBrush _strongTint = Brushes.Transparent;
    private IBrush _label = Brushes.Gray;
    private IBrush _edge = Brushes.Transparent;

    /// <summary>Creates a pill, grey until it is given the site's colour.</summary>
    public StatusFilterPill() => Update();

    public string? Hex
    {
        get => GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    /// <summary>The site's colour at 13%, the fill of a pill not chosen.</summary>
    public IBrush Tint
    {
        get => _tint;
        private set => SetAndRaise(TintProperty, ref _tint, value);
    }

    /// <summary>The site's colour at 26%, the fill of the chosen pill.</summary>
    public IBrush StrongTint
    {
        get => _strongTint;
        private set => SetAndRaise(StrongTintProperty, ref _strongTint, value);
    }

    /// <summary>The text and dot colour, readable on the fill.</summary>
    public IBrush Label
    {
        get => _label;
        private set => SetAndRaise(LabelProperty, ref _label, value);
    }

    /// <summary>The site's colour at 70%, the edge of the chosen pill.</summary>
    public IBrush Edge
    {
        get => _edge;
        private set => SetAndRaise(EdgeProperty, ref _edge, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HexProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var color = StatusColors.Parse(Hex);
        Tint = new ImmutableSolidColorBrush(color, 0.13);
        StrongTint = new ImmutableSolidColorBrush(color, 0.26);
        Label = new ImmutableSolidColorBrush(StatusColors.Label(color));
        Edge = new ImmutableSolidColorBrush(color, 0.7);
    }
}

/// <summary>How a status colour from the site becomes readable on the window's dark surfaces, as the website does it.</summary>
public static class StatusColors
{
    /// <summary>What an unreadable colour shows as.</summary>
    public static readonly Color Fallback = Color.FromRgb(0x71, 0x71, 0x7A);

    private const double MinimumContrast = 4.5;

    private static readonly Color Surface = Color.FromRgb(0x0F, 0x16, 0x26);

    /// <summary>Reads a <c>#rrggbb</c> colour.</summary>
    /// <param name="hex">The site's colour; empty or malformed gives <see cref="Fallback"/>.</param>
    public static Color Parse(string? hex) =>
        hex is ['#', .. var digits] && digits.Length == 6
            && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromUInt32(0xFF000000 | rgb)
            : Fallback;

    /// <summary>
    /// The colour mixed half with white, and whiter in steps of 1/50 until it reads at 4.5:1 on the pill's fill on a surface.
    /// </summary>
    /// <param name="color">The status's base colour.</param>
    public static Color Label(Color color)
    {
        // The website's boards give the completed status a hand-picked label, which this keeps.
        if (color == Color.FromRgb(0x22, 0xC5, 0x5E))
        {
            return Color.FromRgb(0x4A, 0xDE, 0x80);
        }

        var fill = Mix(Surface, color, 0.15);
        var white = 0.5;
        var label = Mix(color, Colors.White, white);
        while (Contrast(label, fill) < MinimumContrast && white < 1)
        {
            white = Math.Min(1, white + (1.0 / 50));
            label = Mix(color, Colors.White, white);
        }

        return label;
    }

    /// <summary>The WCAG contrast ratio of two opaque colours.</summary>
    /// <param name="first">One colour.</param>
    /// <param name="second">The other colour.</param>
    public static double Contrast(Color first, Color second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Mixes two colours.</summary>
    /// <param name="from">The colour at a share of 0.</param>
    /// <param name="to">The colour at a share of 1.</param>
    /// <param name="share">How much of <paramref name="to"/> goes in.</param>
    public static Color Mix(Color from, Color to, double share) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * share)),
        (byte)Math.Round(from.G + ((to.G - from.G) * share)),
        (byte)Math.Round(from.B + ((to.B - from.B) * share)));

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
