using System.Globalization;
using System.Text;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

/// <summary>A Steam installation in memory: a root with its registry values, libraries, manifests and downloads.</summary>
internal sealed class SteamTree
{
    public const string SteamKey = @"Software\Valve\Steam";

    public static readonly DateTimeOffset WrittenAt = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly List<(string Path, bool? Mounted)> _listed = [];

    /// <summary>Creates a Steam root with its <c>steamapps</c> folder.</summary>
    /// <param name="root">The root folder.</param>
    /// <param name="registered">Whether the user's registry names the root, as Steam itself does.</param>
    public SteamTree(string root = @"C:\Steam", bool registered = true)
    {
        Root = root;
        Files.AddDirectory(Path.Join(root, "steamapps"));
        if (registered)
        {
            // Steam writes the path in lower case with forward slashes.
            Registry.SetText(RegistryHive.CurrentUser, RegistryView.Default, SteamKey, "SteamPath",
                root.Replace('\\', '/').ToLowerInvariant());
        }
    }

    public static LibraryEnvironment Windows { get; } = new()
    {
        Platform = AppPlatform.Windows,
        HomeDirectory = @"C:\Users\Player",
        ProgramFilesX86 = @"C:\Program Files (x86)",
    };

    public string Root { get; }

    public InMemoryFileSystem Files { get; } = new();

    public InMemoryRegistry Registry { get; } = new();

    /// <summary>The provider's clock, a day after the manifests were written.</summary>
    public FakeTimeProvider Time { get; } = new(WrittenAt.AddDays(1));

    /// <summary>Creates a Steam provider over this installation.</summary>
    /// <param name="environment">The system the provider runs on; <see cref="Windows"/> when null.</param>
    /// <param name="files">What the provider reads through, such as a wrapper around <see cref="Files"/>; <see cref="Files"/> when null.</param>
    /// <param name="time">The provider's clock; <see cref="Time"/> when null.</param>
    public SteamLibraryProvider CreateProvider(LibraryEnvironment? environment = null, IFileSystem? files = null, TimeProvider? time = null) =>
        new(files ?? Files, Registry, environment ?? Windows, time ?? Time, NullLogger<SteamLibraryProvider>.Instance);

    public Task<StoreScan> ScanAsync(params string[] manualFolders) =>
        CreateProvider().ScanAsync(new LibraryScanContext([.. manualFolders]), TestContext.Current.CancellationToken);

    /// <summary>Adds a library to the root's lists (both copies), creates its <c>steamapps</c> folder and returns its path.</summary>
    /// <param name="path">The library's folder as the lists name it.</param>
    /// <param name="mounted">The lists' <c>mounted</c> flag; left out when null.</param>
    /// <param name="create">Whether to create the <c>steamapps</c> folder.</param>
    public string AddLibrary(string path, bool? mounted = null, bool create = true)
    {
        if (create)
        {
            Files.AddDirectory(Path.Join(path, "steamapps"));
        }

        _listed.Add((path, mounted));
        WriteLibraryLists();
        return path;
    }

    /// <summary>Writes an app's manifest, creates its game folder and returns the manifest's path.</summary>
    /// <param name="appId">The Steam app id.</param>
    /// <param name="name">The app's name, also its folder unless <paramref name="installDir"/> is given.</param>
    /// <param name="stateFlags">The manifest's <c>StateFlags</c>; 4 is fully installed.</param>
    /// <param name="library">The library holding the app; the root when null.</param>
    /// <param name="installDir">The folder under <c>steamapps\common</c>; <paramref name="name"/> when null.</param>
    /// <param name="createFolder">Whether to create the game folder.</param>
    /// <param name="extra">More manifest lines, written as given.</param>
    /// <param name="writtenAt">When the manifest was written; <see cref="WrittenAt"/> when null.</param>
    /// <param name="sizeOnDisk">The manifest's <c>SizeOnDisk</c> in bytes.</param>
    /// <param name="buildId">The manifest's <c>buildid</c>.</param>
    public string AddApp(uint appId, string name, ulong stateFlags = 4, string? library = null, string? installDir = null,
        bool createFolder = true, string extra = "", DateTimeOffset? writtenAt = null, ulong sizeOnDisk = 1000, string buildId = "100")
    {
        library ??= Root;
        installDir ??= name;
        var id = appId.ToString(CultureInfo.InvariantCulture);
        var manifest = $$"""
            "AppState"
            {
            	"appid"		"{{id}}"
            	"Universe"		"1"
            	"name"		"{{name}}"
            	"StateFlags"		"{{stateFlags}}"
            	"installdir"		"{{installDir}}"
            	"SizeOnDisk"		"{{sizeOnDisk}}"
            	"buildid"		"{{buildId}}"
            	{{extra}}
            }
            """;
        var path = Files.AddFile(ManifestPath(appId, library), manifest, writtenAt ?? WrittenAt);
        if (createFolder)
        {
            Files.AddDirectory(Path.Join(library, "steamapps", "common", installDir));
        }

        return path;
    }

    public string ManifestPath(uint appId, string? library = null) =>
        Path.Join(library ?? Root, "steamapps", $"appmanifest_{appId.ToString(CultureInfo.InvariantCulture)}.acf");

    public string GameFolder(string installDir, string? library = null) => Path.Join(library ?? Root, "steamapps", "common", installDir);

    /// <summary>Creates the app's folder under <c>steamapps\downloading</c>, as Steam does while it downloads.</summary>
    /// <param name="appId">The app being downloaded.</param>
    /// <param name="changedAt">When Steam last changed the download; long before <see cref="Time"/> unless given.</param>
    public void AddDownload(uint appId, DateTimeOffset? changedAt = null) =>
        Files.AddDirectory(Path.Join(Root, "steamapps", "downloading", appId.ToString(CultureInfo.InvariantCulture)),
            lastWriteTimeUtc: changedAt ?? WrittenAt);

    public void SetUpdating(uint appId) =>
        Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Default, $@"{SteamKey}\Apps\{appId}", "Updating", RegistryValue.FromDWord(1));

    private void WriteLibraryLists()
    {
        var builder = new StringBuilder("\"libraryfolders\"\n{\n");
        builder.Append(CultureInfo.InvariantCulture, $"\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{Escape(Root)}\"\n\t}}\n");
        for (var i = 0; i < _listed.Count; i++)
        {
            var (path, mounted) = _listed[i];
            builder.Append(CultureInfo.InvariantCulture, $"\t\"{i + 1}\"\n\t{{\n\t\t\"path\"\t\t\"{Escape(path)}\"\n");
            if (mounted is { } isMounted)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\t\t\"mounted\"\t\t\"{(isMounted ? 1 : 0)}\"\n");
            }

            builder.Append("\t}\n");
        }

        var text = builder.Append("}\n").ToString();
        Files.AddFile(Path.Join(Root, "steamapps", "libraryfolders.vdf"), text);
        Files.AddFile(Path.Join(Root, "config", "libraryfolders.vdf"), text);
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);
}
