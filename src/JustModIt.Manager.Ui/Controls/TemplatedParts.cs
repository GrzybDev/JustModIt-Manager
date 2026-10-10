using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// The inside of a choice tile: a prominent icon, a bold title and a short description, all centred. A tile is a
/// <see cref="RadioButton"/> with <c>Theme="{StaticResource ChoiceTile}"</c>, or a button with <c>Classes="tile"</c> that
/// acts at once; the selection shows through the accent alone.
/// </summary>
public sealed class TileContent : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<TileContent, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<TileContent, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<TileContent, string?>(nameof(Description));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}

/// <summary>
/// A page's or a list's empty or unavailable state: an icon in a tinted square, a title, a message and, as content, the
/// actions that lead on. <c>Classes="neutral"</c> takes the grey look of a placeholder.
/// </summary>
public sealed class EmptyState : ContentControl
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<EmptyState, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}

/// <summary>The head of a page: its title, an optional subtitle under it and, as content, the actions beside the title.</summary>
public sealed class PageHeader : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PageHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<PageHeader, string?>(nameof(Subtitle));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}

/// <summary>
/// One message in one place, under what it is about: an icon, a title and a text, tinted by its tone class
/// (<c>info</c>, <c>success</c>, <c>warning</c> or <c>danger</c>), with optional actions as content.
/// </summary>
public sealed class Callout : ContentControl
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<Callout, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<Callout, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<Callout, string?>(nameof(Message));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
