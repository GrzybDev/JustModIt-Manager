using System.Text.Json;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Reads the <c>installed.json</c> of one Heroic Games Launcher <c>gog_store</c> folder. Heroic writes it only after an
/// install finishes, so downloads in progress are recognised by gogdl's markers in the game folder instead.
/// </summary>
public sealed class HeroicGogSource(IFileSystem fileSystem, string storeDirectory) : IGogInstallSource
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of Heroic's list of GOG installs.</summary>
    public const string LibrarySource = "heroic";

    private string InstalledFile => Path.Join(storeDirectory, "installed.json");

    /// <summary>Heroic's <c>gog_store</c> folders on this platform, one per way Heroic can be installed.</summary>
    public static IReadOnlyList<string> StoreDirectories(LibraryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string GogStore(string configRoot) => Path.Join(configRoot, "heroic", "gog_store");
        var home = environment.HomeDirectory;
        return environment.Platform switch
        {
            AppPlatform.Windows => environment.ApplicationData is { } appData ? [GogStore(appData)] : [],
            AppPlatform.MacOS => [GogStore(Path.Join(home, "Library", "Application Support"))],
            _ =>
            [
                GogStore(environment.XdgConfigHome ?? Path.Join(home, ".config")),
                GogStore(Path.Join(home, ".var", "app", "com.heroicgameslauncher.hgl", "config")),
            ],
        };
    }

    public Task<GogSourceScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Access.ReadAsync(InstalledFile, ReadInstalledAsync, cancellationToken);
    }

    private async Task<GogSourceScan> ReadInstalledAsync(CancellationToken cancellationToken)
    {
        // Heroic may be installed, or signed in to GOG, only later; each scan watches one folder closer to the file.
        var watch = new WatchPlan(WatchedFolder.Toward(fileSystem, InstalledFile) is { } toward ? [toward] : [], []);
        if (!fileSystem.FileExists(InstalledFile))
        {
            return new GogSourceScan([], [], watch);
        }

        var file = fileSystem.GetCanonicalPath(InstalledFile);
        IReadOnlyList<HeroicInstalledGame> installed;
        try
        {
            installed = (await GogJson.ReadAsync(fileSystem, file, GogJsonContext.Default.HeroicInstalledFile, cancellationToken)
                .ConfigureAwait(false))?.Installed ?? [];
        }
        // Usually caught while Heroic rewrites it; the games it listed are kept until it can be read again.
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new GogSourceScan([new LibraryLocation(file, LibrarySource, file, LibraryState.Unreadable)], [], watch);
        }

        return new GogSourceScan([new LibraryLocation(file, LibrarySource, file, LibraryState.Online)],
            [.. installed.Select(game => ToRecord(game, file)).OfType<GogInstallRecord>()], watch);
    }

    private static GogInstallRecord? ToRecord(HeroicInstalledGame game, string libraryId) =>
        GogJson.IsId(game.AppName) && GogJson.NullIfBlank(game.InstallPath) is { } path
            ? new GogInstallRecord(game.AppName, path, libraryId)
            {
                BuildId = GogJson.NullIfBlank(game.BuildId),
                IsDlc = game.IsDlc,
                Language = GogJson.NullIfBlank(game.Language),
                Platform = game.Platform?.ToUpperInvariant() switch
                {
                    "WINDOWS" => OperatingSystemKind.Windows,
                    "LINUX" => OperatingSystemKind.Linux,
                    "OSX" or "MAC" => OperatingSystemKind.MacOS,
                    _ => OperatingSystemKind.Unknown,
                },
            }
            : null;
}
