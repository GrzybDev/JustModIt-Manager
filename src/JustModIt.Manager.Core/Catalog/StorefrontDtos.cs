using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;

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
        Store is { } store && IdProperties.TryGetValue(store, out var property) && ReadStoreId(property) is { } id
            ? new GameKey(store, id)
            : null;

    /// <summary>
    /// The languages the store sells the game with, in the store's own terms (Steam's language numbers, GOG's codes); empty
    /// when the store gave none, which means unknown rather than none.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Languages =>
        TryGetData("languages", JsonValueKind.Array, out var languages)
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
        TryGetData("buildid", JsonValueKind.String, out var build) ? build.GetString() ?? ""
        : TryGetData("buildid", JsonValueKind.Number, out build) ? build.GetRawText()
        : "";

    // Ids come as numbers or text; either way they stay text, since GOG ids exceed what a double holds.
    private string? ReadStoreId(string property) =>
        AdditionalData.ValueKind == JsonValueKind.Object && AdditionalData.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetUInt64(out var number) && number > 0 => number.ToString(CultureInfo.InvariantCulture),
                JsonValueKind.String when ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number > 0 => number.ToString(CultureInfo.InvariantCulture),
                _ => null,
            }
            : null;

    private bool TryGetData(string property, JsonValueKind kind, out JsonElement value)
    {
        if (AdditionalData.ValueKind == JsonValueKind.Object && AdditionalData.TryGetProperty(property, out value)
            && value.ValueKind == kind)
        {
            return true;
        }

        value = default;
        return false;
    }
}
