using System.Collections.Frozen;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// The language an app's projects bring to games, if it has one: a game whose store already sells it in that language is
/// never changed automatically, and an app without a target language skips the rule.
/// </summary>
public sealed class TargetLanguage
{
    // Steam names languages by SteamKit's ELanguage numbers in store data and by its own API names elsewhere; a
    // language with regional variants on Steam lists all of them.
    private static readonly FrozenDictionary<string, (string[] Numbers, string[] Names)> SteamLanguages =
        new Dictionary<string, (string[] Numbers, string[] Names)>
        {
            ["en"] = (["0"], ["english"]),
            ["de"] = (["1"], ["german"]),
            ["fr"] = (["2"], ["french"]),
            ["it"] = (["3"], ["italian"]),
            ["ko"] = (["4"], ["koreana"]),
            ["es"] = (["5", "27"], ["spanish", "latam"]),
            ["zh"] = (["6", "7"], ["schinese", "tchinese"]),
            ["ru"] = (["8"], ["russian"]),
            ["th"] = (["9"], ["thai"]),
            ["ja"] = (["10"], ["japanese"]),
            ["pt"] = (["11", "22"], ["portuguese", "brazilian"]),
            ["pl"] = (["12"], ["polish"]),
            ["da"] = (["13"], ["danish"]),
            ["nl"] = (["14"], ["dutch"]),
            ["fi"] = (["15"], ["finnish"]),
            ["no"] = (["16"], ["norwegian"]),
            ["sv"] = (["17"], ["swedish"]),
            ["hu"] = (["18"], ["hungarian"]),
            ["cs"] = (["19"], ["czech"]),
            ["ro"] = (["20"], ["romanian"]),
            ["tr"] = (["21"], ["turkish"]),
            ["bg"] = (["23"], ["bulgarian"]),
            ["el"] = (["24"], ["greek"]),
            ["ar"] = (["25"], ["arabic"]),
            ["uk"] = (["26"], ["ukrainian"]),
            ["vi"] = (["28"], ["vietnamese"]),
            ["id"] = (["29"], ["indonesian"]),
        }.ToFrozenDictionary();

    // GOG uses ISO codes except for these; the rest are the ISO code itself.
    private static readonly FrozenDictionary<string, string[]> GogLanguages = new Dictionary<string, string[]>
    {
        ["cs"] = ["cz"],
        ["ja"] = ["jp"],
        ["el"] = ["gk"],
        ["bg"] = ["bl"],
        ["sr"] = ["sb"],
        ["zh"] = ["cn", "zh"],
        ["pt"] = ["pt", "br"],
    }.ToFrozenDictionary();

    // Norwegian comes as Bokmal and Nynorsk too, which stores do not tell apart.
    private static readonly FrozenDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["nb"] = "no",
        ["nn"] = "no",
    }.ToFrozenDictionary();

    private readonly FrozenSet<string> _steam;
    private readonly FrozenSet<string> _gog;

    private TargetLanguage(string? code)
    {
        Code = code;
        var steam = code is not null && SteamLanguages.TryGetValue(code, out var known) ? known : ([], []);
        _steam = steam.Numbers.Concat(steam.Names).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _gog = (code is null ? [] : GogLanguages.TryGetValue(code, out var codes) ? codes : [code]).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public static TargetLanguage None { get; } = new(null);

    /// <summary>The ISO 639-1 language, e.g. <c>en</c>; null when the app has none.</summary>
    public string? Code { get; }

    public bool IsSet => Code is not null;

    /// <summary>The target language an app's build names.</summary>
    /// <param name="code">An ISO 639-1 code or a tag starting with one, such as <c>pt-BR</c>; empty means none.</param>
    public static TargetLanguage FromCode(string? code)
    {
        var language = code?.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (string.IsNullOrEmpty(language))
        {
            return None;
        }

        return new TargetLanguage(Aliases.GetValueOrDefault(language, language));
    }

    /// <summary>
    /// Whether the store sells the game in the language; a store that gave no languages, or one the app cannot read them
    /// from, counts as no.
    /// </summary>
    /// <param name="link">A project's link to the game in one store.</param>
    public bool IsOfferedBy(StorefrontLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!IsSet)
        {
            return false;
        }

        var accepted = link.Store == StoreId.Steam ? _steam : link.Store == StoreId.Gog ? _gog : null;
        return accepted is not null && link.Languages.Any(language => accepted.Contains(language) || accepted.Contains(Base(language)));
    }

    /// <summary>Whether any of the stores sells the game in the language.</summary>
    /// <param name="links">The projects' links to the player's store version of the game.</param>
    public bool IsOfferedByAny(IEnumerable<StorefrontLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);
        return links.Any(IsOfferedBy);
    }

    public override string ToString() => Code ?? "";

    // A regional code such as "es_mx" or "pl-PL" names the language too.
    private static string Base(string language) => language.Split('-', '_')[0];
}
