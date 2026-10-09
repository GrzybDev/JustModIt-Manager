using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>
/// Game folders the player added, each described by the <c>.mancpn</c> files that the Epic Games Launcher or Legendary
/// left in its <c>.egstore</c> folder; no launcher lists such a game, so the app cannot start it.
/// </summary>
/// <param name="fileSystem">Reads the folders.</param>
/// <param name="platform">The system the app runs on, taken as the system the games are built for.</param>
public sealed class EpicFolderSource(IFileSystem fileSystem, AppPlatform platform)
    : ManualFolderSource<EpicInstallRecord>(fileSystem, LibrarySource)
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of a folder the player added.</summary>
    public const string LibrarySource = "epic-folder";

    protected override async Task<IReadOnlyList<EpicInstallRecord>> FindRecordsAsync(string folder, CancellationToken cancellationToken)
    {
        // The game and each of its DLC have their own file, all in the game's namespace.
        var manifests = await EpicGameFolder.ReadManifestsAsync(FileSystem, folder, cancellationToken).ConfigureAwait(false);
        return manifests is not [var manifest, ..]
            ? []
            :
            [
                new EpicInstallRecord(manifest.CatalogNamespace!, folder, folder)
                {
                    AppName = manifest.AppName!,
                    CatalogItemId = LauncherJson.NullIfBlank(manifest.CatalogItemId),
                    Name = Path.GetFileName(folder),
                    Platform = platform.AsOperatingSystem(),
                },
            ];
    }
}
