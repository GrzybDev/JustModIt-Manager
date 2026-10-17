using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Games;

/// <summary>A store as its plain id, e.g. <c>"steam"</c>.</summary>
public sealed class StoreIdJsonConverter : JsonConverter<StoreId>
{
    public override StoreId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && StoreId.TryParse(reader.GetString(), out var store)
            ? store
            : throw new JsonException("A store id must be a string of letters, digits and hyphens.");

    public override void Write(Utf8JsonWriter writer, StoreId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>A game as its text form, e.g. <c>"steam:1000"</c>.</summary>
public sealed class GameKeyJsonConverter : JsonConverter<GameKey>
{
    public override GameKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && GameKey.TryParse(reader.GetString(), out var key)
            ? key
            : throw new JsonException("A game key must be a string like \"store:id\".");

    public override void Write(Utf8JsonWriter writer, GameKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
