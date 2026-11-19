using System.Globalization;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>What a project published of the achievement texts of one Steam app, as its page sums them up.</summary>
public record ProjectAchievements
{
    public required long SteamAppId { get; init; }

    /// <summary>Steam's API name of the texts' language, such as <c>polish</c>.</summary>
    public string Language { get; init => field = value ?? ""; } = "";

    /// <summary>How many of the app's achievements the texts cover.</summary>
    public int Translated { get; init; }

    public int Total { get; init; }

    /// <summary>Grows with every publication, so texts saved under another revision are out of date.</summary>
    public int Revision { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Steam has texts of its own in the language for some of the app's achievements.</summary>
    public bool HasOfficial { get; init; }

    /// <summary>The Steam copy of the game the texts are for.</summary>
    [JsonIgnore]
    public GameKey Game => new(StoreId.Steam, SteamAppId.ToString(CultureInfo.InvariantCulture));
}

/// <summary>The achievement texts a project published for one Steam app, each achievement's text by its API name.</summary>
public sealed record AchievementTexts : ProjectAchievements
{
    public IReadOnlyList<AchievementText> Achievements { get; init => field = value ?? []; } = [];
}

/// <summary>One achievement's text in the language of its texts.</summary>
public sealed record AchievementText
{
    /// <summary>The name Steam's interface tells the achievement by, whatever the language.</summary>
    public required string ApiName { get; init; }

    public string Name { get; init => field = value ?? ""; } = "";

    public string Description { get; init => field = value ?? ""; } = "";

    /// <summary>Steam keeps the achievement's description hidden until it is unlocked.</summary>
    public bool Hidden { get; init; }
}
