using System.Globalization;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>Reads what the kernel tells of the running processes in the files of a <c>/proc</c> folder.</summary>
/// <param name="root">The <c>/proc</c> folder; tests lay out one of their own.</param>
internal sealed class ProcFiles(string root)
{
    private const string WindowsProgramExtension = ".exe";
    private const string DeletedMark = " (deleted)";
    private const string SteamAppIdVariable = "SteamAppId=";
    private const string SteamCompatAppIdVariable = "STEAM_COMPAT_APP_ID=";

    // Steam gives non-Steam shortcuts ids from 2^31 up, which name no Steam game.
    private const uint FirstShortcutId = 1u << 31;

    // The fields of /proc/<pid>/stat after the name, which ends at its last ')': the state, and the start time, field 22.
    private const int StateField = 0;
    private const int StartTimeField = 19;

    // Wine runs every Windows program on one of its loaders, which is what the kernel shows as the program.
    private static readonly string[] WineLoaders = ["wine-preloader", "wine64-preloader", "wine", "wine64"];

    /// <summary>The ids of the processes running now.</summary>
    public IEnumerable<int> ProcessIds()
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (int.TryParse(Path.GetFileName(folder.AsSpan()), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                yield return id;
            }
        }
    }

    /// <summary>The real user id a process runs as; null once it has exited.</summary>
    /// <param name="process">The process's id, or <c>self</c> for this one.</param>
    public int? UserOf(string process) => ReadStatus(process)?.UserId;

    /// <summary>Describes a process; null once it has exited or while it is a zombie, which holds nothing.</summary>
    /// <param name="processId">The process.</param>
    /// <param name="ownUserId">The user whose processes' environments can be read, which name the game they run for.</param>
    public ProcessEntry? Describe(int processId, int ownUserId)
    {
        var process = processId.ToString(CultureInfo.InvariantCulture);
        if (ReadStatus(process) is not { IsZombie: false } status)
        {
            return null;
        }

        var image = ImageOf(process, status.Name);
        var name = image is null ? status.Name : Path.GetFileName(image);
        if (name.EndsWith(WindowsProgramExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^WindowsProgramExtension.Length];
        }

        return new ProcessEntry(processId, name, status.UserId, image, status.UserId == ownUserId ? SteamGameOf(process) : null);
    }

    /// <summary>
    /// When a process started, in clock ticks after boot, which tells it from a later one given its id; null once it has
    /// exited or while it is a zombie.
    /// </summary>
    /// <param name="processId">The process.</param>
    public ulong? StartTimeOf(int processId)
    {
        string stat;
        try
        {
            stat = File.ReadAllText(Path.Join(root, processId.ToString(CultureInfo.InvariantCulture), "stat"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // The name may hold spaces and parentheses itself.
        var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > StartTimeField && fields[StateField] is not ("Z" or "X")
            && ulong.TryParse(fields[StartTimeField], NumberStyles.None, CultureInfo.InvariantCulture, out var started)
                ? started
                : null;
    }

    /// <summary>The files a process holds open, by the paths the kernel names them with; none once it has exited.</summary>
    /// <param name="processId">The process, which the user must own, since others' are not shown.</param>
    public IEnumerable<string> OpenFiles(int processId)
    {
        string[] descriptors;
        try
        {
            descriptors = Directory.GetFileSystemEntries(Path.Join(root, processId.ToString(CultureInfo.InvariantCulture), "fd"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return descriptors.Select(LinkTarget).OfType<string>();
    }

    private Status? ReadStatus(string process)
    {
        string text;
        try
        {
            text = File.ReadAllText(Path.Join(root, process, "status"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string? name = null;
        var state = string.Empty;
        int? userId = null;
        foreach (var line in text.Split('\n'))
        {
            if (Value(line, "Name:") is { } nameValue)
            {
                name = nameValue;
            }
            else if (Value(line, "State:") is { } stateValue)
            {
                state = stateValue;
            }
            // The real, effective, saved and file system ids, in that order.
            else if (Value(line, "Uid:") is { } ids
                && int.TryParse(ids.Split('\t')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                userId = id;
            }
        }

        return name is null || userId is null ? null : new Status(name, userId.Value, state.StartsWith('Z') || state.StartsWith('X'));
    }

    private string? ImageOf(string process, string name)
    {
        // Another user's process shows no program, and one replaced while it runs shows its old path with a mark.
        if (LinkTarget(Path.Join(root, process, "exe")) is not { Length: > 0 } image)
        {
            return null;
        }

        image = image.EndsWith(DeletedMark, StringComparison.Ordinal) ? image[..^DeletedMark.Length] : image;
        return WineLoaders.Contains(Path.GetFileName(image), StringComparer.Ordinal)
            ? WindowsProgramOf(process, name) ?? image
            : image;
    }

    // Wine names a process after its Windows program, cut to the kernel's 15 bytes, and maps the program's file; a process
    // may map more than its own program, as winedevice.exe maps ntoskrnl.exe. When the name fits none, the one program
    // mapped is taken.
    private string? WindowsProgramOf(string process, string name)
    {
        var programs = new List<string>();
        try
        {
            foreach (var line in File.ReadLines(Path.Join(root, process, "maps")))
            {
                if (MappedPath(line) is { } path && path.EndsWith(WindowsProgramExtension, StringComparison.OrdinalIgnoreCase)
                    && !programs.Contains(path, StringComparer.Ordinal))
                {
                    programs.Add(path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var whole = name.EndsWith(WindowsProgramExtension, StringComparison.OrdinalIgnoreCase);
        return programs.Find(path => whole
                ? Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)
                : Path.GetFileName(path).StartsWith(name, StringComparison.OrdinalIgnoreCase))
            ?? (programs.Count == 1 ? programs[0] : null);
    }

    private GameKey? SteamGameOf(string process)
    {
        string environment;
        try
        {
            environment = File.ReadAllText(Path.Join(root, process, "environ"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string? appId = null;
        string? compatAppId = null;
        foreach (var variable in environment.Split('\0'))
        {
            appId ??= Value(variable, SteamAppIdVariable);
            compatAppId ??= Value(variable, SteamCompatAppIdVariable);
        }

        return (SteamGame(appId) ?? SteamGame(compatAppId)) is { } id
            ? new GameKey(StoreId.Steam, id.ToString(CultureInfo.InvariantCulture))
            : null;
    }

    private static string? LinkTarget(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static uint? SteamGame(string? appId) =>
        uint.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id is > 0 and < FirstShortcutId
            ? id
            : null;

    // A line of /proc/<pid>/maps: the addresses, permissions, offset, device and inode, then the path, which may hold spaces.
    private static string? MappedPath(string line)
    {
        var rest = line.AsSpan();
        for (var field = 0; field < 5; field++)
        {
            rest = rest.TrimStart(' ');
            var end = rest.IndexOf(' ');
            if (end < 0)
            {
                return null;
            }

            rest = rest[end..];
        }

        rest = rest.TrimStart(' ');
        return rest.StartsWith('/') ? rest.ToString() : null;
    }

    private static string? Value(string line, string key) =>
        line.StartsWith(key, StringComparison.Ordinal) ? line[key.Length..].TrimStart('\t') : null;

    private sealed record Status(string Name, int UserId, bool IsZombie);
}
