using System.Globalization;
using System.Text.RegularExpressions;
using Jeffijoe.MessageFormat;

namespace JustModIt.Manager.Localization;

/// <summary>The app's language: its culture, its order of titles and the formatting of its texts.</summary>
public static class Text
{
    private static volatile Language _current = new(TextCatalog.Base);

    /// <summary>The culture of the app's language.</summary>
    public static CultureInfo Culture => _current.Catalog.Culture;

    /// <summary>Orders titles by the alphabet of the app's language, ignoring case, and numbers in titles by value.</summary>
    public static StringComparer Comparer => _current.Comparer;

    internal static double SmallestGroupedNumber => _current.SmallestGroupedNumber;

    internal static Regex VersionWord => _current.VersionWord;

    /// <summary>Makes <paramref name="catalog"/> the app's texts and language; an app calls it once, at startup.</summary>
    /// <param name="catalog">The texts the app's build merged (<see cref="TextCatalog.Load"/>).</param>
    public static void Use(TextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _current = new Language(catalog);
    }

    /// <summary>
    /// Formats an ICU MessageFormat pattern taken from <see cref="Strings"/>, for example
    /// <c>Text.Format(Strings.GameCount, ("count", 3))</c>.
    /// </summary>
    /// <param name="pattern">A resource string; parsed patterns are cached, so never one built at run time.</param>
    /// <param name="arguments">The pattern's arguments by name; numbers print as <see cref="Formats.Number"/> does.</param>
    public static string Format(string pattern, params ReadOnlySpan<(string Name, object? Value)> arguments)
    {
        var values = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var (name, value) in arguments)
        {
            values[name] = value;
        }

        return _current.Formatter.FormatMessage(pattern, values);
    }

    /// <summary>The text of a key in the app's language, or in the English base when the language lacks it.</summary>
    /// <param name="name">The key in <c>Strings.resx</c>.</param>
    internal static string Lookup(string name) => _current.Catalog[name];

    private sealed class Language
    {
        public Language(TextCatalog catalog)
        {
            Catalog = catalog;
            Comparer = StringComparer.Create(catalog.Culture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
            SmallestGroupedNumber = Math.Pow(10, FirstGroupSize(catalog.Culture) + MinimumGroupingDigits(catalog) - 1);
            VersionWord = VersionWordPattern(catalog);
            Formatter = new MessageFormatter(useCache: true, catalog.Culture);
            Formatter.Formatters.Insert(0, new LocalizedPluralFormatter());
            Formatter.Formatters.Insert(0, new LocalizedNumberFormatter());
        }

        public TextCatalog Catalog { get; }

        public StringComparer Comparer { get; }

        /// <summary>
        /// .NET groups digits from the first full group on, but a language may wait for more digits (Spanish writes
        /// 1234 but 12.345), and .NET does not know how many, so the translation says it.
        /// </summary>
        public double SmallestGroupedNumber { get; }

        public MessageFormatter Formatter { get; }

        public Regex VersionWord { get; }

        private static int FirstGroupSize(CultureInfo culture) =>
            culture.NumberFormat.NumberGroupSizes is [> 0 and var size, ..] ? size : 3;

        private static Regex VersionWordPattern(TextCatalog catalog)
        {
            static string Alternatives(string words) =>
                string.Join('|', words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));

            var words = Alternatives(catalog[nameof(Strings.VersionWords)]);
            var all = Alternatives(catalog[nameof(Strings.VersionWords)] + " " + catalog[nameof(Strings.VersionAbbreviations)]);
            return new Regex($@"^(?:(?:{words})(?:\s+|$)|(?:{all})\.?\s*(?=[0-9]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static int MinimumGroupingDigits(TextCatalog catalog) =>
            int.TryParse(catalog[nameof(Strings.MinimumGroupingDigits)], NumberStyles.None, CultureInfo.InvariantCulture,
                out var digits) && digits > 0
                ? digits
                : 1;
    }
}
