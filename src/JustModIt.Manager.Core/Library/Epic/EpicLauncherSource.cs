using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>
/// Reads the Epic Games Launcher's <c>.item</c> manifests of games, DLC, engines and plugins, where an update in progress
/// shows in a pending manifest, the item's flags or <c>LauncherInstalled.dat</c>, which keeps the old version until then.
/// </summary>
/// <param name="fileSystem">Reads the launcher's files.</param>
/// <param name="registry">Tells where the launcher keeps its data when it was moved.</param>
/// <param name="programData">The system's shared application data folder, where the launcher keeps its data.</param>
internal sealed class EpicLauncherSource(IFileSystem fileSystem, IRegistry registry, string programData) : IInstallSource<EpicInstallRecord>
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of the launcher's manifests.</summary>
    public const string LibrarySource = "epic-launcher";

    internal const string LauncherKey = @"SOFTWARE\Epic Games\EpicGamesLauncher";

    private string InstalledFile => Path.Join(programData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");

    public Task<SourceScan<EpicInstallRecord>> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var manifests = Path.Join(DataDirectory(), "Manifests");
        return context.Access.ReadAsync(manifests, token => ReadAsync(manifests, token), cancellationToken);
    }

    private string DataDirectory() =>
        new[] { RegistryView.Registry64, RegistryView.Registry32 }
            .Select(view => registry.ReadKey(RegistryHive.LocalMachine, view, LauncherKey)?.GetText("AppDataPath"))
            .FirstOrDefault(path => path is not null && Path.IsPathFullyQualified(path))
        ?? Path.Join(programData, "Epic", "EpicGamesLauncher", "Data");

    private async Task<SourceScan<EpicInstallRecord>> ReadAsync(string manifests, CancellationToken cancellationToken)
    {
        if (!fileSystem.DirectoryExists(manifests))
        {
            return new([], [], WatchPlan.ForFolders(WatchedFolder.Toward(fileSystem, manifests)));
        }

        var pending = Path.Join(manifests, "Pending");
        var watch = WatchPlan.ForFolders(
            new WatchedFolder(manifests, ["*.item", "Pending"]),
            fileSystem.DirectoryExists(pending) ? new WatchedFolder(pending, ["*.item"]) : null,
            WatchedFolder.Toward(fileSystem, InstalledFile));
        var library = fileSystem.GetCanonicalPath(manifests);
        var records = new List<EpicInstallRecord>();
        try
        {
            var pendingItems = ListItems(pending).ToDictionary(item => Path.GetFileNameWithoutExtension(item.Name),
                item => item.LastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
            var versions = await ReadVersionsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var file in ListItems(manifests))
            {
                var item = await LauncherJson.ReadAsync(fileSystem, file.FullPath, LauncherJsonContext.Default.EpicItem, cancellationToken)
                    .ConfigureAwait(false);
                if (item is not null && ToRecord(item, file, library, pendingItems, versions) is { } record)
                {
                    records.Add(record);
                }
            }
        }
        // Usually caught while the launcher rewrites a manifest; the games it listed are kept until it can be read again.
        catch (Exception exception) when (LauncherJson.IsUnreadable(exception))
        {
            return new([new LibraryLocation(library, LibrarySource, library, LibraryState.Unreadable)], [], watch);
        }

        return new([new LibraryLocation(library, LibrarySource, library, LibraryState.Online)], EpicInstallRecord.FoldDlc(records), watch);
    }

    // The version of each app that the last complete install or update left.
    private async Task<Dictionary<string, string>> ReadVersionsAsync(CancellationToken cancellationToken)
    {
        var installed = await LauncherJson.ReadIfExistsAsync(fileSystem, InstalledFile, LauncherJsonContext.Default.LauncherInstalledFile,
            cancellationToken).ConfigureAwait(false);
        return (installed?.InstallationList ?? [])
            .Where(installation => installation.AppName is not null && installation.AppVersion is not null)
            .DistinctBy(installation => installation.AppName)
            .ToDictionary(installation => installation.AppName!, installation => installation.AppVersion!, StringComparer.Ordinal);
    }

    private List<FileEntry> ListItems(string folder) =>
        fileSystem.DirectoryExists(folder)
            ? [.. fileSystem.Enumerate(folder, "*.item", FileListing.AnyCase).Where(entry => !entry.IsDirectory)]
            : [];

    private static EpicInstallRecord? ToRecord(EpicItem item, FileEntry file, string libraryId,
        Dictionary<string, DateTimeOffset> pendingItems, Dictionary<string, string> versions)
    {
        var isDlc = Has(item.AppCategories, "addons") && !Has(item.AppCategories, "addons/launchable");
        var isGame = Has(item.AppCategories, "games") && LauncherJson.NullIfBlank(item.HostInstallationGuid) is null;
        if (IsEngineContent(item) || !(isDlc || isGame)
            || LauncherJson.NullIfBlank(item.CatalogNamespace) is not { } catalogNamespace
            || LauncherJson.NullIfBlank(item.AppName) is not { } appName
            || LauncherJson.NullIfBlank(item.InstallLocation) is not { } installLocation)
        {
            return null;
        }

        DateTimeOffset? pendingAt = item.InstallationGuid is { } guid && pendingItems.TryGetValue(guid, out var at) ? at : null;
        var catalogItemId = LauncherJson.NullIfBlank(item.CatalogItemId);
        return new EpicInstallRecord(catalogNamespace, installLocation, libraryId)
        {
            AppName = appName,
            CatalogItemId = catalogItemId,
            Name = LauncherJson.NullIfBlank(item.DisplayName),
            BuildId = LauncherJson.NullIfBlank(item.AppVersionString),
            IsDlc = isDlc,
            Platform = OperatingSystemKind.Windows,
            Launch = LauncherLink(catalogNamespace, catalogItemId, appName),
            Activity = item.IsIncompleteInstall ? GameInstallState.Installing
                : pendingAt is not null ? GameInstallState.Updating
                : item.NeedsValidation ? GameInstallState.Validating
                : versions.TryGetValue(appName, out var complete) && complete != item.AppVersionString ? GameInstallState.Updating
                : null,
            ActivityAt = pendingAt > file.LastWriteTimeUtc ? pendingAt : file.LastWriteTimeUtc,
        };
    }

    private static LaunchLink? LauncherLink(string catalogNamespace, string? catalogItemId, string appName) =>
        catalogItemId is null
            ? null
            : LaunchLink.TryCreate($"com.epicgames.launcher://apps/{Uri.EscapeDataString(catalogNamespace)}%3A"
                + $"{Uri.EscapeDataString(catalogItemId)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true");

    // The launcher installs Unreal Engine and its plugins too; none of them is a game.
    private static bool IsEngineContent(EpicItem item) =>
        string.Equals(item.CatalogNamespace, "ue", StringComparison.OrdinalIgnoreCase)
        || (item.CompatibleApps ?? []).Any(app => app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase))
        || (item.AppCategories ?? []).Concat((item.TechnicalType ?? "").Split(',', StringSplitOptions.TrimEntries))
            .Select(category => category.Split('/')[0])
            .Any(kind => kind.Equals("plugins", StringComparison.OrdinalIgnoreCase)
                || kind.Equals("engines", StringComparison.OrdinalIgnoreCase));

    private static bool Has(IReadOnlyList<string>? categories, string category) =>
        categories?.Contains(category, StringComparer.OrdinalIgnoreCase) == true;
}
