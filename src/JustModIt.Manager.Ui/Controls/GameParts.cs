using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
namespace JustModIt.Manager.Ui.Controls;

/// <summary>The tone of a line about what happens on this computer, which colours its label and icon.</summary>
public enum Tone
{
    Neutral,
    Accent,
    Success,
    Info,
    Warning,
    Danger,
}

/// <summary>Gives its child a height that follows its width, such as a cover's 2:3, since layout has no aspect ratio.</summary>
public sealed class AspectBox : Decorator
{
    /// <summary>The height as a share of the width: 1.5 for a 2:3 cover.</summary>
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectBox, double>(nameof(Ratio), 1.5);

    static AspectBox() => AffectsMeasure<AspectBox>(RatioProperty);

    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var size = new Size(width, width * Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

/// <summary>
/// A line about what happens with a game: a toned icon, then a toned label, a detail after a separator or on a line of
/// its own, and a warning that closes the line; it wraps, and its parts read as one sentence.
/// </summary>
public sealed class StatusLine : Grid
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<StatusLine, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<Tone> ToneProperty = AvaloniaProperty.Register<StatusLine, Tone>(nameof(Tone));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<StatusLine, string?>(nameof(Label));

    /// <summary>Words after the label in the label's colour, e.g. the versions of an update.</summary>
    public static readonly StyledProperty<string?> LabelExtraProperty =
        AvaloniaProperty.Register<StatusLine, string?>(nameof(LabelExtra));

    public static readonly StyledProperty<string?> DetailProperty = AvaloniaProperty.Register<StatusLine, string?>(nameof(Detail));

    /// <summary>A last part in the warning colour, such as that an update waits for the connection.</summary>
    public static readonly StyledProperty<string?> WarningProperty = AvaloniaProperty.Register<StatusLine, string?>(nameof(Warning));

    /// <summary>The detail goes on a line of its own under the label instead of after a separator.</summary>
    public static readonly StyledProperty<bool> StacksDetailProperty =
        AvaloniaProperty.Register<StatusLine, bool>(nameof(StacksDetail));

    /// <summary>The icon turns, for work under way.</summary>
    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<StatusLine, bool>(nameof(IsBusy));

    /// <summary>The height of a line of the text, which also centres the icon on the first line.</summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<StatusLine, double>(nameof(LineHeight), 20);

    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<StatusLine>();

    /// <summary>The colour of the detail after the label.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<StatusLine>();

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private const string Separator = " · ";

    private readonly Icon _icon = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Top };
    // A word wider than the line, such as a long stage name in a narrow card, runs into the padding rather than breaking.
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.WrapWithOverflow };

    /// <summary>Creates an empty line.</summary>
    public StatusLine()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        ColumnSpacing = 6;
        SetColumn(_text, 1);
        Children.Add(_icon);
        Children.Add(_text);
        Rebuild();
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Tone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? LabelExtra
    {
        get => GetValue(LabelExtraProperty);
        set => SetValue(LabelExtraProperty, value);
    }

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string? Warning
    {
        get => GetValue(WarningProperty);
        set => SetValue(WarningProperty, value);
    }

    public bool StacksDetail
    {
        get => GetValue(StacksDetailProperty);
        set => SetValue(StacksDetailProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>The brush of a tone's labels, from the theme.</summary>
    /// <param name="control">A control in the window, whose resources hold the theme.</param>
    /// <param name="tone">The tone.</param>
    public static IBrush ToneBrush(Control control, Tone tone)
    {
        ArgumentNullException.ThrowIfNull(control);
        var key = tone switch
        {
            Tone.Accent => "AccentTextBrush",
            Tone.Success => "SuccessTextBrush",
            Tone.Info => "InfoTextBrush",
            Tone.Warning => "WarningTextBrush",
            Tone.Danger => "DangerTextBrush",
            _ => "NeutralTextBrush",
        };
        return control.TryFindResource(key, control.ActualThemeVariant, out var brush) && brush is IBrush found ? found : Brushes.Gray;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty || change.Property == ToneProperty || change.Property == LabelProperty
            || change.Property == LabelExtraProperty || change.Property == DetailProperty || change.Property == WarningProperty
            || change.Property == StacksDetailProperty || change.Property == IsBusyProperty || change.Property == LineHeightProperty)
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
        var tone = ToneBrush(this, Tone);
        var muted = this.TryFindResource("TextMutedBrush", ActualThemeVariant, out var mutedBrush) ? mutedBrush as IBrush : null;
        var secondary = this.TryFindResource("TextSecondaryBrush", ActualThemeVariant, out var secondaryBrush) ? secondaryBrush as IBrush : null;
        _icon.Data = Icon;
        _icon.IsVisible = Icon is not null;
        _icon.Foreground = tone;
        _icon.Classes.Set("spin", IsBusy);
        _text.LineHeight = LineHeight;
        _icon.Margin = new Thickness(0, Math.Max(0, (LineHeight - 14) / 2), 0, 0);

        var inlines = new InlineCollection();
        if (Label is { Length: > 0 } label)
        {
            inlines.Add(new Run(label) { Foreground = tone, FontWeight = FontWeight.Medium });
        }

        if (LabelExtra is { Length: > 0 } extra)
        {
            inlines.Add(new Run(" " + extra) { Foreground = tone, FontWeight = FontWeight.SemiBold });
        }

        if (Detail is { Length: > 0 } detail)
        {
            if (StacksDetail)
            {
                if (inlines.Count > 0)
                {
                    inlines.Add(new LineBreak());
                }

                inlines.Add(new Run(detail) { Foreground = secondary });
            }
            else
            {
                if (inlines.Count > 0)
                {
                    inlines.Add(new Run(Separator) { Foreground = muted });
                }

                inlines.Add(new Run(detail));
            }
        }

        if (Warning is { Length: > 0 } warning)
        {
            if (inlines.Count > 0)
            {
                inlines.Add(new Run(Separator) { Foreground = muted });
            }

            inlines.Add(new Run(warning) { Foreground = ToneBrush(this, Tone.Warning), FontWeight = FontWeight.Medium });
        }

        _text.Inlines = inlines;
    }
}

