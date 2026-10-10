using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>The layer that draws the dialog shown over a dimmed window; a click beside the dialog dismisses it.</summary>
public sealed partial class DialogLayer : UserControl
{
    /// <summary>Creates the layer, which shows the dialog host it is given as its data.</summary>
    public DialogLayer()
    {
        InitializeComponent();
        Host.PropertyChanged += OnHostChanged;
    }

    // Focus lands in the dialog, on its first button: the way out of a question, the first choice of a choice. Its ring
    // shows only to a player who was using the keyboard, as a page's own focus would.
    private void OnHostChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ContentProperty && e.NewValue is not null)
        {
            var byKeyboard = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is StyledElement focused
                && focused.Classes.Contains(":focus-visible");
            Dispatcher.UIThread.Post(
                () => Host.GetVisualDescendants().OfType<Button>().FirstOrDefault()
                    ?.Focus(byKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified),
                DispatcherPriority.Loaded);
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DialogHost dialogs)
        {
            dialogs.CancelCurrent();
            e.Handled = true;
        }
    }
}
