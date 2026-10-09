using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Reads <c>SOFTWARE\GOG.com\Games</c>, which both GOG Galaxy and GOG's offline installers write, then the installers'
/// Inno Setup uninstall entries for games whose GOG.com key is missing. Galaxy's own database tells which of them Galaxy
/// manages and what it is doing with them.
/// </summary>
/// <param name="registry">Reads the registry records.</param>
/// <param name="galaxy">Null where Galaxy's database cannot be located.</param>
public sealed class GogRegistrySource(IRegistry registry, GalaxyDatabase? galaxy = null) : IGogInstallSource
{
    /// <summary>The <see cref="LibraryLocation.Id"/> and <see cref="LibraryLocation.Source"/> of the registry records.</summary>
    public const string LibrarySource = "gog-registry";

    internal const string GogKey = @"SOFTWARE\GOG.com";
    internal const string GamesKey = @"SOFTWARE\GOG.com\Games";
    internal const string GalaxyClientKey = @"SOFTWARE\GOG.com\GalaxyClient";
    internal const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string InnoSetupSuffix = "_is1";

    // GOG's 32-bit installers write to the 32-bit view (WOW6432Node); duplicates across views are merged later.
    private static readonly (RegistryHive Hive, RegistryView View)[] SearchOrder =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.CurrentUser, RegistryView.Default),
    ];

    public async Task<GogSourceScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The uninstall entries are not watched: every program changes them, and they only back up a missing GOG.com key.
        var watch = new WatchPlan(galaxy?.Watch is { } database ? [database] : [],
            [.. SearchOrder.Select(location => new WatchedRegistryKey(location.Hive, location.View, GamesKey, IncludeSubkeys: true))]);
        if (!SearchOrder.Any(location => registry.ReadKey(location.Hive, location.View, GogKey) is not null))
        {
            return new GogSourceScan([], [], watch);
        }

        var records = new List<GogInstallRecord>();
        foreach (var (hive, view) in SearchOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.AddRange(ReadSubkeys(hive, view, GamesKey, GogJson.IsId).Select(FromGamesKey).OfType<GogInstallRecord>());
        }

        foreach (var (hive, view) in SearchOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.AddRange(ReadSubkeys(hive, view, UninstallKey, name => UninstallGameId(name) is not null)
                .Select(FromUninstallKey).OfType<GogInstallRecord>());
        }

        if (galaxy is not null && SearchOrder.Any(location => registry.ReadKey(location.Hive, location.View, GalaxyClientKey) is not null))
        {
            GalaxyGames games;
            try
            {
                games = await context.Access.ReadAsync(galaxy.StorageDirectory, _ => Task.FromResult(galaxy.Read()), cancellationToken)
                    .ConfigureAwait(false);
            }
            // Usually caught while Galaxy writes it; the games it listed are kept until it can be read again.
            catch (IOException)
            {
                return new GogSourceScan([Library(LibraryState.Unreadable)], [], watch);
            }

            records = [.. records.Select(record => record with
            {
                Launch = games.Manages(record.GameId, record.InstallPath)
                    ? LaunchLink.TryCreate($"goggalaxy://openGameView/{record.GameId}")
                    : null,
                Activity = games.Jobs.TryGetValue(record.GameId, out var job) ? job : null,
            })];
        }

        return new GogSourceScan([Library(LibraryState.Online)], records, watch);
    }

    private static LibraryLocation Library(LibraryState state) => new(LibrarySource, LibrarySource, Path: null, state);

    // Names are checked before a key is opened, so hundreds of unrelated uninstall entries cost only a comparison.
    private IEnumerable<RegistryKeySnapshot> ReadSubkeys(RegistryHive hive, RegistryView view, string path, Func<string, bool> includeName) =>
        registry.ReadKey(hive, view, path)?.SubKeyNames
            .Where(includeName)
            .Select(name => registry.ReadKey(hive, view, $@"{path}\{name}"))
            .OfType<RegistryKeySnapshot>()
        ?? [];

    private static GogInstallRecord? FromGamesKey(RegistryKeySnapshot key)
    {
        if (Text(key, "path", "workingDir") is not { } path)
        {
            return null;
        }

        var gameId = Text(key, "gameID") is { } value && GogJson.IsId(value) ? value : KeyName(key);
        return new GogInstallRecord(gameId, path, LibrarySource)
        {
            Name = Text(key, "gameName"),
            BuildId = Text(key, "buildId"),
            IsDlc = Text(key, "dependsOn") is not null,
            Language = Text(key, "language"),
            Platform = OperatingSystemKind.Windows,
        };
    }

    private static GogInstallRecord? FromUninstallKey(RegistryKeySnapshot key)
    {
        if (UninstallGameId(KeyName(key)) is not { } gameId
            || !string.Equals(Text(key, "Publisher"), "GOG.com", StringComparison.OrdinalIgnoreCase)
            || Text(key, "InstallLocation") is not { } path)
        {
            return null;
        }

        return new GogInstallRecord(gameId, path, LibrarySource)
        {
            Name = Text(key, "DisplayName"),
            Platform = OperatingSystemKind.Windows,
        };
    }

    // Only "<digits>_is1" names qualify, which rules out GOG's "GOGPACK…" bundles and MSI entries.
    private static string? UninstallGameId(string keyName) =>
        keyName.EndsWith(InnoSetupSuffix, StringComparison.OrdinalIgnoreCase) && keyName[..^InnoSetupSuffix.Length] is var gameId
        && GogJson.IsId(gameId)
            ? gameId
            : null;

    private static string KeyName(RegistryKeySnapshot key) => key.Path[(key.Path.LastIndexOf('\\') + 1)..];

    // Older installers write PATH, GAMENAME and BUILDID; the snapshot ignores letter case. A blank value counts as absent.
    private static string? Text(RegistryKeySnapshot key, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (GogJson.NullIfBlank(key.GetText(name)) is { } text)
            {
                return text;
            }
        }

        return null;
    }
}
