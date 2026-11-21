using Avalonia.Media;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>What an install's row says of the launch options its project needs, the options drawn as code.</summary>
/// <param name="Tone">The line's colour.</param>
/// <param name="Icon">Its icon.</param>
/// <param name="Lead">The words before the options.</param>
/// <param name="Options">The options; empty when the sentence names none.</param>
/// <param name="Tail">The words after them.</param>
public sealed record LaunchOptionsLine(Tone Tone, Geometry Icon, string Lead, string Options, string Tail)
{
    /// <summary>The player took an option out, which the row offers to set again.</summary>
    public bool CanReapply { get; init; }

    public bool IsSuccess => Tone == Tone.Success;

    public bool IsInfo => Tone == Tone.Info;

    public bool IsWarning => Tone == Tone.Warning;
}

/// <summary>Launch options for the player to add in Steam by hand, since nothing can set them: Steam's steps and the options to copy.</summary>
/// <param name="Title">What the project needs.</param>
/// <param name="Steps">Where Steam keeps a game's launch options.</param>
/// <param name="Options">The options, to copy.</param>
public sealed record ManualLaunchOptions(string Title, string Steps, string Options)
{
    /// <summary>The copy button named with the options, for screen readers.</summary>
    public string CopyName => Text.Format(Strings.GamePage_CopyName, ("options", Options));
}

/// <summary>The words of an install's launch options, as its row and an uninstall's result say them.</summary>
internal static class LaunchOptionsLines
{
    /// <summary>The line under an install's facts; null when it needs none or the player sets them by hand.</summary>
    /// <param name="options">What the install's project needs of the launch options.</param>
    public static LaunchOptionsLine? Line(LaunchOptionsView? options) => options switch
    {
        { State: LaunchOptionsState.Applied } => Split(Tone.Success, Icons.Terminal, Strings.GamePage_LaunchOptionsApplied, options.Options),
        { State: LaunchOptionsState.WaitingForSteam } => Split(Tone.Info, Icons.Clock, Strings.GamePage_LaunchOptionsWaiting, Needed(options)),
        { State: LaunchOptionsState.RemovedByPlayer } =>
            Split(Tone.Warning, Icons.TriangleAlert, Strings.GamePage_LaunchOptionsMissing, Needed(options)) with { CanReapply = true },
        { State: LaunchOptionsState.Refused } => new LaunchOptionsLine(Tone.Warning, Icons.TriangleAlert, Strings.GamePage_LaunchOptionsRefused, "", ""),
        _ => null,
    };

    /// <summary>What the player adds in Steam by hand, since nothing can set the options; null for any other state.</summary>
    /// <param name="options">What the install's project needs of the launch options.</param>
    /// <param name="inGameMode">The window runs in Game Mode, whose Steam reaches a game's properties another way.</param>
    public static ManualLaunchOptions? Manual(LaunchOptionsView? options, bool inGameMode)
    {
        if (options is not { State: LaunchOptionsState.Manual })
        {
            return null;
        }

        var needed = Needed(options);
        List<string> overrides = [.. needed.Where(SteamLaunchOptions.IsWineOverride)];
        List<string> others = [.. needed.Where(option => option != CheckedLaunchOptions.GameCommand && !SteamLaunchOptions.IsWineOverride(option))];
        // Wine reads its overrides only before Steam's command line, and the game gets the options after it.
        IReadOnlyList<string> line = overrides.Count == 0 ? needed : [.. overrides, CheckedLaunchOptions.GameCommand, .. others];
        var count = ("count", (object?)needed.Count);
        var place = ("place", (object?)(overrides.Count == 0 ? "end" : others.Count == 0 ? "start" : "around"));
        return new ManualLaunchOptions(Text.Format(Strings.GamePage_LaunchOptionsManualTitle, count),
            Text.Format(Strings.GamePage_LaunchOptionsManual, count, place, OptionsPath(inGameMode)), string.Join(' ', line));
    }

    /// <summary>What an uninstall's result says of the launch options it takes out of Steam.</summary>
    /// <param name="options">The options the app added for the install that Steam still had.</param>
    /// <param name="steam">
    /// The way into Steam's interface: reached, the options are out already; out of reach, they go at the next chance; with
    /// none, the player takes them out.
    /// </param>
    /// <param name="inGameMode">The window runs in Game Mode, whose Steam reaches a game's properties another way.</param>
    public static string Removed(IReadOnlyList<string> options, SteamUiView steam, bool inGameMode) =>
        Text.Format(Strings.GamePage_LaunchOptionsRemoved, ("count", options.Count), ("options", string.Join(' ', options)),
            ("way", steam.Delivery == SteamUiDelivery.None || steam.PluginDisabled ? "manual" : steam.Reachable ? "reachable" : "waiting"),
            OptionsPath(inGameMode));

    // Game Mode's Steam has no context menu on a game, only the cog button on its page.
    private static (string, object?) OptionsPath(bool inGameMode) =>
        ("path", Text.Format(Strings.GamePage_LaunchOptionsPath, ("session", inGameMode ? "game" : "desktop")));

    // Only those Steam lacks, once the player's options were read.
    private static IReadOnlyList<string> Needed(LaunchOptionsView options) => options.Missing.Count > 0 ? options.Missing : options.Options;

    // The sentence split where the options stand, so they can be drawn as code.
    private static LaunchOptionsLine Split(Tone tone, Geometry icon, string pattern, IReadOnlyList<string> options)
    {
        var joined = string.Join(' ', options);
        var sentence = Text.Format(pattern, ("count", options.Count), ("options", joined));
        var at = sentence.IndexOf(joined, StringComparison.Ordinal);
        return at < 0
            ? new LaunchOptionsLine(tone, icon, sentence, "", "")
            : new LaunchOptionsLine(tone, icon, sentence[..at], joined, sentence[(at + joined.Length)..]);
    }
}
