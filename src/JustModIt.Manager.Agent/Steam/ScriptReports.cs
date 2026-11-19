using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>What the scripts in Steam's interface report, read alike from the classic mode's calls and the plugin's reports.</summary>
internal static class ScriptReports
{
    /// <summary>Which of the hook's hooks are in place; null when the report holds none.</summary>
    /// <param name="hooks">The hook's report.</param>
    public static SteamUiHooks? Hooks(JsonElement? hooks) => hooks is { ValueKind: JsonValueKind.Object } found
        ? new SteamUiHooks(found.GetBooleanOrNull("achievements") ?? false, found.GetBooleanOrNull("notifications") ?? false)
        : null;

    /// <summary>The player's launch options by app id; empty when the report holds none.</summary>
    /// <param name="options">The launch options script's report.</param>
    public static ImmutableDictionary<string, string> LaunchOptions(JsonElement? options) => options is { ValueKind: JsonValueKind.Object } found
        ? found.EnumerateObject().Where(option => option.Value.ValueKind == JsonValueKind.String)
            .ToImmutableDictionary(option => option.Name, option => option.Value.GetString()!, StringComparer.Ordinal)
        : ImmutableDictionary<string, string>.Empty;
}
