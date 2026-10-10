using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace JustModIt.Manager.Ui.Pages.Onboarding;

/// <summary>
/// Binds a choice tile to the value it stands for, given as the converter's parameter: the tile is checked while the
/// value is chosen, and checking it chooses the value.
/// </summary>
internal sealed class ChoiceConverter : IValueConverter
{
    public static ChoiceConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    // A tile unchecked by its neighbour's choice leaves the value to that neighbour.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
