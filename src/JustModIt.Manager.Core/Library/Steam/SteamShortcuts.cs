using System.Text;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// Reads the shortcuts of Steam's library (<c>userdata/&lt;account&gt;/config/shortcuts.vdf</c>): binary KeyValues with
/// inline keys, in which the object <c>shortcuts</c> holds one object per shortcut.
/// </summary>
internal static class SteamShortcuts
{
    /// <summary>Lists the shortcuts in the order of the file.</summary>
    /// <param name="data">The whole file.</param>
    /// <exception cref="InvalidDataException">The file is damaged or cut short.</exception>
    public static List<SteamShortcut> Read(ReadOnlySpan<byte> data)
    {
        var shortcuts = new List<SteamShortcut>();
        var reader = new BinaryKeyValues(data, keys: null);
        var depth = 0;
        var name = "";
        var program = "";
        while (reader.Read())
        {
            switch (reader.Kind)
            {
                case BinaryKeyValueKind.Object:
                    depth++;
                    break;
                case BinaryKeyValueKind.End:
                    if (depth == 2)
                    {
                        shortcuts.Add(new SteamShortcut(name, program));
                        name = program = "";
                    }

                    depth--;
                    break;
                case BinaryKeyValueKind.Text when depth == 2:
                    // Earlier clients wrote the keys in lower case.
                    if (reader.Key.Equals("AppName", StringComparison.OrdinalIgnoreCase))
                    {
                        name = Encoding.UTF8.GetString(reader.Text);
                    }
                    else if (reader.Key.Equals("Exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var exe = Encoding.UTF8.GetString(reader.Text);
                        program = exe is ['"', .. var quoted, '"'] ? quoted : exe;
                    }

                    break;
            }
        }

        return shortcuts;
    }
}

/// <summary>A shortcut in Steam's library.</summary>
/// <param name="Name">The name the library shows.</param>
/// <param name="Program">The program it starts, without the quotes Steam puts around it.</param>
public sealed record SteamShortcut(string Name, string Program);
