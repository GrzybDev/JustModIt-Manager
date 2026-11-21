using System.Text;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>An action the app's entry offers in its menu beside starting the app, such as exiting it.</summary>
/// <param name="Id">The action's key in the entry.</param>
/// <param name="Name">What the menu calls it.</param>
/// <param name="Argument">The argument that makes the executable take the action.</param>
public sealed record DesktopAction(string Id, string Name, string Argument);

/// <summary>
/// The app's entry among the desktop's apps, with its icon, and its link scheme, which the entry declares and the user's
/// default applications give to it; files only, which the desktops read themselves.
/// </summary>
public sealed class DesktopEntry : ILinkRegistration
{
    private const string DefaultsSection = "[Default Applications]";

    private readonly string _desktopId;
    private readonly string _categories;
    private readonly IReadOnlyList<DesktopAction> _actions;
    private readonly Func<byte[]?> _icon;
    private readonly string _entry;
    private readonly string _icons;
    private readonly string _defaults;

    /// <summary>Creates the entry of one app and build.</summary>
    /// <param name="desktopId">Names the entry and its icon, as it names the app's window class.</param>
    /// <param name="categories">The menu categories the entry lists, each followed by a semicolon.</param>
    /// <param name="actions">The actions the entry offers beside starting the app.</param>
    /// <param name="icon">Reads the app's <c>.ico</c>, whose frames are PNG images; null for an app without one.</param>
    /// <param name="dataDirectory">The user's data folder (<c>XDG_DATA_HOME</c>), which holds the entries and icons.</param>
    /// <param name="configDirectory">The user's configuration folder (<c>XDG_CONFIG_HOME</c>), which holds the default applications.</param>
    public DesktopEntry(string desktopId, string categories, IReadOnlyList<DesktopAction> actions, Func<byte[]?> icon,
        string dataDirectory, string configDirectory)
    {
        _desktopId = desktopId;
        _categories = categories;
        _actions = actions;
        _icon = icon;
        _entry = FileOf(desktopId, dataDirectory);
        _icons = Path.Join(dataDirectory, "icons");
        _defaults = Path.Join(configDirectory, "mimeapps.list");
    }

    /// <summary>Where the entry of one app and build lies among the user's apps.</summary>
    /// <param name="desktopId">Names the entry.</param>
    /// <param name="dataDirectory">The user's data folder (<c>XDG_DATA_HOME</c>).</param>
    public static string FileOf(string desktopId, string dataDirectory) => Path.Join(dataDirectory, "applications", desktopId + ".desktop");

    public void Register(string scheme, string displayName, string executable)
    {
        WriteIfChanged(_entry, Text(scheme, displayName, executable));
        if (_icon() is { } icon)
        {
            AppIcon.WriteFrames(_icons, _desktopId, icon);
        }

        WriteIfChanged(_defaults, WithDefault(Read(_defaults), Handler(scheme), $"{_desktopId}.desktop"));
    }

    public void Unregister(string scheme)
    {
        if (Read(_defaults) is { } defaults)
        {
            WriteIfChanged(_defaults, WithDefault(defaults, Handler(scheme), handler: null));
        }

        File.Delete(_entry);
        AppIcon.Delete(_icons, _desktopId);
    }

    /// <summary>
    /// The entry's file, which a click in the desktop's menu or a link of the scheme runs, with its actions, such as exiting
    /// the app, which a desktop without a tray offers nowhere else.
    /// </summary>
    /// <param name="scheme">The app's link scheme.</param>
    /// <param name="displayName">The app's name in the menu.</param>
    /// <param name="executable">What the entry runs: the AppImage file of an installed copy.</param>
    internal string Text(string scheme, string displayName, string executable) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name={displayName}
        Exec={Quote(executable)} %u
        Icon={_desktopId}
        Terminal=false
        Categories={_categories}
        MimeType={Handler(scheme)};
        StartupWMClass={_desktopId}
        StartupNotify=true
        Actions={string.Concat(_actions.Select(action => action.Id + ";"))}

        """ + string.Concat(_actions.Select(action => $"""

        [Desktop Action {action.Id}]
        Name={action.Name}
        Exec={Quote(executable)} {action.Argument}

        """));

    /// <summary>
    /// The user's default applications with the handler of one type set, or taken away, every other line kept as it is.
    /// </summary>
    /// <param name="text">The file as it is; null when there is none.</param>
    /// <param name="type">The type whose handler changes.</param>
    /// <param name="handler">The entry that handles the type; null takes the type's line away.</param>
    internal static string WithDefault(string? text, string type, string? handler)
    {
        List<string> lines = text is null ? [] : [.. text.TrimEnd('\n').Split('\n')];
        var section = lines.FindIndex(line => line.Trim() == DefaultsSection);
        if (section < 0)
        {
            if (handler is not null)
            {
                lines.AddRange(lines.Count > 0 ? ["", DefaultsSection] : [DefaultsSection]);
                lines.Add($"{type}={handler}");
            }

            return Joined(lines);
        }

        var end = lines.FindIndex(section + 1, line => line.TrimStart().StartsWith('['));
        end = end < 0 ? lines.Count : end;
        var existing = lines.FindIndex(section + 1, end - section - 1, line => line.Split('=', 2)[0].Trim() == type);
        if (existing >= 0)
        {
            lines.RemoveAt(existing);
            end--;
        }

        if (handler is not null)
        {
            // After the section's last line rather than before the blank lines that part it from the next one.
            while (end > section + 1 && lines[end - 1].Trim().Length == 0)
            {
                end--;
            }

            lines.Insert(existing >= 0 ? existing : end, $"{type}={handler}");
        }

        return Joined(lines);
    }

    private static string Handler(string scheme) => $"x-scheme-handler/{scheme}";

    // The Desktop Entry Specification's quoting of an argument, then its escaping of a string and of field codes.
    private static string Quote(string argument)
    {
        var quoted = new StringBuilder("\"");
        foreach (var character in argument)
        {
            if (character is '"' or '`' or '$' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(character);
        }

        return quoted.Append('"').ToString().Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
    }

    private static string Joined(List<string> lines) => lines.Count == 0 ? "" : string.Join('\n', lines) + "\n";

    private static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    // Links are registered at every start, which mostly finds the files as they are.
    private static void WriteIfChanged(string path, string text) =>
        AtomicFile.WriteIfChangedAsync(path, Encoding.UTF8.GetBytes(text), CancellationToken.None).GetAwaiter().GetResult();
}
