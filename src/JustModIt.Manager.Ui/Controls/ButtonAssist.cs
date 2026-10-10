using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// Gives a button with text content an icon, so the text stays its content and names it for screen readers:
/// <c>&lt;Button Content="{x:Static loc:Strings.X}" ui:ButtonAssist.Icon="{x:Static theme:Icons.Download}" /&gt;</c>.
/// </summary>
public static class ButtonAssist
{
    public static readonly AttachedProperty<Geometry?> IconProperty =
        AvaloniaProperty.RegisterAttached<Button, Geometry?>("Icon", typeof(ButtonAssist));

    /// <summary>Puts the icon after the text, as for a link that leaves the app.</summary>
    public static readonly AttachedProperty<bool> IconAfterProperty =
        AvaloniaProperty.RegisterAttached<Button, bool>("IconAfter", typeof(ButtonAssist));

    /// <summary>
    /// Set while the button has an icon, so the theme gives it the template that draws the icon beside its text; a
    /// button whose content is a control keeps the content as it is.
    /// </summary>
    public static readonly AttachedProperty<bool> HasIconProperty =
        AvaloniaProperty.RegisterAttached<Button, bool>("HasIcon", typeof(ButtonAssist));

    static ButtonAssist() =>
        IconProperty.Changed.AddClassHandler<Button>((button, change) => button.SetValue(HasIconProperty, change.NewValue is not null));

    /// <summary>Reads the icon of a button.</summary>
    /// <param name="button">The button.</param>
    public static Geometry? GetIcon(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.GetValue(IconProperty);
    }

    /// <summary>Sets the icon of a button.</summary>
    /// <param name="button">The button.</param>
    /// <param name="value">A geometry of <see cref="Theme.Icons"/>.</param>
    public static void SetIcon(Button button, Geometry? value)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(IconProperty, value);
    }

    /// <summary>Reads whether the button has an icon.</summary>
    /// <param name="button">The button.</param>
    public static bool GetHasIcon(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.GetValue(HasIconProperty);
    }

    /// <summary>Reads whether the icon follows the text.</summary>
    /// <param name="button">The button.</param>
    public static bool GetIconAfter(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.GetValue(IconAfterProperty);
    }

    /// <summary>Sets whether the icon follows the text.</summary>
    /// <param name="button">The button.</param>
    /// <param name="value">True for an icon after the text.</param>
    public static void SetIconAfter(Button button, bool value)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(IconAfterProperty, value);
    }
}
