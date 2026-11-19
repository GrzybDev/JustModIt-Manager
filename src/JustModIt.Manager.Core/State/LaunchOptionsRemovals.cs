using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.State;

/// <summary>
/// The launch options the app added for installs that are gone, by game, kept until the app took them out of the player's
/// options in Steam, which it can only while Steam's interface can be reached.
/// </summary>
/// <param name="Games">The options to take out, by game.</param>
public sealed record LaunchOptionsRemovals(ValueDictionary<GameKey, ValueList<string>> Games);

/// <summary>The format of <c>state/launch-options.json</c>, which leaves out an entry it cannot read.</summary>
public sealed class LaunchOptionsRemovalsFormat : IJsonDocumentFormat<LaunchOptionsRemovals>
{
    private const string GamesProperty = "games";

    public static LaunchOptionsRemovalsFormat Instance { get; } = new();

    public int CurrentVersion => 1;

    public IReadOnlySet<string> KnownProperties { get; } = FrozenSet.Create(StringComparer.Ordinal, GamesProperty);

    public LaunchOptionsRemovals Read(JsonElement root, int version)
    {
        var games = ImmutableDictionary.CreateBuilder<GameKey, ValueList<string>>();
        foreach (var entry in root.GetItems(GamesProperty))
        {
            if (GameKey.TryParse(entry.GetTextOrNull("game"), out var game) && entry.GetStrings("options").ToList() is { Count: > 0 } options)
            {
                games[game] = [.. options];
            }
        }

        return new LaunchOptionsRemovals(new ValueDictionary<GameKey, ValueList<string>>(games.ToImmutable()));
    }

    public void Write(Utf8JsonWriter writer, LaunchOptionsRemovals document)
    {
        writer.WriteStartArray(GamesProperty);
        foreach (var (game, options) in document.Games.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("game", game.ToString());
            writer.WriteStrings("options", options);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
