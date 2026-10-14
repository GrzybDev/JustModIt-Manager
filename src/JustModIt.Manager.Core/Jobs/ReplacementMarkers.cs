using System.Text.Encodings.Web;
using System.Text.Json;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>A replacement under way, such as an update, whose new install has not committed yet.</summary>
/// <param name="Previous">The record of the install it replaces, with every package it came from.</param>
/// <param name="Kind">What the replacing job does, which the job that puts the install back after a stop says too.</param>
/// <param name="Origin">Whether the player or the automation started the replacement.</param>
/// <param name="ReplacementProject">The project the new install comes from; null when it is not known.</param>
public sealed record ReplacementMarker(InstallRecord Previous, JobKind Kind, JobOrigin Origin, Guid? ReplacementProject)
{
    /// <summary>
    /// Whether an install shows that the replacement is over: the replaced install itself, after an uninstall that was
    /// undone, or an install of either project on the game, which the new install committed or a restore put back.
    /// </summary>
    /// <param name="record">An install the app has.</param>
    public bool IsSettledBy(InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Id == Previous.Id
            || (record.Game == Previous.Game && record.Source?.ProjectUuid is { } project
                && (project == Previous.Source?.ProjectUuid || project == ReplacementProject));
    }
}

/// <summary>
/// One file per replacement under way, written before the replaced install is uninstalled and removed once the new install
/// has committed or the replaced one was put back, so a start after a stop in between knows to put it back.
/// </summary>
public sealed partial class ReplacementMarkers
{
    private const int FormatVersion = 1;
    private const long MaxFileLength = 16L << 20;
    private const string Extension = ".json";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly ILogger<ReplacementMarkers> _logger;

    /// <summary>Keeps the markers in a folder of the app's data.</summary>
    /// <param name="directory">The folder, created with the first marker.</param>
    /// <param name="time">Times the retries of atomic writes.</param>
    /// <param name="logger">Logs markers that cannot be read or removed.</param>
    public ReplacementMarkers(string directory, TimeProvider time, ILogger<ReplacementMarkers> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _directory = directory;
        _time = time;
        _logger = logger;
    }

    /// <summary>Writes the marker of a replacement in one step.</summary>
    /// <param name="marker">The replacement.</param>
    /// <param name="cancellationToken">Stops the write, which leaves no marker.</param>
    public Task SaveAsync(ReplacementMarker marker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return AtomicFile.WriteJsonAsync(PathOf(marker.Previous.Id), WriterOptions, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WriteString("kind", marker.Kind.ToString());
            writer.WriteString("origin", marker.Origin.ToString());
            writer.WriteIfSet("replacementProject", marker.ReplacementProject);
            writer.WritePropertyName("previous");
            JsonSerializer.Serialize(writer, marker.Previous, InstallationJsonContext.Default.InstallRecord);
            writer.WriteEndObject();
        }, cancellationToken, _time);
    }

    /// <summary>Removes the marker of a replacement that is over, if there is one.</summary>
    /// <param name="recordId">The record id of the replaced install.</param>
    public void Remove(Guid recordId)
    {
        try
        {
            File.Delete(PathOf(recordId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next start finds the replacement settled by the installs it left and removes the marker then.
            LogRemoveFailed(recordId, exception);
        }
    }

    /// <summary>Every marker left by replacements that did not end, leaving out one that cannot be read.</summary>
    /// <param name="cancellationToken">Stops the reads.</param>
    public async Task<IReadOnlyList<ReplacementMarker>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var markers = new List<ReplacementMarker>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*" + Extension))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id))
            {
                continue;
            }

            try
            {
                var content = await SharedFileRead.ReadAllBytesAsync(path, MaxFileLength, cancellationToken).ConfigureAwait(false);
                if (Read(content) is { } marker && marker.Previous.Id == id)
                {
                    markers.Add(marker);
                }
                else
                {
                    LogUnreadable(id, null);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                or JsonException)
            {
                LogUnreadable(id, exception);
            }
        }

        return markers;
    }

    // Null for a marker of a newer app, whose record this version may not read whole, or one that is damaged.
    private static ReplacementMarker? Read(byte[] content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetInt32OrNull("formatVersion") is not FormatVersion
            || root.GetEnumOrNull<JobKind>("kind") is not { } kind
            || root.GetOrNull("previous", JsonValueKind.Object) is not { } previous
            || previous.Deserialize(InstallationJsonContext.Default.InstallRecord) is not { } record
            || record.FormatVersion > InstallRecord.CurrentFormatVersion)
        {
            return null;
        }

        return new ReplacementMarker(record, kind, root.GetEnumOrNull<JobOrigin>("origin") ?? JobOrigin.User,
            root.GetGuidOrNull("replacementProject"));
    }

    private string PathOf(Guid recordId) => Path.Join(_directory, recordId.ToString("N") + Extension);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The marker of the replacement of {RecordId} could not be removed")]
    private partial void LogRemoveFailed(Guid recordId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The marker of the replacement of {RecordId} cannot be read, so it is left as it is")]
    private partial void LogUnreadable(Guid recordId, Exception? exception);
}
