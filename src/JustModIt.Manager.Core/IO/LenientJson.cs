using System.Text.Json;

namespace JustModIt.Manager.Core.IO;

/// <summary>Reads single properties of a JSON object, giving null for a missing value or one of the wrong kind.</summary>
public static class LenientJson
{
    /// <summary>Reads the boolean property <paramref name="name"/>.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static bool? GetBooleanOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>Reads the text property <paramref name="name"/>, giving null also for empty or white-space text.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static string? GetTextOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    /// <summary>
    /// Reads the path property <paramref name="name"/>, giving null also for a relative path, which would resolve against
    /// the current folder.
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static string? GetFullPathOrNull(this JsonElement element, string name) =>
        element.GetTextOrNull(name) is { } path && Path.IsPathFullyQualified(path) ? path : null;

    /// <summary>
    /// Reads the enum property <paramref name="name"/> written as one member's name in any letter case; numbers and
    /// combinations give null.
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static TEnum? GetEnumOrNull<TEnum>(this JsonElement element, string name)
        where TEnum : struct, Enum =>
        element.GetTextOrNull(name) is { } text ? EnumByName<TEnum>(text) : null;

    /// <summary>The enum member named <paramref name="text"/> in any letter case; null for numbers and combinations.</summary>
    /// <param name="text">The member's name as stored.</param>
    public static TEnum? EnumByName<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().FirstOrDefault(member => member.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } match
            ? Enum.Parse<TEnum>(match)
            : null;

    /// <summary>Reads the date and time property <paramref name="name"/>, written as ISO 8601 text.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time)
            ? time
            : null;

    /// <summary>Reads the number property <paramref name="name"/> when it is a whole number that fits an <see cref="int"/>.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static int? GetInt32OrNull(this JsonElement element, string name) =>
        element.GetOrNull(name, JsonValueKind.Number) is { } number && number.TryGetInt32(out var value) ? value : null;

    /// <summary>Reads the number property <paramref name="name"/> when it is a whole number that fits a <see cref="long"/>.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static long? GetInt64OrNull(this JsonElement element, string name) =>
        element.GetOrNull(name, JsonValueKind.Number) is { } number && number.TryGetInt64(out var value) ? value : null;

    /// <summary>Reads the text property <paramref name="name"/> as a GUID.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static Guid? GetGuidOrNull(this JsonElement element, string name) =>
        Guid.TryParse(element.GetTextOrNull(name), out var guid) ? guid : null;

    /// <summary>The items of the array property <paramref name="name"/> that are of the given kind; none without the array.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="kind">The kind the items must have; others are left out.</param>
    public static IEnumerable<JsonElement> GetItems(this JsonElement element, string name, JsonValueKind kind = JsonValueKind.Object) =>
        element.GetOrNull(name, JsonValueKind.Array) is { } array ? array.EnumerateArray().Where(item => item.ValueKind == kind) : [];

    /// <summary>The text items of the array property <paramref name="name"/>; none without the array.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    public static IEnumerable<string> GetStrings(this JsonElement element, string name) =>
        element.GetItems(name, JsonValueKind.String).Select(item => item.GetString()!);

    /// <summary>Reads the property <paramref name="name"/> when its value is of the given kind.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="kind">The kind the value must have.</param>
    public static JsonElement? GetOrNull(this JsonElement element, string name, JsonValueKind kind) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : null;
}

/// <summary>Writes the optional properties that <see cref="LenientJson"/> reads, leaving out those without a value.</summary>
internal static class OptionalJsonWriting
{
    /// <summary>Writes a text property when it has a value.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">The value; null leaves the property out.</param>
    public static void WriteIfSet(this Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    /// <summary>Writes a boolean property when it has a value.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">The value; null leaves the property out.</param>
    public static void WriteIfSet(this Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } set)
        {
            writer.WriteBoolean(name, set);
        }
    }

    /// <summary>Writes a number property when it has a value.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">The value; null leaves the property out.</param>
    public static void WriteIfSet(this Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } set)
        {
            writer.WriteNumber(name, set);
        }
    }

    /// <summary>Writes a GUID property in its hyphenated form when it has a value.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">The value; null leaves the property out.</param>
    public static void WriteIfSet(this Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is { } set)
        {
            writer.WriteString(name, set.ToString("D"));
        }
    }

    /// <summary>Writes a date and time property when it has a value.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">The value; null leaves the property out.</param>
    public static void WriteIfSet(this Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is { } set)
        {
            writer.WriteString(name, set);
        }
    }

    /// <summary>Writes an enum property as its member's name when it has a value.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">The value; null leaves the property out.</param>
    public static void WriteEnumIfSet<TEnum>(this Utf8JsonWriter writer, string name, TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is { } set)
        {
            writer.WriteString(name, set.ToString());
        }
    }

    /// <summary>Writes an array of texts when there is any.</summary>
    /// <param name="writer">The writer inside an object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="values">The texts; none leaves the property out.</param>
    public static void WriteStringsIfAny(this Utf8JsonWriter writer, string name, IReadOnlyCollection<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
