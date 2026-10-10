using System.Globalization;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Ui.Images;

/// <summary>The addresses of a film on YouTube, which a site's galleries and descriptions show, read as the site reads them.</summary>
public static partial class YouTube
{
    /// <summary>Tells whether a text is a film's id as YouTube gives them out.</summary>
    /// <param name="id">The text.</param>
    public static bool IsId(string? id) => id is not null && Id().IsMatch(id);

    /// <summary>The address of a film's still: the largest, which a few films lack, or the one every film has, letterboxed.</summary>
    /// <param name="id">The film's id.</param>
    /// <param name="largest">The largest still rather than the one every film has.</param>
    public static string Poster(string id, bool largest = true) => $"https://i.ytimg.com/vi/{id}/{(largest ? "maxres" : "hq")}default.jpg";

    /// <summary>The film's page, where it plays in the browser.</summary>
    /// <param name="id">The film's id.</param>
    /// <param name="start">Where it starts, in seconds.</param>
    public static Uri Watch(string id, int start = 0) =>
        new("https://www.youtube.com/watch?v=" + id + (start > 0 ? "&t=" + start.ToString(CultureInfo.InvariantCulture) + "s" : ""));

    /// <summary>Reads the film an embedded player shows, as the site embeds them; null for any other address.</summary>
    /// <param name="address">The player's address.</param>
    public static (string Id, int Start)? FromEmbed(string address)
    {
        if (Embed().Match(address) is not { Success: true } match)
        {
            return null;
        }

        var start = Start().Match(address) is { Success: true } time && int.TryParse(time.Groups["seconds"].ValueSpan, CultureInfo.InvariantCulture,
            out var seconds) ? seconds : 0;
        return (match.Groups["id"].Value, start);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex Id();

    [GeneratedRegex(@"^https://www\.youtube(?:-nocookie)?\.com/embed/(?<id>[A-Za-z0-9_-]{11})(?:[?#]|$)")]
    private static partial Regex Embed();

    [GeneratedRegex(@"[?&]start=(?<seconds>\d{1,6})(?:&|#|$)")]
    private static partial Regex Start();
}
