using System.Text.RegularExpressions;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Steam;

/// <summary>
/// A Steam game's launch options as Steam runs them, and how the app changes them as the launch options script in Steam's
/// interface does: what stands before Steam's command line sets the game's environment, where Wine reads its library
/// overrides from the last assignment, whose entries the app shares with the player's own.
/// </summary>
public static partial class SteamLaunchOptions
{
    /// <summary>How an option for Wine's library overrides starts; the rest is one entry, such as <c>dinput8=n,b</c>.</summary>
    public const string WineOverrides = "WINEDLLOVERRIDES=";

    /// <summary>Whether an option is an entry of Wine's library overrides.</summary>
    /// <param name="option">One option.</param>
    public static bool IsWineOverride(string option) => option.StartsWith(WineOverrides, StringComparison.Ordinal);

    /// <summary>
    /// The options that take effect, word by word: with Steam's command line among them, the overrides Wine reads count
    /// entry by entry and every other assignment of them not at all.
    /// </summary>
    /// <param name="text">The options as the player's field holds them.</param>
    public static IReadOnlySet<string> Of(string text)
    {
        var words = Words(text);
        var command = words.FindIndex(word => word.Text == CheckedLaunchOptions.GameCommand);
        if (command < 0)
        {
            return words.Select(word => word.Text).ToHashSet(StringComparer.Ordinal);
        }

        var options = words.Select(word => word.Text).Where(word => !IsWineOverride(word)).ToHashSet(StringComparer.Ordinal);
        var applied = AppliedOverrides(words, command);
        if (applied >= 0 && Overrides.Read(words[applied].Text) is { } overrides)
        {
            options.UnionWith(overrides.Entries.Select(entry => WineOverrides + entry.Trim()));
        }

        return options;
    }

    /// <summary>
    /// The options after taking out whole options and adding the missing ones at the end, Steam's command line before the
    /// player's words, which all went to the game without it, and the overrides' entries in the assignment Wine reads, with
    /// everything else as the player wrote it.
    /// </summary>
    /// <param name="text">The options before.</param>
    /// <param name="add">The options to add where they are missing.</param>
    /// <param name="remove">The options to take out where they are present.</param>
    public static string Changed(string text, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove)
    {
        var words = Words(text);
        words.RemoveAll(word => remove.Contains(word.Text));
        List<string> adding = [.. add.Distinct()];
        List<string> overrides = [.. adding.Where(IsWineOverride)];
        foreach (var option in adding.Except(overrides).Where(option => option != CheckedLaunchOptions.GameCommand))
        {
            AddMissing(words, option);
        }

        var command = words.FindIndex(word => word.Text == CheckedLaunchOptions.GameCommand);
        if (command < 0 && adding.Contains(CheckedLaunchOptions.GameCommand))
        {
            command = words.TakeWhile(word => IsWineOverride(word.Text)).Count();
            words.Insert(command, new Word(" ", CheckedLaunchOptions.GameCommand));
        }

        if (command >= 0)
        {
            ChangeOverrides(words, command, [.. overrides.Select(EntryOf)], [.. remove.Where(IsWineOverride).Select(EntryOf)]);
        }
        else
        {
            // Without Steam's command line nothing sets the environment, so an override goes to the game like any option.
            foreach (var option in overrides)
            {
                AddMissing(words, option);
            }
        }

        return string.Concat(words.Select((word, index) => (index == 0 ? "" : word.Space.Length > 0 ? word.Space : " ") + word.Text));
    }

    private static string EntryOf(string option) => option[WineOverrides.Length..];

    private static void AddMissing(List<Word> words, string option)
    {
        if (!words.Exists(word => word.Text == option))
        {
            words.Add(new Word(" ", option));
        }
    }

    private static int AppliedOverrides(List<Word> words, int command) =>
        command == 0 ? -1 : words.FindLastIndex(command - 1, word => IsWineOverride(word.Text));

    private static void ChangeOverrides(List<Word> words, int command, List<string> adding, List<string> removing)
    {
        var applied = AppliedOverrides(words, command);
        if ((applied < 0 ? null : Overrides.Read(words[applied].Text)) is not { } overrides)
        {
            // After an assignment whose quotes the app cannot read, the app's own is the one Wine reads.
            if (adding.Count > 0)
            {
                words.Insert(applied + 1, new Word(" ", new Overrides(adding, null).ToString()));
            }

            return;
        }

        List<string> kept = [.. overrides.Entries.Where(entry => !removing.Contains(entry.Trim()))];
        kept.AddRange(adding.Where(entry => !kept.Exists(known => known.Trim() == entry)));
        if (kept.SequenceEqual(overrides.Entries))
        {
            return;
        }

        if (kept.Count == 0)
        {
            words.RemoveAt(applied);
        }
        else
        {
            words[applied] = words[applied] with { Text = (overrides with { Entries = kept }).ToString() };
        }
    }

    // Words split at spaces outside quotes, as the shell that runs the options splits them, each with the spaces before
    // it; where a quote is left open, which no shell runs, at every space.
    private static List<Word> Words(string text)
    {
        var trimmed = text.Trim();
        var words = Split(trimmed, QuotedWord());
        return string.Concat(words.Select(word => word.Space + word.Text)) == trimmed
            && words.TrueForAll(word => string.IsNullOrWhiteSpace(word.Space))
                ? words
                : Split(trimmed, PlainWord());
    }

    private static List<Word> Split(string text, Regex word)
    {
        List<Word> words = [];
        var end = 0;
        foreach (Match match in word.Matches(text))
        {
            words.Add(new Word(text[end..match.Index], match.Value));
            end = match.Index + match.Length;
        }

        return words;
    }

    [GeneratedRegex("""(?:[^\s"']+|"[^"]*"|'[^']*')+""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedWord();

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant)]
    private static partial Regex PlainWord();

    /// <summary>One word of the options and the spaces before it.</summary>
    /// <param name="Space">The spaces before the word as the player wrote them; one space for a word the app adds.</param>
    /// <param name="Text">The word.</param>
    private sealed record Word(string Space, string Text);

    /// <summary>An assignment of Wine's library overrides: its <c>;</c>-separated entries and the quote around them.</summary>
    /// <param name="Entries">The entries as written, empty ones left out.</param>
    /// <param name="Quote">The quote around the value; null for none.</param>
    private sealed record Overrides(List<string> Entries, char? Quote)
    {
        /// <summary>Reads an assignment; null for one whose quotes the app cannot read.</summary>
        /// <param name="word">The word that starts with <see cref="WineOverrides"/>.</param>
        public static Overrides? Read(string word)
        {
            var value = word[WineOverrides.Length..];
            char? quote = value.Length > 1 && value[0] is '"' or '\'' && value[^1] == value[0]
                && !value[1..^1].Contains(value[0], StringComparison.Ordinal) ? value[0] : null;
            if (quote is null && value.AsSpan().IndexOfAny('"', '\'') >= 0)
            {
                return null;
            }

            var inner = quote is null ? value : value[1..^1];
            return new Overrides([.. inner.Split(';').Where(entry => !string.IsNullOrWhiteSpace(entry))], quote);
        }

        // More than one entry needs quotes, since the shell ends a command at a semicolon.
        public override string ToString()
        {
            var quote = Quote?.ToString() ?? (Entries.Count > 1 ? "\"" : "");
            return WineOverrides + quote + string.Join(';', Entries) + quote;
        }
    }
}
