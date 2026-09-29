using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Downloads;

/// <summary>
/// The sidecar of a download, keeping the site's address and the resume validator while partial, so a resume counts no
/// second download, and what it was verified against once finished, so it is reused without the site.
/// </summary>
internal sealed record DownloadRecord
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public long? Size { get; init; }

    [JsonPropertyName("etag")]
    public string? ETag { get; init; }

    public string? Md5 { get; init; }

    public string? Sha256 { get; init; }

    public string? Address { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DownloadRecord))]
internal sealed partial class DownloadJsonContext : JsonSerializerContext;
