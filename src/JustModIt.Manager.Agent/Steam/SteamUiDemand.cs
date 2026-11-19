using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>What Steam's interface is to get from the app, with the texts kept as the hook takes them rather than as objects.</summary>
/// <param name="Texts">
/// The achievement texts of the games whose texts are on, as JSON: name and description by app id, then by API name.
/// </param>
/// <param name="Games">How many games the texts are for.</param>
/// <param name="Language">The texts' language by Steam's API name.</param>
/// <param name="UpdatedAt">The newest publication among the texts; null when none tells.</param>
/// <param name="LaunchOptionsGames">The games whose launch options the app manages, which Steam's interface reports.</param>
internal sealed record SteamUiDemand(ReadOnlyMemory<byte> Texts, int Games, string Language, DateTimeOffset? UpdatedAt,
    ValueList<GameKey> LaunchOptionsGames)
{
    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static SteamUiDemand None { get; } = From([], []);

    /// <summary>Something needs Steam's interface, so the plugin or the classic mode's switch is worth putting in place.</summary>
    public bool IsNeeded => Games > 0 || LaunchOptionsGames.Count > 0;

    /// <summary>What Steam's interface is to get, from the texts in the language most of them are in, since the hook takes one.</summary>
    /// <param name="texts">The achievement texts of the games whose texts are on.</param>
    /// <param name="launchOptionsGames">The games whose launch options the app manages.</param>
    public static SteamUiDemand From(IReadOnlyList<AchievementTexts> texts, ValueList<GameKey> launchOptionsGames)
    {
        var language = texts.GroupBy(game => game.Language, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count())
            .Select(group => group.Key).FirstOrDefault() ?? "";
        List<AchievementTexts> shown = [.. texts.Where(game => game.Language.Equals(language, StringComparison.OrdinalIgnoreCase))];
        var content = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(content, Compact))
        {
            writer.WriteStartObject();
            foreach (var game in shown)
            {
                writer.WriteStartObject(game.Game.StoreGameId);
                foreach (var achievement in game.Achievements)
                {
                    writer.WriteStartArray(achievement.ApiName);
                    writer.WriteStringValue(achievement.Name);
                    writer.WriteStringValue(achievement.Description);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return new SteamUiDemand(content.WrittenMemory.ToArray(), shown.Count, language, shown.Max(game => game.UpdatedAt),
            launchOptionsGames);
    }
}
