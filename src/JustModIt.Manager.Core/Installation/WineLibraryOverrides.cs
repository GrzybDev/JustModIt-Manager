using System.Collections.Frozen;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// The launch options a Steam game's Windows build needs on Linux for the loaders an install puts beside its program:
/// Proton's Wine loads its own copy of a system library before the game's, so a loader that stands in for one loads only
/// when the game's launch names it in <c>WINEDLLOVERRIDES</c>.
/// </summary>
internal static class WineLibraryOverrides
{
    /// <summary>
    /// The system libraries that the loaders the package creator's hosting rules know stand in for: the ASI loader's,
    /// BepInEx's winhttp and UE4SS's dwmapi.
    /// </summary>
    internal static readonly FrozenSet<string> ProxyLibraries = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "dinput8.dll", "dsound.dll", "winmm.dll", "version.dll", "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll",
        "dxgi.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "wininet.dll", "msacm32.dll", "winhttp.dll", "dwmapi.dll");

    /// <summary>
    /// The project's launch options with those the install's loaders need, which come first, since they stand before the
    /// game's command line; the options as they were when no loader needs any or the project's were refused.
    /// </summary>
    /// <param name="options">The options the project needs; null when it needs none.</param>
    /// <param name="game">The game the install is for.</param>
    /// <param name="platform">The system the app runs on.</param>
    /// <param name="files">What the install did to the game's files.</param>
    /// <param name="holdsProgram">
    /// Whether a game-relative folder, with '/' separators, holds a Windows program, whose own folder Windows loads its
    /// libraries from.
    /// </param>
    public static InstalledLaunchOptions? Add(InstalledLaunchOptions? options, InstalledGame game, AppPlatform platform,
        IEnumerable<InstalledFile> files, Func<string, bool> holdsProgram)
    {
        if (game.Key.Store != StoreId.Steam || game.Platform != OperatingSystemKind.Windows || platform != AppPlatform.Linux
            || options?.Refusal is not null)
        {
            return options;
        }

        List<string> loaders =
        [
            .. files.Where(file => file.Change is FileChange.Added or FileChange.Replaced)
                .Select(file => (Folder: file.Path[..Math.Max(file.Path.LastIndexOf(PackagePath.Separator), 0)],
                    Name: file.Path[(file.Path.LastIndexOf(PackagePath.Separator) + 1)..]))
                .Where(file => ProxyLibraries.Contains(file.Name) && (file.Folder.Length == 0 || holdsProgram(file.Folder)))
                .Select(file => Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant())
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];
        if (loaders.Count == 0)
        {
            return options;
        }

        string[] needed = [$"{SteamLaunchOptions.WineOverrides}{string.Join(',', loaders)}=n,b", CheckedLaunchOptions.GameCommand];
        return options is null
            ? new InstalledLaunchOptions([.. needed])
            : options with { Required = [.. needed.Union(options.Required, StringComparer.Ordinal)] };
    }
}
