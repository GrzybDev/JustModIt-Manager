using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>A store the site links games to, such as Steam or GOG.</summary>
public sealed record StorefrontPlatform
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Icon { get; init => field = value ?? ""; } = "";

    public string Color { get; init => field = value ?? ""; } = "";

    public string UrlPattern { get; init => field = value ?? ""; } = "";

    /// <summary>The store the backend reads ids and languages from, e.g. <c>steam</c>; empty for a plain link.</summary>
    public string Provider { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }

    /// <summary>The address the site expects, in words; empty when it gives none.</summary>
    public string UrlPatternHint { get; init => field = value ?? ""; } = "";

    /// <summary>What players call the app they start this store's games from; empty means the store's name.</summary>
    public string LauncherName { get; init => field = value ?? ""; } = "";

    /// <summary>Markdown in the site's language: how players find the folder of a game they have from this store.</summary>
    public string InstallHint { get; init => field = value ?? ""; } = "";

    /// <summary>The game platform the site suggests for a new project whose game comes from this store.</summary>
    public Guid? GamePlatform { get; init; }

    [JsonIgnore]
    public StoreId? Store => StoreId.TryParse(Provider, out var store) ? store : null;

    /// <summary>The name to put into a sentence about the app this store's games start from.</summary>
    [JsonIgnore]
    public string Launcher => LauncherName.Length > 0 ? LauncherName : Name;
}

/// <summary>A project's link to the game in one store: the store version of the game the project is made for.</summary>
public sealed record StorefrontLink
{
    // The backend extracts each store's product id when a link is saved and keeps it in additional_data under a key of
    // that store's own; a new store with ids needs only an entry here.
    private static readonly FrozenDictionary<StoreId, string> IdProperties = new Dictionary<StoreId, string>
    {
        [StoreId.Steam] = "appid",
        [StoreId.Gog] = "gog_id",
        [StoreId.Epic] = "namespace",
    }.ToFrozenDictionary();

    public required Guid Uuid { get; init; }

    public StorefrontPlatform? Platform { get; init; }

    public string Url { get; init => field = value ?? ""; } = "";

    /// <summary>What the team says the project supports; empty means unknown or every system.</summary>
    public IReadOnlyList<OperatingSystemKind> OperatingSystems { get; init => field = value ?? []; } = [];

    /// <summary>What the backend learned from the store; every key is optional and its meaning depends on the store.</summary>
    public JsonElement AdditionalData { get; init; }

    public int Position { get; init; }

    /// <summary>The store has a build the team has not confirmed the project with; null from a backend that does not say.</summary>
    public bool? IsOutdated { get; init; }

    /// <summary>When the store's unconfirmed build appeared; null unless <see cref="IsOutdated"/>.</summary>
    public DateTimeOffset? OutdatedSince { get; init; }

    /// <summary>The store's newest build known, confirmed or not; empty when unknown.</summary>
    public string LatestBuild { get; init => field = value ?? ""; } = "";

    [JsonIgnore]
    public StoreId? Store => Platform?.Store;

    /// <summary>Null when the store has no ids or the backend could not find one for the link.</summary>
    [JsonIgnore]
    public GameKey? GameKey =>
        Store is { } store && IdProperties.TryGetValue(store, out var property) && ReadStoreId(store, property) is { } id
            ? new GameKey(store, id)
            : null;

    /// <summary>
    /// The languages the store sells the game with, in the store's own terms (Steam's language numbers, GOG's codes) or, for
    /// Epic, whose names for them the backend turns into BCP 47 tags, as tags; empty when the store gave none, which means
    /// unknown rather than none.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Languages =>
        Data("languages", JsonValueKind.Array) is { } languages
            ? [.. languages.EnumerateArray().Select(language => language.ValueKind switch
            {
                JsonValueKind.String => language.GetString() ?? "",
                JsonValueKind.Number => language.GetRawText(),
                _ => "",
            }).Where(language => language.Length > 0)]
            : [];

    /// <summary>The last store build the team confirmed the project works with; empty when unknown.</summary>
    [JsonIgnore]
    public string BuildId =>
        Data("buildid", JsonValueKind.String)?.GetString() ?? Data("buildid", JsonValueKind.Number)?.GetRawText() ?? "";

    // Steam and GOG ids are numbers, which come as numbers or text and stay text, since GOG ids exceed what a double holds.
    // An Epic namespace is a word or hexadecimal text that the store's own records spell the same way, so it is taken as
    // it is spelt.
    private string? ReadStoreId(StoreId store, string property)
    {
        if (AdditionalData.ValueKind != JsonValueKind.Object || !AdditionalData.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (store == StoreId.Epic)
        {
            return value.ValueKind == JsonValueKind.String && value.GetString() is { } text && IsNamespace(text) ? text : null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetUInt64(out var number) && number > 0 => number.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String when ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > 0 => number.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static bool IsNamespace(string text) =>
        text.Length is > 0 and <= 64 && char.IsAsciiLetterOrDigit(text[0])
        && text.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_');

    private JsonElement? Data(string property, JsonValueKind kind) =>
        AdditionalData.ValueKind == JsonValueKind.Object ? AdditionalData.GetOrNull(property, kind) : null;
}

/// <summary>Looks catalogue items up by the store ids that their storefront links name.</summary>
internal static class StoreIdLookup
{
    /// <summary>The items by each store id their links name, an item once per id, in the items' order.</summary>
    /// <param name="items">The items to look up.</param>
    /// <param name="links">The storefront links of an item.</param>
    public static FrozenDictionary<GameKey, T[]> Build<T>(IEnumerable<T> items, Func<T, IEnumerable<StorefrontLink>> links) =>
        items
            .SelectMany(item => links(item).Select(link => link.GameKey).OfType<GameKey>().Distinct().Select(key => (Key: key, Item: item)))
            .GroupBy(match => match.Key)
            .ToFrozenDictionary(group => group.Key, group => group.Select(match => match.Item).ToArray());
}
