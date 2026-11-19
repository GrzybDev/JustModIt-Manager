using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>The app's way into Steam's interface as the window tells it, without the launch options it reads there.</summary>
public sealed record SteamUiView
{
    /// <inheritdoc cref="SteamUiStatus.Delivery"/>
    public SteamUiDelivery Delivery { get; init; }

    /// <inheritdoc cref="SteamUiStatus.Reachable"/>
    public bool Reachable { get; init; }

    /// <inheritdoc cref="SteamUiStatus.RestartNeeded"/>
    public bool RestartNeeded { get; init; }

    /// <inheritdoc cref="SteamUiStatus.PortTaken"/>
    public bool PortTaken { get; init; }

    /// <inheritdoc cref="SteamUiStatus.Hooks"/>
    public SteamUiHooks? Hooks { get; init; }

    /// <inheritdoc cref="SteamUiStatus.MillenniumVersion"/>
    public string MillenniumVersion { get; init; } = "";

    /// <inheritdoc cref="SteamUiStatus.PluginVersion"/>
    public string PluginVersion { get; init; } = "";

    /// <inheritdoc cref="SteamUiStatus.PluginDisabled"/>
    public bool PluginDisabled { get; init; }

    /// <inheritdoc cref="SteamUiStatus.IsNeeded"/>
    public bool IsNeeded { get; init; }

    /// <inheritdoc cref="SteamUiStatus.RunningGame"/>
    public GameKey? RunningGame { get; init; }

    /// <summary>The name of <see cref="RunningGame"/> when the player's list has it; null otherwise.</summary>
    public string? RunningGameName { get; init; }

    /// <summary>Maps the way into Steam's interface the engine's state knows of.</summary>
    /// <param name="state">The engine's state, whose games name the running one.</param>
    public static SteamUiView From(AppState state)
    {
        var steam = state.SteamUi;
        return new SteamUiView
        {
            Delivery = steam.Delivery,
            Reachable = steam.Reachable,
            RestartNeeded = steam.RestartNeeded,
            PortTaken = steam.PortTaken,
            Hooks = steam.Hooks,
            MillenniumVersion = steam.MillenniumVersion,
            PluginVersion = steam.PluginVersion,
            PluginDisabled = steam.PluginDisabled,
            IsNeeded = steam.IsNeeded,
            RunningGame = steam.RunningGame,
            RunningGameName = steam.RunningGame is { } running ? state.Find(running)?.Name : null,
        };
    }
}
