using System.Globalization;
using System.Text;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Folds text for searching (lower case, no diacritics, words separated by single spaces), so the catalogue is searched
/// here, since the site's own search folds nothing.
/// </summary>
public static class SearchText
{
    /// <summary>The folded form of a text.</summary>
    /// <param name="text">A name or the player's query.</param>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        var separate = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (!char.IsLetterOrDigit(character))
            {
                separate = folded.Length > 0;
                continue;
            }

            if (separate)
            {
                folded.Append(' ');
                separate = false;
            }

            folded.Append(BaseLetter(char.ToLowerInvariant(character)));
        }

        return folded.ToString();
    }

    /// <summary>The parts of a text that the words of a query match, in order and without overlaps, to highlight them.</summary>
    /// <param name="text">A name the search looked in.</param>
    /// <param name="query">The player's query.</param>
    public static IReadOnlyList<(int Start, int Length)> Find(string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        // One folded character per character of the text, so a match's position is its position in the text; anything
        // but a letter or a digit is a space, which no word holds.
        var folded = string.Concat(text.Select(character =>
        {
            if (char.IsSurrogate(character))
            {
                return ' ';
            }

            var letter = character.ToString().Normalize(NormalizationForm.FormD)[0];
            return char.IsLetterOrDigit(letter) ? BaseLetter(char.ToLowerInvariant(letter)) : ' ';
        }));
        var marked = new bool[text.Length];
        foreach (var word in words)
        {
            for (var at = folded.IndexOf(word, StringComparison.Ordinal); at >= 0; at = folded.IndexOf(word, at + 1, StringComparison.Ordinal))
            {
                Array.Fill(marked, true, at, word.Length);
            }
        }

        var parts = new List<(int Start, int Length)>();
        for (var start = 0; start < marked.Length; start++)
        {
            if (marked[start])
            {
                var end = Array.IndexOf(marked, false, start);
                end = end < 0 ? marked.Length : end;
                parts.Add((start, end - start));
                start = end;
            }
        }

        return parts;
    }

    // Letters with a stroke have no canonical decomposition, so stripping marks alone would keep them apart from their
    // base letter.
    private static char BaseLetter(char character) => character switch
    {
        (char)0x0142 => 'l',
        (char)0x0111 => 'd',
        (char)0x00F8 => 'o',
        (char)0x0127 => 'h',
        _ => character,
    };
}
