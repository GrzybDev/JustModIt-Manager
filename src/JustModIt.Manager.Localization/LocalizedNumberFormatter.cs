using System.Globalization;
using Jeffijoe.MessageFormat;
using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Formatting.Formatters;

namespace JustModIt.Manager.Localization;

/// <summary>
/// Prints a number in <c>{name}</c> or <c>{name, number}</c> as <see cref="Formats.Number"/> does, where the library
/// would group digits from four on or not at all.
/// </summary>
internal sealed class LocalizedNumberFormatter : IFormatter
{
    private static readonly VariableFormatter Variables = new();

    public bool VariableMustExist => true;

    public bool CanFormat(FormatterRequest request) =>
        request.FormatterName is null
        || (request.FormatterName == "number" && string.IsNullOrWhiteSpace(request.FormatterArguments));

    public string Format(
        CultureInfo culture,
        FormatterRequest request,
        IReadOnlyDictionary<string, object?> args,
        object? value,
        IMessageFormatter messageFormatter) =>
        value switch
        {
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Formats.Number(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            _ => Variables.Format(culture, request, args, value, messageFormatter),
        };
}
