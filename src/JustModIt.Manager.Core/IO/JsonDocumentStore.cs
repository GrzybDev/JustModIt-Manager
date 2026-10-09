using System.Collections.Immutable;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Stores a document as one JSON object with a schema version, moving an unreadable file aside so the next save neither
/// fails on it nor destroys it, and writing back unknown properties (e.g. a newer app's) so a downgrade loses nothing.
/// </summary>
public sealed class JsonDocumentStore<T> : IDocumentStore<T>
    where T : class
{
    public const string VersionProperty = "version";
    public const long MaxLength = 16 * 1024 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    // The files are only read by this app and by people, who should see letters rather than escapes.
    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly IJsonDocumentFormat<T> _format;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    // Filled by a load, so one owner loads before its first save and never loads and saves at once.
    private ImmutableArray<(string Name, JsonElement Value)> _unknownProperties = [];
    private int _storedVersion;

    /// <summary>Creates a store for the file at <paramref name="path"/>.</summary>
    /// <param name="path">The document's file.</param>
    /// <param name="format">Reads and writes the document's own properties.</param>
    /// <param name="time">Stamps the names of files moved aside and paces retries.</param>
    /// <param name="logger">Logs files that could not be read or moved aside.</param>
    public JsonDocumentStore(string path, IJsonDocumentFormat<T> format, TimeProvider time, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _path = Path.GetFullPath(path);
        _format = format;
        _time = time;
        _logger = logger;
    }

    private string DocumentName => Path.GetFileName(_path);

    public async Task<T?> LoadAsync(CancellationToken cancellationToken)
    {
        _unknownProperties = [];
        _storedVersion = 0;
        byte[] content;
        try
        {
            content = await TransientFileRetry.RunAsync(
                () => SharedFileRead.ReadAllBytesAsync(_path, MaxLength, cancellationToken), _time, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.DocumentUnreadable(DocumentName, exception);
            await MoveAsideAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        try
        {
            return Parse(content);
        }
        catch (JsonException exception)
        {
            _logger.DocumentUnreadable(DocumentName, exception);
            await MoveAsideAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    public Task SaveAsync(T document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var version = Math.Max(_format.CurrentVersion, _storedVersion);
        var unknownProperties = _unknownProperties;
        return AtomicFile.WriteJsonAsync(_path, WriteOptions, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionProperty, version);
            _format.Write(writer, document);
            foreach (var (name, value) in unknownProperties)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }, cancellationToken, _time);
    }

    private T Parse(ReadOnlyMemory<byte> content)
    {
        content = Utf8Json.WithoutByteOrderMark(content);
        // The parser checks UTF-8 only when a string is read, so a bad byte would otherwise fail outside this method.
        if (!Utf8.IsValid(content.Span))
        {
            throw new JsonException("The document is not valid UTF-8.");
        }

        using var json = JsonDocument.Parse(content, ReadOptions);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"The document is a JSON {root.ValueKind}, not an object.");
        }

        var version = root.GetInt32OrNull(VersionProperty) is { } number and >= 0 ? number : 0;
        var document = _format.Read(root, version);
        _storedVersion = version;
        _unknownProperties =
        [
            .. root.EnumerateObject()
                .Where(property => property.Name != VersionProperty && !_format.KnownProperties.Contains(property.Name))
                .Select(property => (property.Name, property.Value.Clone())),
        ];
        return document;
    }

    private async Task MoveAsideAsync(CancellationToken cancellationToken)
    {
        var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backup = $"{_path}.bak-{stamp}";
        for (var copy = 2; File.Exists(backup); copy++)
        {
            backup = $"{_path}.bak-{stamp}-{copy.ToString(CultureInfo.InvariantCulture)}";
        }

        try
        {
            await TransientFileRetry.RunAsync(() => File.Move(_path, backup), _time, cancellationToken).ConfigureAwait(false);
            _logger.DocumentMovedAside(DocumentName, Path.GetFileName(backup));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.DocumentNotMovedAside(DocumentName, exception);
        }
    }
}
