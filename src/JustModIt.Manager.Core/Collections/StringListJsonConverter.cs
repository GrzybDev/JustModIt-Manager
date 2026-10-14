using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Collections;

/// <summary>A list of strings as a JSON array, which the serializer cannot build a <see cref="ValueList{T}"/> from by itself.</summary>
internal sealed class StringListJsonConverter : JsonConverter<ValueList<string>>
{
    public override bool HandleNull => true;

    public override ValueList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A list of strings must be an array.");
        }

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(reader.TokenType == JsonTokenType.String
                ? reader.GetString()!
                : throw new JsonException("A list of strings may hold only strings."));
        }

        return [.. items];
    }

    public override void Write(Utf8JsonWriter writer, ValueList<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
