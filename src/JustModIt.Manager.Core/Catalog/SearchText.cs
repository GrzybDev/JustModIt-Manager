using System.Globalization;
using System.Text;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Folds text for searching (lower case, no diacritics, words separated by single spaces), so the catalogue is searched
/// here, since the site's own search folds nothing.
/// </summary>
internal static class SearchText
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
