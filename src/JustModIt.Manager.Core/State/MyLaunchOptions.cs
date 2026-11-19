using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Core.State;

/// <summary>Whether Steam has the launch options an install's project needs.</summary>
public enum LaunchOptionsState
{
    /// <summary>The player's options in Steam have every one of them.</summary>
    Applied,

    /// <summary>The app sets them as soon as Steam's interface can be reached, at the latest when Steam starts.</summary>
    WaitingForSteam,

    /// <summary>Neither Millennium nor the classic mode can set them, so the player adds them in Steam.</summary>
    Manual,

    /// <summary>The player took out an option the app had set or found, which the app puts back only when asked to.</summary>
    RemovedByPlayer,

    /// <summary>The site gave options that could start a program, so the app sets none.</summary>
    Refused,
}

/// <summary>The launch options an install's project needs, and whether Steam has them.</summary>
/// <param name="State">Whether Steam has them.</param>
/// <param name="Options">The options the project needs; empty when they were refused.</param>
/// <param name="Missing">Those of them the player's options in Steam lack, as last read; empty while unknown.</param>
public sealed record MyLaunchOptions(LaunchOptionsState State, ValueList<string> Options, ValueList<string> Missing)
{
    /// <summary>What an install's project needs of the launch options and whether Steam has it; null when it needs none.</summary>
    /// <param name="record">The install's record.</param>
    /// <param name="steam">What the app knows of its way into Steam's interface and of the player's options there.</param>
    internal static MyLaunchOptions? For(InstallRecord record, SteamUiStatus steam)
    {
        if (record.LaunchOptions is not { } options)
        {
            return null;
        }

        if (options.Refusal is not null)
        {
            return new MyLaunchOptions(LaunchOptionsState.Refused, [], []);
        }

        if (options.Required.Count == 0)
        {
            return null;
        }

        var current = steam.LaunchOptionsOf(record.Game);
        ValueList<string> missing = current is null ? [] : [.. options.Required.Where(option => !current.Contains(option))];
        var state = current is not null && missing.Count == 0 ? LaunchOptionsState.Applied
            : missing.Any(options.WasSeen) ? LaunchOptionsState.RemovedByPlayer
            : !steam.CanDeliver ? LaunchOptionsState.Manual
            // Seen before and not read since, e.g. while Steam is closed.
            : current is null && options.Required.All(options.WasSeen) ? LaunchOptionsState.Applied
            : LaunchOptionsState.WaitingForSteam;
        return new MyLaunchOptions(state, options.Required, missing);
    }
}