/// <summary>
/// A team's mark: its avatar from the site, or the initials of its name on a colour that its name picks, so a team looks
/// the same on every screen.
/// </summary>
public sealed class TeamBadge : Border
{
    public static readonly StyledProperty<string?> NameTextProperty = AvaloniaProperty.Register<TeamBadge, string?>(nameof(NameText));

    public static readonly StyledProperty<string?> AvatarProperty = AvaloniaProperty.Register<TeamBadge, string?>(nameof(Avatar));

    private static readonly (Color Fill, Color Text)[] Palette =
    [
        (Color.Parse("#1E2A3F"), Color.Parse("#93C5FD")),
        (Color.Parse("#1D3326"), Color.Parse("#86EFAC")),
        (Color.Parse("#2A2140"), Color.Parse("#C4B5FD")),
        (Color.Parse("#33291A"), Color.Parse("#FCD34D")),
        (Color.Parse("#173331"), Color.Parse("#5EEAD4")),
        (Color.Parse("#3A1D27"), Color.Parse("#FDA4AF")),
    ];

    private readonly TextBlock _initials = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeight.Bold,
    };

    private readonly RemoteImage _avatar = new();

    /// <summary>Creates a 16 px badge; its size sets the size of its letters.</summary>
    public TeamBadge()
    {
        Width = 16;
        Height = 16;
        CornerRadius = new CornerRadius(4);
        ClipToBounds = true;
        IsHitTestVisible = false;
        Child = new Panel { Children = { _initials, _avatar } };
        Update();
    }

    /// <summary>The team's name.</summary>
    public string? NameText
    {
        get => GetValue(NameTextProperty);
        set => SetValue(NameTextProperty, value);
    }

    /// <summary>The team's avatar from the site; empty shows the initials.</summary>
    public string? Avatar
    {
        get => GetValue(AvatarProperty);
        set => SetValue(AvatarProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NameTextProperty || change.Property == AvatarProperty || change.Property == WidthProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var name = NameText ?? "";
        var (fill, text) = Palette[(int)(StableHash(name) % (uint)Palette.Length)];
        Background = new ImmutableSolidColorBrush(fill);
        _initials.Foreground = new ImmutableSolidColorBrush(text);
        _initials.Text = Initials(name);
        _initials.FontSize = Math.Max(6.5, Width * 0.38);
        _avatar.Source = Avatar;
        _avatar.IsVisible = Avatar is { Length: > 0 };
    }

    /// <summary>The two letters a team without an avatar shows: the initials of its first two words, or its first two letters.</summary>
    /// <param name="name">The team's name.</param>
    public static string Initials(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var letters = words.Length >= 2
            ? string.Concat(words.Take(2).Select(word => word[0]))
            : new string([.. name.Where(char.IsLetterOrDigit).Take(2)]);
        return letters.ToUpperInvariant();
    }

    // string.GetHashCode differs from run to run, and a team must keep its colour.
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return hash;
    }
}
