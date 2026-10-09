using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Core.Library;

/// <summary>Turns the language names and codes that stores write into BCP 47 tags.</summary>
internal static partial class LanguageNames
{
    // Steam's API language codes, which the registry entries of GOG's installers share for the common languages.
    private static readonly FrozenDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["arabic"] = "ar",
        ["brazilian"] = "pt-BR",
        ["bulgarian"] = "bg",
        ["chinese"] = "zh",
        ["croatian"] = "hr",
        ["czech"] = "cs",
        ["danish"] = "da",
        ["dutch"] = "nl",
        ["english"] = "en",
        ["estonian"] = "et",
        ["finnish"] = "fi",
        ["french"] = "fr",
        ["german"] = "de",
        ["greek"] = "el",
        ["hebrew"] = "he",
        ["hungarian"] = "hu",
        ["indonesian"] = "id",
        ["italian"] = "it",
        ["japanese"] = "ja",
        ["korean"] = "ko",
        ["koreana"] = "ko",
        ["latam"] = "es-419",
        ["latvian"] = "lv",
        ["lithuanian"] = "lt",
        ["norwegian"] = "no",
        ["polish"] = "pl",
        ["portuguese"] = "pt",
        ["romanian"] = "ro",
        ["russian"] = "ru",
        ["schinese"] = "zh-CN",
        ["serbian"] = "sr",
        ["slovak"] = "sk",
        ["slovenian"] = "sl",
        ["spanish"] = "es",
        ["swedish"] = "sv",
        ["tchinese"] = "zh-TW",
        ["thai"] = "th",
        ["turkish"] = "tr",
        ["ukrainian"] = "uk",
        ["vietnamese"] = "vi",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gives the tag of a language name such as <c>polish</c> or <c>schinese</c>; null when it is not known.</summary>
    /// <param name="name">The name as a store wrote it, in any letter case; may be null.</param>
    public static string? FromName(string? name) =>
        name is null ? null : Names.GetValueOrDefault(name.Trim());

    /// <summary>Gives a tag such as <c>pl-PL</c> in its usual casing; null when the text is not a language tag.</summary>
    /// <param name="tag">The text as a store wrote it; may be null.</param>
    public static string? FromTag(string? tag)
    {
        if (tag is null || !Tag().IsMatch(tag.Trim()))
        {
            return null;
        }

        var parts = tag.Trim().Split('-');
        parts[0] = parts[0].ToLowerInvariant();
        for (var i = 1; i < parts.Length; i++)
        {
            parts[i] = parts[i].Length == 2 ? parts[i].ToUpperInvariant() : parts[i];
        }

        return string.Join('-', parts);
    }

    /// <summary>Every language name whose tag is of the language, such as <c>schinese</c> and <c>tchinese</c> for <c>zh</c>.</summary>
    /// <param name="code">An ISO 639-1 code.</param>
    public static IEnumerable<string> NamesOf(string code) =>
        Names.Where(name => Base(name.Value).Equals(code, StringComparison.OrdinalIgnoreCase)).Select(name => name.Key);

    /// <summary>The language of a tag without its region, such as <c>pl</c> for <c>pl-PL</c> or <c>es</c> for <c>es_mx</c>.</summary>
    /// <param name="tag">A language tag or a store's language code.</param>
    public static string Base(string tag) => tag.Split('-', '_')[0];

    /// <summary>Reads a tag or, failing that, a language name; null when the text is neither.</summary>
    /// <param name="text">The text as a store wrote it; may be null.</param>
    public static string? FromTagOrName(string? text) => FromTag(text) ?? FromName(text);

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();
}
