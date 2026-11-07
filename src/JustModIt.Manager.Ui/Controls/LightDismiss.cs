using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>Closes a part of a page that stands open over it, as a popup would close: a click outside it, or Escape.</summary>
public static class LightDismiss
{
    /// <summary>Makes a page close the part on a click outside it, or on Escape.</summary>
    /// <param name="page">The page.</param>
    /// <param name="part">The part that stands open.</param>
    /// <param name="opener">The control that opens it, which handles its own click.</param>
    /// <param name="isOpen">Tells whether the part is open.</param>
    /// <param name="close">Closes it.</param>
    public static void Connect(Control page, Visual part, Visual opener, Func<bool> isOpen, Action close)
    {
        page.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (isOpen() && e.Source is Visual source && !part.IsVisualAncestorOf(source) && !opener.IsVisualAncestorOf(source) && source != opener)
            {
                close();
            }
        }, RoutingStrategies.Tunnel);
        page.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && isOpen())
            {
                close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }
}
