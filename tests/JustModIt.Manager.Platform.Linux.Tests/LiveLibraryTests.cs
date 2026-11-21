using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Epic;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class LiveLibraryTests
{
    public LiveLibraryTests() =>
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("MANAGER_LIVE_TESTS") == "1",
            "Live tests run only with MANAGER_LIVE_TESTS=1 on Linux.");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LibraryEnvironment User { get; } = LibraryEnvironment.ForCurrentUser();

    [Fact]
    public async Task The_real_steam_library_scans_to_sane_results_with_the_system_each_game_is_built_for()
    {
        var provider = Steam();

        var scan = await provider.ScanAsync(LibraryScanContext.Empty, Token);

        scan.IsPresent.Should().BeTrue();
        scan.IsComplete.Should().BeTrue();
        scan.Libraries.Should().NotBeEmpty().And.OnlyContain(library => Path.IsPathFullyQualified(library.Id));
        scan.Games.Select(game => game.Key).Should().OnlyHaveUniqueItems();
        scan.Games.Select(game => game.InstallDirectory).Should().OnlyHaveUniqueItems();
        scan.Games.Should().OnlyContain(game => scan.Libraries.Any(library => library.Id == game.LibraryId) && !string.IsNullOrWhiteSpace(game.Name));
        scan.Games.Should().NotContain(game => game.Name.StartsWith("Proton", StringComparison.Ordinal)
            || game.Name.StartsWith("Steam Linux Runtime", StringComparison.Ordinal)
            || game.Name.StartsWith("Steamworks", StringComparison.Ordinal), "tools are not games");

        var linux = LiveGames.Load().Linux;
        Assert.SkipWhen(linux is null, $"{LiveGames.FileName} names no Linux games.");
        var proton = scan.Games.Should().ContainSingle(game => game.Key == linux.Proton).Subject;
        var native = scan.Games.Should().ContainSingle(game => game.Key == linux.Native).Subject;
        proton.Platform.Should().Be(OperatingSystemKind.Windows, "Steam installed the Windows build to run it through Proton");
        native.Platform.Should().Be(OperatingSystemKind.Linux);
        foreach (var game in new[] { proton, native }.Where(game => game.IsReady))
        {
            var probed = await provider.ProbeAsync(game, LibraryScanContext.Empty, Token);
            (probed!.Key, probed.InstallDirectory, probed.Platform).Should().Be((game.Key, game.InstallDirectory, game.Platform),
                "a probe agrees with the scan");
        }
    }

    [Fact]
    public async Task The_library_service_watches_each_folder_through_one_system_watcher()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
        var time = TimeProvider.System;
        ILibraryProvider[] providers =
        [
            Steam(),
            GogLibraryProvider.Create(PhysicalFileSystem.Instance, NoWindowsRecords.Instance, NoWindowsRecords.Instance, User,
                NullLogger<GogLibraryProvider>.Instance),
            EpicLibraryProvider.Create(PhysicalFileSystem.Instance, NoWindowsRecords.Instance, User, NullLogger<EpicLibraryProvider>.Instance),
        ];
        await using var settings = await SettingsService.LoadAsync(
            new JsonDocumentStore<AppSettings>(Path.Combine(directory, "settings.json"), SettingsFormat.Instance, time,
                NullLogger<JsonDocumentStore<AppSettings>>.Instance),
            time, NullLogger<SettingsService>.Instance, Token);
        var before = SystemWatchers();
        using var events = new LinuxSystemEvents(DBusConnection.System, NullLogger<LinuxSystemEvents>.Instance);
        await using var service = new LibraryService(providers, settings,
            new RunningGameDetector(LinuxProcessSource.Instance, PhysicalFileSystem.Instance, time), PhysicalFileSystem.Instance,
            FileSystemFolderWatcher.Instance, NoWindowsRecords.Instance, events,
            new JsonDocumentStore<LibrarySnapshot>(Path.Combine(directory, "library.json"), LibrarySnapshotFormat.Instance, time,
                NullLogger<JsonDocumentStore<LibrarySnapshot>>.Instance),
            time, NullLogger<LibraryService>.Instance);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = service.RunAsync(stop.Token);
        try
        {
            await service.CheckNowAsync(Token).WaitAsync(TimeSpan.FromMinutes(1), Token);

            service.Snapshot.FindStore(StoreId.Steam)!.IsPresent.Should().BeTrue();
            List<string> folders = [];
            foreach (var provider in providers)
            {
                folders.AddRange((await provider.ScanAsync(LibraryScanContext.Empty, Token)).Watch.Folders.Select(folder => folder.Directory));
            }

            var watched = folders.Distinct(PathComparison.Comparer).Count();
            service.ActiveWatchCount.Should().BeGreaterThanOrEqualTo(watched);
            (SystemWatchers() - before).Should().BeInRange(1, watched, "watches of one folder share its system watcher");
        }
        finally
        {
            await stop.CancelAsync();
            await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_steam_clients_files_name_its_running_process_and_its_language()
    {
        var files = SteamInstallation.ReadClientFiles(PhysicalFileSystem.Instance, User);

        var steam = LinuxProcessSource.Instance.Snapshot().FirstOrDefault(process => process.Id == files.ProcessId);
        Assert.SkipWhen(steam is null, "Start Steam first.");
        steam.Name.Should().Be("steam");
        files.Language.Should().NotBeNullOrEmpty();
    }

    private static SteamLibraryProvider Steam() =>
        new(PhysicalFileSystem.Instance, NoWindowsRecords.Instance, User, TimeProvider.System, NullLogger<SteamLibraryProvider>.Instance);

    // Each system watcher of .NET is an inotify instance of its own, which Linux counts against a limit per user.
    private static int SystemWatchers() => new DirectoryInfo("/proc/self/fd").EnumerateFileSystemInfos().Count(fd =>
    {
        try
        {
            return fd.LinkTarget == "anon_inode:inotify";
        }
        // A descriptor closed while the folder is listed has no link left to read.
        catch (IOException)
        {
            return false;
        }
    });
}
