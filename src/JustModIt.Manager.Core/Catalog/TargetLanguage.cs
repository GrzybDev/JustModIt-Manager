using System.Collections.Frozen;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// The language an app's projects bring to games, if it has one: a game whose store already sells it in that language is
/// never changed automatically, and an app without a target language skips the rule.
/// </summary>
public sealed class TargetLanguage
{
    // Steam names languages by SteamKit's ELanguage numbers in store data and by its API names (LanguageNames)
    // elsewhere; a language with regional variants on Steam lists all of them.
    private static readonly FrozenDictionary<string, string[]> SteamNumbers = new Dictionary<string, string[]>
    {
        ["en"] = ["0"],
        ["de"] = ["1"],
        ["fr"] = ["2"],
        ["it"] = ["3"],
        ["ko"] = ["4"],
        ["es"] = ["5", "27"],
        ["zh"] = ["6", "7"],
        ["ru"] = ["8"],
        ["th"] = ["9"],
        ["ja"] = ["10"],
        ["pt"] = ["11", "22"],
        ["pl"] = ["12"],
        ["da"] = ["13"],
        ["nl"] = ["14"],
        ["fi"] = ["15"],
        ["no"] = ["16"],
        ["sv"] = ["17"],
        ["hu"] = ["18"],
        ["cs"] = ["19"],
        ["ro"] = ["20"],
        ["tr"] = ["21"],
        ["bg"] = ["23"],
        ["el"] = ["24"],
        ["ar"] = ["25"],
        ["uk"] = ["26"],
        ["vi"] = ["28"],
        ["id"] = ["29"],
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
        _steam = (code is null ? [] : SteamNumbers.GetValueOrDefault(code, []).Concat(LanguageNames.NamesOf(code)))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _gog = (code is null ? [] : GogLanguages.TryGetValue(code, out var codes) ? codes : [code]).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public static TargetLanguage None { get; } = new(null);

    /// <summary>The ISO 639-1 language, e.g. <c>en</c>; null when the app has none.</summary>
    public string? Code { get; }

    public bool IsSet => Code is not null;

    /// <summary>The target language an app's build names.</summary>
    /// <param name="code">An ISO 639-1 code or a tag starting with one, such as <c>pt-BR</c>; empty means none.</param>
    public static TargetLanguage FromCode(string? code) => CodeOf(code) is { } language ? new TargetLanguage(language) : None;

    /// <summary>Whether a tag names the language, in any region.</summary>
    /// <param name="tag">A language tag such as <c>pl-PL</c>, e.g. one a store names for a game's install.</param>
    public bool IsNamedBy(string tag) => IsSet && CodeOf(tag) == Code;

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

        if (link.Store == StoreId.Epic)
        {
            return link.Languages.Any(IsNamedBy);
        }

        var accepted = link.Store == StoreId.Steam ? _steam : link.Store == StoreId.Gog ? _gog : null;
        return accepted is not null
            && link.Languages.Any(language => accepted.Contains(language) || accepted.Contains(LanguageNames.Base(language)));
    }

    /// <summary>Whether any of the stores sells the game in the language.</summary>
    /// <param name="links">The projects' links to the player's store version of the game.</param>
    public bool IsOfferedByAny(IEnumerable<StorefrontLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);
        return links.Any(IsOfferedBy);
    }

    public override string ToString() => Code ?? "";

    private static string? CodeOf(string? tag)
    {
        var language = tag is null ? null : LanguageNames.Base(tag.Trim()).ToLowerInvariant();
        return string.IsNullOrEmpty(language) ? null : Aliases.GetValueOrDefault(language, language);
    }
}
