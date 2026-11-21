using System.Globalization;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>What the Linux Steam client keeps of itself in its folder.</summary>
/// <param name="ProcessId">The client's process; it stays recorded after Steam exits, and null when none is.</param>
/// <param name="Language">Steam's interface language by its API name; null when none can be read now.</param>
public sealed record SteamClientFiles(int? ProcessId, string? Language);

/// <summary>Where Steam is installed, for the parts of the app beyond the library that work with Steam's own folder.</summary>
public static class SteamInstallation
{
    /// <summary>The file in the Linux client's folder that names its process.</summary>
    public const string ProcessFile = "steam.pid";

    /// <summary>The file in the Linux client's folder that keeps its settings as Windows keeps them in the registry.</summary>
    public const string RegistryFile = "registry.vdf";

    /// <summary>
    /// The folder Steam is installed in; null when it is not installed. On Linux only the client behind <c>~/.steam</c>
    /// counts, since a Flatpak or Snap Steam runs in a sandbox of its own.
    /// </summary>
    /// <param name="fileSystem">Reads the candidate folders.</param>
    /// <param name="registry">Tells where Steam is installed, on Windows.</param>
    /// <param name="environment">The user's folders Steam is found relative to.</param>
    public static string? FindRoot(IFileSystem fileSystem, IRegistry registry, LibraryEnvironment environment)
    {
        var locator = new SteamRootLocator(fileSystem, registry, environment);
        IEnumerable<string> candidates = ClientFolder(environment) is { } client
            ? [Path.Join(client, "root"), Path.Join(client, "steam")]
            : locator.Candidates();
        return candidates.Select(locator.Probe).FirstOrDefault(root => root is not null);
    }

    /// <summary>The Linux client's own folder, <c>~/.steam</c>; null on other systems.</summary>
    /// <param name="environment">The user's folders.</param>
    public static string? ClientFolder(LibraryEnvironment environment) =>
        environment.Platform == AppPlatform.Linux ? Path.Join(environment.HomeDirectory, ".steam") : null;

    /// <summary>
    /// The Linux client's own program, which hands a link to the running client and starts a closed one with it, and needs
    /// no display for either: <c>steam</c> on <c>PATH</c>, as SteamOS's own tools call it, else the client's
    /// <c>steam.sh</c>; null on other systems and without the client behind <c>~/.steam</c>.
    /// </summary>
    /// <param name="fileSystem">Looks for the program.</param>
    /// <param name="environment">The user's folders and the program folders.</param>
    public static string? ClientProgram(IFileSystem fileSystem, LibraryEnvironment environment)
    {
        if (ClientFolder(environment) is not { } folder || !fileSystem.DirectoryExists(folder))
        {
            return null;
        }

        return environment.ProgramFolders.Select(programs => Path.Join(programs, "steam"))
            .Concat([Path.Join(folder, "root", "steam.sh"), Path.Join(folder, "steam", "steam.sh")])
            .FirstOrDefault(fileSystem.FileExists);
    }

    /// <summary>The file in Steam's folder that lists the accounts that signed in, which names the one signed in last.</summary>
    /// <param name="root">The folder Steam is installed in.</param>
    public static string AccountsFile(string root) => Path.Join(root, "config", "loginusers.vdf");

    /// <summary>The file of the shortcuts in the library of the account that signed in to Steam last.</summary>
    /// <param name="fileSystem">Reads the list of accounts.</param>
    /// <param name="root">The folder Steam is installed in.</param>
    /// <returns>Null when no account signed in.</returns>
    /// <exception cref="InvalidDataException">The list of accounts is damaged, or cut short while Steam writes it.</exception>
    public static string? ShortcutsFile(IFileSystem fileSystem, string root)
    {
        try
        {
            return ReadText(fileSystem, AccountsFile(root)) switch
            {
                null => null,
                // Steam rewrites the list in place, so one it just emptied names no account yet.
                "" => throw new InvalidDataException("The list of Steam's accounts is empty."),
                var accounts => SteamAccounts.Recent(accounts) is { } account
                    ? Path.Join(root, "userdata", account.ToString(CultureInfo.InvariantCulture), "config", "shortcuts.vdf")
                    : null,
            };
        }
        catch (VdfFormatException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    /// <summary>Reads the shortcuts of a library, which only Steam writes.</summary>
    /// <param name="fileSystem">Reads the file.</param>
    /// <param name="file">The library's shortcuts file.</param>
    /// <returns>The shortcuts in the file's order; none when the library has no file of them.</returns>
    /// <exception cref="InvalidDataException">The file is damaged, or cut short while Steam writes it.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static IReadOnlyList<SteamShortcut> ReadShortcuts(IFileSystem fileSystem, string file)
    {
        using var bytes = new MemoryStream();
        try
        {
            using var stream = fileSystem.OpenRead(file, FileShare.ReadWrite | FileShare.Delete);
            stream.CopyTo(bytes);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        return SteamShortcuts.Read(bytes.GetBuffer().AsSpan(0, (int)bytes.Length));
    }

    /// <summary>Reads what the Linux client keeps of itself in its folder.</summary>
    /// <param name="fileSystem">Reads the files.</param>
    /// <param name="environment">The user's folders.</param>
    public static SteamClientFiles ReadClientFiles(IFileSystem fileSystem, LibraryEnvironment environment)
    {
        if (ClientFolder(environment) is not { } folder)
        {
            return new SteamClientFiles(null, null);
        }

        var processId = int.TryParse(ReadText(fileSystem, Path.Join(folder, ProcessFile)), NumberStyles.None, CultureInfo.InvariantCulture,
            out var id) && id > 0 ? id : (int?)null;
        return new SteamClientFiles(processId, Language(ReadText(fileSystem, Path.Join(folder, RegistryFile))));
    }

    // Steam rewrites the file in place, so a read may meet half of it, or none when it was just emptied.
    private static string? Language(string? registry)
    {
        try
        {
            return registry is null ? null
                : VdfParser.Parse(registry)["Registry"]?["HKCU"]?["Software"]?["Valve"]?["Steam"]?.GetString("language");
        }
        catch (VdfFormatException)
        {
            return null;
        }
    }

    private static string? ReadText(IFileSystem fileSystem, string path)
    {
        try
        {
            using var reader = new StreamReader(fileSystem.OpenRead(path, FileShare.ReadWrite | FileShare.Delete));
            return reader.ReadToEnd().Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
