using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Library.Gog;

internal static class GogJson
{
    /// <summary>Tells whether <paramref name="value"/> is a GOG product id, which is digits only.</summary>
    /// <param name="value">Text from a store's records; may be null.</param>
    public static bool IsId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value) && !value.AsSpan().ContainsAnyExceptInRange('0', '9');
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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(GogInfoFile))]
[JsonSerializable(typeof(GogIdFile))]
[JsonSerializable(typeof(HeroicInstalledFile))]
internal sealed partial class GogJsonContext : JsonSerializerContext;
