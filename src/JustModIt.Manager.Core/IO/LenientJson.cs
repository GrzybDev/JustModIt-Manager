using System.Text.Json;

namespace JustModIt.Manager.Core.IO;

/// <summary>Reads single properties of a JSON object, giving null for a missing value or one of the wrong kind.</summary>
internal static class LenientJson
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
        element.GetTextOrNull(name) is { } text
        && Enum.GetNames<TEnum>().FirstOrDefault(member => member.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } match
            ? Enum.Parse<TEnum>(match)
            : null;

    /// <summary>Reads the property <paramref name="name"/> when its value is of the given kind.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="kind">The kind the value must have.</param>
    public static JsonElement? GetOrNull(this JsonElement element, string name, JsonValueKind kind) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : null;
}
