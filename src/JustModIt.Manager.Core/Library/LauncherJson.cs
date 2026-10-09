using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library.Epic;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Heroic;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library;

/// <summary>Reads the JSON files that launchers keep about their installs.</summary>
internal static class LauncherJson
{
    /// <summary>The longest store or launcher file read, far above any real one, so a damaged file cannot fill the memory.</summary>
    public const long MaxFileLength = 16 * 1024 * 1024;

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
        var content = await fileSystem.ReadAllBytesAsync(path, MaxFileLength, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(Utf8Json.WithoutByteOrderMark(content.AsSpan()), typeInfo);
    }

    /// <summary>Reads a launcher's JSON file as <see cref="ReadAsync"/> does; null when there is no such file.</summary>
    /// <param name="fileSystem">Reads the file.</param>
    /// <param name="path">The file.</param>
    /// <param name="typeInfo">The source-generated contract of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<T?> ReadIfExistsAsync<T>(IFileSystem fileSystem, string path, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await ReadAsync(fileSystem, path, typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether reading a launcher's file failed because it is missing, locked, too large or caught mid-write, which the
    /// launcher's next write usually fixes.
    /// </summary>
    /// <param name="exception">What the read threw.</param>
    public static bool IsUnreadable(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException;

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

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(GogInfoFile))]
[JsonSerializable(typeof(GogIdFile))]
[JsonSerializable(typeof(HeroicInstalledFile))]
[JsonSerializable(typeof(HeroicDownloadManager))]
[JsonSerializable(typeof(EpicItem))]
[JsonSerializable(typeof(LauncherInstalledFile))]
[JsonSerializable(typeof(Dictionary<string, LegendaryInstalledGame>))]
[JsonSerializable(typeof(LegendaryMetadataFile))]
[JsonSerializable(typeof(EpicFolderManifest))]
internal sealed partial class LauncherJsonContext : JsonSerializerContext;
