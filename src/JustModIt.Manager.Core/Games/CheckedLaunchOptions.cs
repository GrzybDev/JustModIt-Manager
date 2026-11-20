using System.Text.RegularExpressions;
using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Games;

/// <summary>Why the app refuses the launch options a project asks for.</summary>
public enum LaunchOptionsRefusal
{
    /// <summary>
    /// An option holds a character other than letters, digits and <c>-+/_.=:,</c>, such as a quote, a percent sign or a
    /// shell operator, with which the store could start a program.
    /// </summary>
    UnsafeOption,
}

/// <summary>The launch options a project needs on a store's copy of a game, as the app accepts them, or why it refuses them.</summary>
/// <param name="Options">The options in the order given, each once; empty when there are none or they are refused.</param>
/// <param name="Refusal">Why the options were refused as a whole; null when they were accepted.</param>
public sealed partial record CheckedLaunchOptions(ValueList<string> Options, LaunchOptionsRefusal? Refusal = null)
{
    /// <summary>
    /// Steam's stand-in for the game's own command line among its launch options: what comes before it, such as variables for
    /// the game's program, wraps the game, and what follows goes to the game.
    /// </summary>
    public const string GameCommand = "%command%";

    /// <summary>
    /// Splits space-separated launch options, refusing them all when any one is not plain, since Steam runs a game's options
    /// as a command line.
    /// </summary>
    /// <param name="value">The options as a project gives them; empty for none.</param>
    public static CheckedLaunchOptions Check(string value)
    {
        var options = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return options.All(Plain().IsMatch)
            ? new CheckedLaunchOptions([.. options.Distinct(StringComparer.Ordinal)])
            : new CheckedLaunchOptions([], LaunchOptionsRefusal.UnsafeOption);
    }

    // \z rather than $, which also matches before a final line break.
    [GeneratedRegex(@"^[-+/A-Za-z0-9_.=:,]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Plain();
}
