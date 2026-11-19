using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    Converters = [typeof(NullAsEmptyStringConverter), typeof(OperatingSystemKindConverter)])]
[JsonSerializable(typeof(ApiPage<CatalogueGame>))]
[JsonSerializable(typeof(ApiPage<GameDownloads>))]
[JsonSerializable(typeof(ApiPage<Team>))]
[JsonSerializable(typeof(IReadOnlyList<ProjectSummary>))]
[JsonSerializable(typeof(ProjectPage))]
[JsonSerializable(typeof(IReadOnlyList<ProjectStatus>))]
[JsonSerializable(typeof(IReadOnlyList<ProjectType>))]
[JsonSerializable(typeof(IReadOnlyList<Genre>))]
[JsonSerializable(typeof(IReadOnlyList<GamePlatform>))]
[JsonSerializable(typeof(IReadOnlyList<StorefrontPlatform>))]
[JsonSerializable(typeof(SiteConfiguration))]
[JsonSerializable(typeof(DownloadLink))]
[JsonSerializable(typeof(ReleaseDownloadLinks))]
[JsonSerializable(typeof(ApiKeyDetails))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ListingData))]
[JsonSerializable(typeof(IndexData))]
[JsonSerializable(typeof(IReadOnlyList<AchievementTexts>))]
internal sealed partial class CatalogueJsonContext : JsonSerializerContext;

/// <summary>
/// Reads a project's achievement texts leaving out the entries it cannot read, and anything but an array as none, so texts
/// the site starts sending in another shape never make the project's page unreadable.
/// </summary>
internal sealed class LenientAchievementsConverter : JsonConverter<IReadOnlyList<ProjectAchievements>>
{
    public override bool HandleNull => true;

    public override IReadOnlyList<ProjectAchievements> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = JsonElement.ParseValue(ref reader);
        if (list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var type = (JsonTypeInfo<ProjectAchievements>)options.GetTypeInfo(typeof(ProjectAchievements));
        var items = new List<ProjectAchievements>();
        foreach (var item in list.EnumerateArray())
        {
            try
            {
                if (item.Deserialize(type) is { } value)
                {
                    items.Add(value);
                }
            }
            catch (JsonException)
            {
                // The entry is left out.
            }
        }

        return items;
    }

    // Pages are kept as the site sent them, so the app never writes one.
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<ProjectAchievements> value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

/// <summary>
/// Reads a JSON null as empty text, which already means "not given" everywhere in the catalogue, so a field the backend
/// starts sending as null can never put a null where the code expects text.
/// </summary>
internal sealed class NullAsEmptyStringConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Null => "",
            _ => throw new JsonException($"Expected text, found {reader.TokenType}."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value ?? "");
}

/// <summary>
/// Reads systems the app does not know as <see cref="OperatingSystemKind.Unknown"/>, so a new one on the backend cannot
/// make a project unreadable; a package made only for such a system then serves nobody here.
/// </summary>
internal sealed class OperatingSystemKindConverter : JsonConverter<OperatingSystemKind>
{
    public override OperatingSystemKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return OperatingSystemKind.Unknown;
        }

        return reader.GetString() switch
        {
            "windows" => OperatingSystemKind.Windows,
            "macos" => OperatingSystemKind.MacOS,
            "linux" => OperatingSystemKind.Linux,
            _ => OperatingSystemKind.Unknown,
        };
    }

    public override void Write(Utf8JsonWriter writer, OperatingSystemKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            OperatingSystemKind.Windows => "windows",
            OperatingSystemKind.MacOS => "macos",
            OperatingSystemKind.Linux => "linux",
            _ => "unknown",
        });
}
