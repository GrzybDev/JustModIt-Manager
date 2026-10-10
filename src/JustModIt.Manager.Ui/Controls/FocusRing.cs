using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// Shapes the keyboard focus ring after the control it surrounds: 2 px of the focus colour 2 px off its edge, with
/// the control's corner radius plus 4, so one ring fits buttons, pills, switches and cards alike.
/// </summary>
public static class FocusRing
{
    /// <summary>Turns the focused control into the ring's corner radius.</summary>
    public static IValueConverter Radius { get; } = new RadiusConverter();

    private sealed class RadiusConverter : IValueConverter
    {
        private const double Gap = 4;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var radius = value switch
            {
                TemplatedControl control => control.CornerRadius,
                Border border => border.CornerRadius,
                _ => new CornerRadius(8),
            };
            return new CornerRadius(Grow(radius.TopLeft), Grow(radius.TopRight), Grow(radius.BottomRight), Grow(radius.BottomLeft));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

        private static double Grow(double radius) => radius > 0 ? radius + Gap : 0;
    }
}
