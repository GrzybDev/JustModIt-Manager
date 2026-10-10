using Avalonia;
using Avalonia.Media;
using JustModIt.Manager.Ui;

namespace MenadzerSpolszczen;

/// <summary>The app's own touch to the window: the name in the title bar split like its logo, white over red.</summary>
internal static class WindowLook
{
    /// <summary>Adds the app's look to the window.</summary>
    /// <param name="ui">Where the window is put together.</param>
    public static void Compose(UiComposition ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.Resources["WordmarkBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xE9, 0xE8, 0xE7), 0.5),
                new GradientStop(Color.FromRgb(0xD4, 0x21, 0x3D), 0.5),
            },
        };
    }
}
