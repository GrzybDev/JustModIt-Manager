using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Steam;

/// <summary>How the app reaches Steam's interface to show achievement texts and change launch options.</summary>
public enum SteamUiDelivery
{
    /// <summary>Not at all: Millennium is not installed and the classic mode is off, or Steam is not there.</summary>
    None,

    /// <summary>Through the app's plugin, which Millennium loads into Steam's interface without opening a port.</summary>
    Millennium,

    /// <summary>Through Steam's local developer tools port, which Steam opens at its start while the app's switch is there.</summary>
    Classic,
}

/// <summary>What the hook in Steam's interface could wrap the last time it ran.</summary>
/// <param name="Achievements">The achievement views take the texts.</param>
/// <param name="Notifications">The unlock notifications take the texts.</param>
public sealed record SteamUiHooks(bool Achievements, bool Notifications);

/// <summary>What the app knows of its way into Steam's interface, compared by value, so an unchanged status changes nothing.</summary>
public sealed record SteamUiStatus
{
    /// <summary>No way into Steam's interface.</summary>
    public static SteamUiStatus None { get; } = new();

    public SteamUiDelivery Delivery { get; init; }

    /// <summary>The hook runs in Steam's interface now, so texts and launch options change at once.</summary>
    public bool Reachable { get; init; }

    /// <summary>The plugin or the classic mode's switch was written while Steam ran, so it takes a new start of Steam.</summary>
    public bool RestartNeeded { get; init; }

    /// <summary>Another program held the classic mode's port when Steam started, so Steam could not open it.</summary>
    public bool PortTaken { get; init; }

    /// <summary>The language of Steam's interface by Steam's API name, such as <c>english</c>; empty while unknown.</summary>
    public string InterfaceLanguage { get; init; } = "";

    /// <summary>What the hook could wrap; null while unknown.</summary>
    public SteamUiHooks? Hooks { get; init; }

    /// <summary>The player's launch options as Steam has them, read for the games the app manages them for, by game.</summary>
    public ValueDictionary<GameKey, string> LaunchOptions { get; init; }

    /// <summary>The Steam game running now, which a new start of Steam would close; null when none runs.</summary>
    public GameKey? RunningGame { get; init; }

    /// <summary>The installed Millennium's version; empty when it is not installed.</summary>
    public string MillenniumVersion { get; init; } = "";

    /// <summary>The version of the app's plugin in Millennium; empty when it is not installed.</summary>
    public string PluginVersion { get; init; } = "";

    /// <summary>
    /// The player turned the app's plugin off in Millennium's list of plugins, which the app leaves as it is, so nothing
    /// reaches Steam's interface.
    /// </summary>
    public bool PluginDisabled { get; init; }

    /// <summary>
    /// A game's achievement texts are on or an install's launch options are managed, so the plugin or the classic mode's
    /// switch is worth having in Steam's folder.
    /// </summary>
    public bool IsNeeded { get; init; }

    /// <summary>Millennium or the classic mode can bring texts and launch options into Steam's interface.</summary>
    public bool CanDeliver => Delivery != SteamUiDelivery.None && !PluginDisabled;

    /// <summary>The player's launch options of a game, option by option; null while they were not read.</summary>
    /// <param name="game">The Steam game.</param>
    public IReadOnlySet<string>? LaunchOptionsOf(GameKey game) =>
        LaunchOptions.TryGetValue(game, out var options)
            ? options.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            : null;
}
