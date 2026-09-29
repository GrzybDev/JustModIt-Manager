using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// Finds every Steam installation. Linux can have several at once (native, Flatpak, Snap), and its conventional paths
/// are mostly symbolic links to one another, so candidates are deduplicated by canonical path.
/// </summary>
/// <param name="fileSystem">Reads the candidate folders.</param>
/// <param name="registry">Tells where Steam is installed, on Windows.</param>
/// <param name="environment">The user's folders Steam is found relative to.</param>
internal sealed class SteamRootLocator(IFileSystem fileSystem, IRegistry registry, LibraryEnvironment environment)
{
    public const string SteamKey = @"Software\Valve\Steam";

    private const string MachineSteamKey = @"SOFTWARE\Valve\Steam";
    private const string FlatpakAppId = "com.valvesoftware.Steam";

    /// <summary>Canonical roots that hold a <c>steamapps</c> folder, in order of preference.</summary>
    public IReadOnlyList<string> Locate()
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);
        foreach (var candidate in Candidates())
        {
            var root = fileSystem.GetCanonicalPath(candidate);
            if (SteamPaths.FindSteamApps(fileSystem, root) is not null && seen.Add(root))
            {
                roots.Add(root);
            }
        }

        return roots;
    }

    /// <summary>Where Steam may be installed, whether it is or not.</summary>
    public IEnumerable<string> Candidates() =>
        RawCandidates().Where(candidate => Path.IsPathFullyQualified(candidate) && !candidate.Contains('\0', StringComparison.Ordinal));

    private IEnumerable<string> RawCandidates() => environment.Platform switch
    {
        AppPlatform.Windows => WindowsCandidates(),
        AppPlatform.MacOS => [Path.Join(environment.HomeDirectory, "Library", "Application Support", "Steam")],
        _ => LinuxCandidates(),
    };

    private IEnumerable<string> WindowsCandidates()
    {
        // The per-user value comes first: it follows a moved Steam, while the machine-wide ones may be stale. It is
        // written in lower case with forward slashes.
        if (registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, SteamKey)?.GetText("SteamPath") is { Length: > 0 } steamPath)
        {
            yield return steamPath;
        }

        // The installer writes to the 32-bit view; some machines also have the 64-bit key.
        foreach (var view in (RegistryView[])[RegistryView.Registry32, RegistryView.Registry64])
        {
            if (registry.ReadKey(RegistryHive.LocalMachine, view, MachineSteamKey)?.GetText("InstallPath") is { Length: > 0 } installPath)
            {
                yield return installPath;
            }
        }

        if (environment.ProgramFilesX86 is { } programFiles)
        {
            yield return Path.Join(programFiles, "Steam");
        }
    }

    private IEnumerable<string> LinuxCandidates()
    {
        var home = environment.HomeDirectory;
        foreach (var candidate in SandboxCandidates(home, environment.XdgDataHome))
        {
            yield return candidate;
        }

        yield return Path.Join(home, ".steam", "debian-installation");

        foreach (var candidate in SandboxCandidates(Path.Join(home, ".var", "app", FlatpakAppId), dataHome: null))
        {
            yield return candidate;
        }

        var snapData = environment.SnapUserData ?? Path.Join(home, "snap");
        foreach (var candidate in SandboxCandidates(Path.Join(snapData, "steam", "common"), dataHome: null))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> SandboxCandidates(string home, string? dataHome) =>
    [
        Path.Join(dataHome ?? Path.Join(home, ".local", "share"), "Steam"),
        Path.Join(home, ".steam", "steam"),
        Path.Join(home, ".steam", "root"),
    ];
}
