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
