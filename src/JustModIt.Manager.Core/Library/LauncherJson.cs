using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library;

/// <summary>Reads the JSON files that launchers keep about their installs.</summary>
internal static class LauncherJson
{
    private const long MaxFileLength = 16 * 1024 * 1024;

    /// <summary>Reads a launcher's JSON file, with or without a byte order mark.</summary>
    /// <param name="fileSystem">Reads the file.</param>
    /// <param name="path">The file.</param>
    /// <param name="typeInfo">The source-generated contract of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="InvalidDataException">The file is too large to be launcher metadata.</exception>
    /// <exception cref="JsonException">The file is not the expected JSON.</exception>
    public static async Task<T?> ReadAsync<T>(IFileSystem fileSystem, string path, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (fileSystem.GetEntry(path) is { Length: > MaxFileLength })
        {
            throw new InvalidDataException("The file is too large to be launcher metadata.");
        }

        // Launchers write these files without a byte order mark, but hand-edited or third-party copies may have one.
        var content = await fileSystem.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        ReadOnlySpan<byte> json = content;
        if (json.StartsWith(Encoding.UTF8.Preamble))
        {
            json = json[Encoding.UTF8.Preamble.Length..];
        }

        return JsonSerializer.Deserialize(json, typeInfo);
    }

    /// <summary>Gives the trimmed text, or null when it is empty or only white space.</summary>
    /// <param name="value">A value from a launcher's file; may be null.</param>
    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Accepts ids written as JSON numbers too, keeping the number's own text, since build ids exceed 2^53 and a double
/// would round them.
/// </summary>
internal sealed class IdStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            _ => throw new JsonException("Expected an id as a string or a number."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
