using System.Text;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>Changes a game's launch options as the launch options script in Steam's interface changes them.</summary>
internal static partial class LaunchOptionsText
{
    /// <summary>
    /// The options after taking out whole options and adding the missing ones at the end, with everything else as the
    /// player wrote it.
    /// </summary>
    /// <param name="options">The options before.</param>
    /// <param name="add">The options to add where they are missing.</param>
    /// <param name="remove">The options to take out where they are present.</param>
    public static string Changed(string options, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove)
    {
        // Options and the spaces between them alternate, so a space the player wrote stays where it was.
        var parts = Separators().Split(options.Trim());
        var result = new StringBuilder();
        for (var index = 0; index < parts.Length; index += 2)
        {
            if (parts[index].Length == 0 || remove.Contains(parts[index]))
            {
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(parts[index - 1]);
            }

            result.Append(parts[index]);
        }

        foreach (var option in add.Where(option => !Separators().Split(result.ToString()).Contains(option)))
        {
            result.Append(result.Length > 0 ? " " : "").Append(option);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"(\s+)")]
    private static partial Regex Separators();
}
