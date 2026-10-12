using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A row of a settings card: an icon in a square, a title with a line under it and, beside them, the row's control, such
/// as a switch. With <c>Classes="stacked"</c> the control goes under the texts across the row, e.g. a row of choice tiles;
/// <c>Classes="divided"</c> draws the line above a row that follows another.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<SettingRow, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

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
