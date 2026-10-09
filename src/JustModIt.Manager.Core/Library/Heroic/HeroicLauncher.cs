using System.Text.Json;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Heroic;

/// <summary>Heroic Games Launcher, which installs games of several stores through a runner per store.</summary>
public static class HeroicLauncher
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of Heroic's list of a store's installs.</summary>
    public const string LibrarySource = "heroic";

    /// <summary>Gives Heroic's configuration folders on this platform, one per way Heroic can be installed.</summary>
    /// <param name="environment">The user's folders and environment variables.</param>
    public static IReadOnlyList<string> ConfigDirectories(LibraryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var home = environment.HomeDirectory;
        return environment.Platform switch
        {
            AppPlatform.Windows => environment.ApplicationData is { } appData ? [Path.Join(appData, "heroic")] : [],
            AppPlatform.MacOS => [Path.Join(home, "Library", "Application Support", "heroic")],
            _ =>
            [
                Path.Join(environment.XdgConfigHome ?? Path.Join(home, ".config"), "heroic"),
                Path.Join(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic"),
            ],
        };
    }

    /// <summary>Gives the file of Heroic's download queue.</summary>
    /// <param name="configDirectory">Heroic's configuration folder.</param>
    internal static string QueueFile(string configDirectory) => Path.Join(configDirectory, "store", "download-manager.json");

    /// <summary>
    /// Reads what Heroic's download manager has queued for the games of one runner: it installs or updates each of them
    /// once the downloads before it finish.
    /// </summary>
    /// <param name="fileSystem">Reads the queue.</param>
    /// <param name="configDirectory">Heroic's configuration folder.</param>
    /// <param name="runner">The store's runner, such as <c>gog</c> or <c>legendary</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="JsonException">The queue is caught while Heroic rewrites it.</exception>
    internal static async Task<IReadOnlyDictionary<string, GameInstallState>> ReadQueueAsync(IFileSystem fileSystem, string configDirectory,
        string runner, CancellationToken cancellationToken)
    {
        var file = QueueFile(configDirectory);
        var queue = fileSystem.FileExists(file)
            ? (await LauncherJson.ReadAsync(fileSystem, file, HeroicJsonContext.Default.HeroicDownloadManager, cancellationToken)
                .ConfigureAwait(false))?.Queue ?? []
            : [];
        return queue
            .Where(element => element.Params?.Runner == runner && LauncherJson.NullIfBlank(element.Params.AppName) is not null)
            .DistinctBy(element => element.Params!.AppName)
            .ToDictionary(element => element.Params!.AppName!,
                element => element.Type == "update" ? GameInstallState.Updating : GameInstallState.Installing, StringComparer.Ordinal);
    }

    /// <summary>Creates a link Heroic opens by starting the game through its runner.</summary>
    /// <param name="appName">The runner's id of the game.</param>
    /// <param name="runner">The store's runner, such as <c>gog</c> or <c>legendary</c>.</param>
    internal static LaunchLink? Launch(string appName, string runner) =>
        LaunchLink.TryCreate($"heroic://launch?appName={Uri.EscapeDataString(appName)}&runner={runner}");
}

/// <summary>Heroic's <c>store\download-manager.json</c>.</summary>
internal sealed class HeroicDownloadManager
{
    public IReadOnlyList<HeroicQueueElement>? Queue { get; init; }
}

/// <summary>A download waiting in Heroic's queue.</summary>
internal sealed class HeroicQueueElement
{
    public HeroicQueueParams? Params { get; init; }

    /// <summary><c>install</c> or <c>update</c>.</summary>
    public string? Type { get; init; }
}

/// <summary>The game a queued download is for.</summary>
internal sealed class HeroicQueueParams
{
    [JsonConverter(typeof(IdStringConverter))]
    public string? AppName { get; init; }

    /// <summary>The store's runner, such as <c>gog</c> or <c>legendary</c> for the Epic Games Store.</summary>
    public string? Runner { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(HeroicDownloadManager))]
internal sealed partial class HeroicJsonContext : JsonSerializerContext;
