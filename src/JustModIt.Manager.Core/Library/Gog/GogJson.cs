using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

internal static class GogJson
{
    private const long MaxFileLength = 16 * 1024 * 1024;

    /// <exception cref="InvalidDataException">The file is too large to be launcher metadata.</exception>
    /// <exception cref="JsonException">The file is not the expected JSON.</exception>
    public static async Task<T?> ReadAsync<T>(IFileSystem fileSystem, string path, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (fileSystem.GetEntry(path) is { Length: > MaxFileLength })
        {
            throw new InvalidDataException("The file is too large to be launcher metadata.");
        }

        // GOG writes these files without a byte order mark, but hand-edited or third-party copies may have one.
        var content = await fileSystem.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        ReadOnlySpan<byte> json = content;
        if (json.StartsWith(Encoding.UTF8.Preamble))
        {
            json = json[Encoding.UTF8.Preamble.Length..];
        }

        return JsonSerializer.Deserialize(json, typeInfo);
    }

    /// <summary>Tells whether <paramref name="value"/> is a GOG product id, which is digits only.</summary>
    /// <param name="value">Text from a store's records; may be null.</param>
    public static bool IsId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value) && !value.AsSpan().ContainsAnyExceptInRange('0', '9');

    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class GogInfoFile
{
    [JsonConverter(typeof(IdStringConverter))]
    public string? GameId { get; init; }

    [JsonConverter(typeof(IdStringConverter))]
    public string? RootGameId { get; init; }

    [JsonConverter(typeof(IdStringConverter))]
    public string? BuildId { get; init; }

    public string? Name { get; init; }

    /// <summary>The installed language as an English name, e.g. <c>Polish</c>.</summary>
    public string? Language { get; init; }

    /// <summary>The installed languages as tags, e.g. <c>pl-PL</c>.</summary>
    public IReadOnlyList<string>? Languages { get; init; }

    public IReadOnlyList<JsonElement>? PlayTasks { get; init; }
}

/// <summary>A <c>goggame-&lt;id&gt;.id</c> file, which names the build when the <c>.info</c> file next to it does not.</summary>
internal sealed class GogIdFile
{
    [JsonConverter(typeof(IdStringConverter))]
    public string? BuildId { get; init; }
}

/// <summary>Heroic's <c>gog_store\installed.json</c>.</summary>
internal sealed class HeroicInstalledFile
{
    public IReadOnlyList<HeroicInstalledGame>? Installed { get; init; }
}

/// <summary>An entry of Heroic's <c>gog_store\installed.json</c>.</summary>
internal sealed class HeroicInstalledGame
{
    [JsonConverter(typeof(IdStringConverter))]
    public string? AppName { get; init; }

    [JsonPropertyName("install_path")]
    public string? InstallPath { get; init; }

    [JsonPropertyName("is_dlc")]
    public bool IsDlc { get; init; }

    [JsonConverter(typeof(IdStringConverter))]
    public string? BuildId { get; init; }

    /// <summary><c>windows</c>, <c>linux</c> or <c>osx</c>: the build Heroic installed.</summary>
    public string? Platform { get; init; }

    /// <summary>The installed language as a tag, e.g. <c>pl-PL</c>.</summary>
    public string? Language { get; init; }
}

/// <summary>
/// Accepts ids written as JSON numbers too. Build ids exceed 2^53, so the number's own text is kept instead of going
/// through a double.
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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(GogInfoFile))]
[JsonSerializable(typeof(GogIdFile))]
[JsonSerializable(typeof(HeroicInstalledFile))]
internal sealed partial class GogJsonContext : JsonSerializerContext;
