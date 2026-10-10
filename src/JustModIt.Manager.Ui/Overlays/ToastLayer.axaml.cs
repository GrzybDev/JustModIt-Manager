using Avalonia.Controls;
using Avalonia.Input;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>The notifications in the window's corner; one the pointer rests on stays until it leaves.</summary>
public sealed partial class ToastLayer : UserControl
{
    /// <summary>Creates the layer, which shows the notifications of the host it is given as its data.</summary>
    public ToastLayer() => InitializeComponent();

    private void OnPointerEntered(object? sender, PointerEventArgs e) => Hold(sender, held: true);

    private void OnPointerExited(object? sender, PointerEventArgs e) => Hold(sender, held: false);

    private static void Hold(object? sender, bool held)
    {
        if (sender is Control { DataContext: ToastViewModel toast })
        {
            toast.IsHeld = held;
        }
    }
}
