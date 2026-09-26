using System.Globalization;
using System.Text;
using Jeffijoe.MessageFormat;
using Jeffijoe.MessageFormat.Formatting;

namespace JustModIt.Manager.Localization;

/// <summary>
/// Takes over <c>plural</c> from the library, whose "#" is <c>double.ToString()</c> in the thread's culture: no digit
/// grouping, and the decimal separator of whatever language the system uses.
/// </summary>
internal sealed class LocalizedPluralFormatter : BaseFormatter, IFormatter
{
    // "#" becomes an argument instead of the number itself, so the pattern cache keeps one entry per form, not one
    // per number.
    private const string NumberArgument = "__number";
    private const string NumberReference = "{" + NumberArgument + "}";

    // The library applies the CLDR plural rules of every language, but only while it formats a plural, so a message
    // with one form per category tells which category a number falls in.
    private const string CategoryMessage = "{n, plural, zero {zero} one {one} two {two} few {few} many {many} other {other}}";

    private static readonly MessageFormatter Categories = new(useCache: true);

    public bool VariableMustExist => true;

    public bool CanFormat(FormatterRequest request) => request.FormatterName == "plural";

    public string Format(
        CultureInfo culture,
        FormatterRequest request,
        IReadOnlyDictionary<string, object?> args,
        object? value,
        IMessageFormatter messageFormatter)
    {
        var arguments = ParseArguments(request);
        var offset = arguments.Extensions
            .Where(extension => extension.Extension == "offset")
            .Select(extension => double.Parse(extension.Value, CultureInfo.InvariantCulture))
            .FirstOrDefault();
        var number = Convert.ToDouble(value, culture);
        var category = Category(number - offset, culture);
        var blocks = arguments.KeyedBlocks.ToList();
        var block = blocks.Find(candidate => IsExactly(candidate.Key, number))
            ?? blocks.Find(candidate => candidate.Key == category)
            ?? blocks.Find(candidate => candidate.Key == OtherKey)
            ?? throw new MessageFormatterException("'other' option not found in pattern.");

        var values = new Dictionary<string, object?>(args, StringComparer.Ordinal)
        {
            [NumberArgument] = Formats.Number(number - offset),
        };
        return messageFormatter.FormatMessage(ReplaceNumberSigns(block.BlockText), values, culture);
    }

    private static string Category(double number, CultureInfo culture) =>
        Categories.FormatMessage(CategoryMessage, new Dictionary<string, object?> { ["n"] = number }, culture);

    private static bool IsExactly(string key, double number) =>
        key.StartsWith('=')
        && double.TryParse(key.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
        && exact == number;

    // Quotes stay in the text, because the nested message is parsed again; only a "#" of this plural, outside quotes
    // and nested arguments, is the number.
    private static string ReplaceNumberSigns(string block)
    {
        var text = new StringBuilder(block.Length + NumberReference.Length);
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < block.Length; i++)
        {
            var character = block[i];
            var next = i + 1 < block.Length ? block[i + 1] : '\0';
            if (character == '\'' && next == '\'')
            {
                text.Append("''");
                i++;
                continue;
            }

            if (character == '\'')
            {
                quoted = !quoted && next is '{' or '}' or '#' or '|';
            }
            else if (!quoted && character == '{')
            {
                depth++;
            }
            else if (!quoted && character == '}')
            {
                depth--;
            }
            else if (!quoted && character == '#' && depth == 0)
            {
                text.Append(NumberReference);
                continue;
            }

            text.Append(character);
        }

        return text.ToString();
    }
}
