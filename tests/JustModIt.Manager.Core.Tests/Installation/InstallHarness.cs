using System.Security.Cryptography;
using System.Text;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Installation;

/// <summary>A game folder, the app's data folder, a folder the player picked for backups and an engine over them.</summary>
internal sealed class InstallHarness : IAsyncDisposable
{
    public const string HiddenFolderName = ".example-app";
    public const string GameVolume = "VOLUME-GAMES";
    public const string OtherVolume = "VOLUME-OTHER";

    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    public static readonly GameKey Key = new(StoreId.Steam, "1000");
    public static readonly byte[] OriginalPak = TestPatch.RandomBytes(256 << 10, seed: 1);
    public static readonly byte[] ChangedPak = Change(OriginalPak);

    public static readonly InstallSource Source = new()
    {
        ProjectId = "project-1",
        ProjectSlug = "example-project",
        ProjectName = "Example Project",
        GameSlug = "example-game",
        GameName = "Example Game",
        TeamSlug = "example-team",
        TeamName = "Example Team",
        ReleaseId = "release-1",
        ReleaseLabel = "v1.2",
        ReleaseVersion = 3,
        ReleaseFileId = "file-1",
        VariantName = "Text only",
        CoverUrl = new Uri("https://images.example.com/covers/example-game.jpg"),
        CoverPath = "cache/images/example-game.jpg",
    };

    private readonly TempDirectory _temp = new();
    private readonly List<InstallEngine> _engines = [];

    public InstallHarness(BackupChoice? backup = null)
    {
        var root = PathCanonicalizer.Canonicalize(_temp.Path);
        GameRoot = Directory.CreateDirectory(Path.Join(root, "Game")).FullName;
        AppData = Path.Join(root, "AppData");
        CustomFolder = Directory.CreateDirectory(Path.Join(root, "Backups")).FullName;
        Packages = Path.Join(root, "Packages");
        Crashes = Path.Join(root, "Crashes");
        Backup = backup ?? BackupChoice.GameFolder;
        Volumes.Assign(root, GameVolume);
        Engine = CreateEngine();

        WriteGameFile("bin/game.exe", TestPatch.RandomBytes(64 << 10, seed: 2));
        WriteGameFile("bin/library.dll", "original library");
        WriteGameFile("data/strings.pak", OriginalPak);
        WriteGameFile("movies/intro.bik", "video");
        WriteGameFile("readme.txt", "Read me");
    }

    public string GameRoot { get; }

    public string AppData { get; }

    public string CustomFolder { get; }

    public string Packages { get; }

    public string Crashes { get; }

    public BackupChoice Backup { get; set; }

    public FakeVolumes Volumes { get; } = new();

    public FakeGameProbe Probe { get; } = new();

    public FakeLockHolders LockHolders { get; } = new();

    public InstallEngine Engine { get; }

    public Action<ChangeKind, string?>? BeforeChange { get; set; }

    public long SlowBackupCopyBytes { get; set; } = 1L << 30;

    public InstalledGame Game => new()
    {
        Key = Key,
        Name = "Example Game",
        InstallDirectory = GameRoot,
        LibraryId = "library-1",
        BuildId = "42",
        State = GameInstallState.Installed,
    };

    public string InstallsDirectory => Path.Join(AppData, "installs");

    public string HiddenDirectory => Path.Join(GameRoot, HiddenFolderName);

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static BackupChoice InAppData => new(BackupLocation.AppData);

    public BackupChoice InCustomFolder => new(BackupLocation.CustomFolder, CustomFolder);

    /// <summary>A fresh engine over the same folders, as after a restart of the app.</summary>
    /// <param name="decoder">The patch decoder; null for the real xdelta3 one.</param>
    public InstallEngine CreateEngine(IPatchDecoder? decoder = null)
    {
        var engine = new InstallEngine(
            new InstallEngineOptions
            {
                AppId = "ExampleApp",
                HiddenFolderName = HiddenFolderName,
                InstallsDirectory = InstallsDirectory,
                BackupsDirectory = Path.Join(AppData, "backups"),
                SlowBackupCopyBytes = SlowBackupCopyBytes,
                BeforeChange = (kind, path) => BeforeChange?.Invoke(kind, path),
            },
            new PackageReader(), decoder ?? new XDelta3Patcher(memoryBudget: 64 << 20), new FileLockProbe(LockHolders), Probe,
            Volumes, new FakeTimeProvider(Now), NullLogger<InstallEngine>.Instance);
        _engines.Add(engine);
        return engine;
    }

    public string GameFile(string relativePath) => PackagePath.ToFullPath(GameRoot, relativePath);

    public string WriteGameFile(string relativePath, string content) => WriteGameFile(relativePath, Encoding.UTF8.GetBytes(content));

    public string WriteGameFile(string relativePath, byte[] content)
    {
        var path = GameFile(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        File.WriteAllBytes(path, content);
        return path;
    }

    public string ReadGameFile(string relativePath) => File.ReadAllText(GameFile(relativePath));

    public string ManifestPackage(TestPackage package) =>
        package.WriteManifestPackage(Path.Join(Packages, Guid.NewGuid().ToString("N") + ".examplemod"));

    public string Zip(TestPackage package) => package.WriteZip(Path.Join(Packages, Guid.NewGuid().ToString("N") + ".zip"));

    public InstallRequest Request(string package, BackupChoice? backup = null) => new(Game, package, backup ?? Backup, Source);

    public Task<InstallResult> InstallAsync(string package, IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Engine.InstallAsync(Request(package), progress, cancellationToken == default ? Token : cancellationToken);

    /// <summary>Installs a package that must be refused, and checks that the refusal changed nothing.</summary>
    /// <param name="package">The package file's path.</param>
    public async Task<InstallBlockedException> InstallBlockedAsync(string package)
    {
        var game = Snapshot();
        var app = AppFiles();
        var install = () => InstallAsync(package);
        var blocked = (await install.Should().ThrowAsync<InstallBlockedException>()).Which;
        Snapshot().Should().BeEquivalentTo(game, "a refused install changes nothing");
        AppFiles().Should().BeEquivalentTo(app);
        return blocked;
    }

    /// <summary>Every file (hash and attributes) and folder of the game, to prove an operation left no trace.</summary>
    public Dictionary<string, string> Snapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(GameRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(GameRoot, entry).Replace('\\', '/');
            snapshot[relative] = Directory.Exists(entry) ? "<dir>" : Describe(entry);
        }

        return snapshot;
    }

    /// <summary>The game without the app's own folder, whose backups are named after each install.</summary>
    public Dictionary<string, string> SnapshotOfGameFiles() =>
        Snapshot().Where(entry => !entry.Key.StartsWith(HiddenFolderName, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static string Describe(string file)
    {
        var attributes = File.GetAttributes(file) & (FileAttributes.ReadOnly | FileAttributes.Hidden);
        try
        {
            return $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))} {attributes}";
        }
        catch (IOException)
        {
            // A file a test holds open to lock it.
            return $"<in use> {attributes}";
        }
    }

    /// <summary>What the app keeps apart from the game: install folders and backups in its data and the player's folder.</summary>
    public IReadOnlyList<string> AppFiles() =>
        [.. new[] { AppData, CustomFolder }.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            .Where(entry => !PathComparison.AreSame(entry, InstallsDirectory))];

    /// <summary>Makes the game's volume and the app's other folders look like different drives.</summary>
    public void PutBackupsOnAnotherVolume()
    {
        Volumes.Assign(AppData, OtherVolume);
        Volumes.Assign(CustomFolder, OtherVolume);
    }

    /// <summary>Copies the game and the app's folders aside at the given change, as a crash would leave them.</summary>
    /// <param name="change">The zero-based number of the change the crash comes right before.</param>
    public void CrashAt(int change)
    {
        var count = 0;
        CrashWhen((_, _) => count++ == change);
    }

    /// <summary>Crashes at the first change of a kind, e.g. right before the record that commits an install.</summary>
    /// <param name="kind">The kind of change the crash comes right before.</param>
    public void CrashBefore(ChangeKind kind) => CrashWhen((changing, _) => changing == kind);

    private void CrashWhen(Func<ChangeKind, string?, bool> isCrash)
    {
        var crashed = false;
        BeforeChange = (kind, path) =>
        {
            if (!crashed && isCrash(kind, path))
            {
                crashed = true;
                foreach (var (folder, name) in Folders())
                {
                    TreeCopy.Copy(folder, Path.Join(Crashes, name));
                }
            }
        };
    }

    /// <summary>Counts the changes an operation makes, to crash at each of them.</summary>
    /// <returns>A function that reads the count so far.</returns>
    public Func<int> CountChanges()
    {
        var count = 0;
        BeforeChange = (_, _) => count++;
        return () => count;
    }

    /// <summary>Puts back what <see cref="CrashAt"/> or <see cref="CrashBefore"/> captured, as if the process had died there.</summary>
    /// <returns>False when no crash was captured.</returns>
    public bool RestoreCrash()
    {
        BeforeChange = null;
        if (!Directory.Exists(Crashes))
        {
            return false;
        }

        foreach (var (folder, name) in Folders())
        {
            TreeCopy.Delete(folder);
            if (Directory.Exists(Path.Join(Crashes, name)))
            {
                TreeCopy.Copy(Path.Join(Crashes, name), folder);
            }
        }

        TreeCopy.Delete(Crashes);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var engine in _engines)
        {
            await engine.DisposeAsync();
        }

        TreeCopy.Delete(_temp.Path);
        _temp.Dispose();
    }

    private (string Folder, string Name)[] Folders() => [(GameRoot, "Game"), (AppData, "AppData"), (CustomFolder, "Backups")];

    private static byte[] Change(byte[] original)
    {
        var changed = original.ToArray();
        "Save the game"u8.CopyTo(changed.AsSpan(4096));
        "Load the game"u8.CopyTo(changed.AsSpan(100_000));
        return changed;
    }
}

/// <summary>Copies and removes folder trees with their attributes and times, including files others hold open for writing.</summary>
internal static class TreeCopy
{
    public static void Copy(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Prepend(source))
        {
            var target = Directory.CreateDirectory(Path.Join(destination, Path.GetRelativePath(source, directory)));
            target.Attributes = File.GetAttributes(directory);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Join(destination, Path.GetRelativePath(source, file));
            try
            {
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var output = File.Create(target))
                {
                    input.CopyTo(output);
                }

                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
                File.SetCreationTimeUtc(target, File.GetCreationTimeUtc(file));
                File.SetAttributes(target, File.GetAttributes(file));
            }
            catch (IOException)
            {
                // The game lock is held exclusively and would not survive a crash anyway.
            }
        }
    }

    public static void Delete(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(entry, File.GetAttributes(entry) & ~(FileAttributes.ReadOnly | FileAttributes.Hidden));
        }

        Directory.Delete(folder, recursive: true);
    }
}

/// <summary>Volumes by folder, so tests can put backups on "another drive" within one temporary folder.</summary>
internal sealed class FakeVolumes : IVolumes
{
    private readonly List<(string Folder, string Volume)> _volumes = [];

    public Dictionary<string, long> Available { get; } = [];

    public HashSet<string> Detached { get; } = [];

    public HashSet<string> NetworkFolders { get; } = new(PathComparison.Comparer);

    public void Assign(string folder, string volume) => _volumes.Add((PathComparison.Normalize(folder), volume));

    public string? GetVolumeId(string path) =>
        _volumes.Where(volume => PathComparison.IsWithin(volume.Folder, path)).MaxBy(volume => volume.Folder.Length).Volume
        ?? "VOLUME-SYSTEM";

    public bool IsAttached(string volumeId) => !Detached.Contains(volumeId);

    public long? GetAvailableBytes(string path) =>
        GetVolumeId(path) is { } volume && Available.TryGetValue(volume, out var available) ? available : null;

    public bool IsNetworkPath(string path) => NetworkFolders.Any(folder => PathComparison.IsWithin(folder, path));
}

/// <summary>What the store and the running processes say about the game when an operation asks.</summary>
internal sealed class FakeGameProbe : IGameProbe
{
    public GameInstallState State { get; set; } = GameInstallState.Installed;

    public GameStateReason Reason { get; set; }

    public bool IsStale { get; set; }

    /// <summary>The store no longer lists the game.</summary>
    public bool IsGone { get; set; }

    public ValueList<RunningProcess> Running { get; set; } = [];

    public void Runs(params string[] names) =>
        Running = [.. names.Select((name, index) => new RunningProcess(1000 + index, name, ExecutablePath: null))];

    public Task<GameProbe> ProbeAsync(GameKey key, CancellationToken cancellationToken) =>
        Task.FromResult(new GameProbe(key, IsGone ? null : new InstalledGame
        {
            Key = key,
            Name = "Example Game",
            InstallDirectory = "unused",
            LibraryId = "library-1",
            State = State,
            Reason = Reason,
            IsStale = IsStale,
        }, Running));
}

internal sealed class FakeLockHolders : ILockHolderSource
{
    public IReadOnlyList<string> FindHolders(IReadOnlyList<string> paths) => ["Example.exe"];
}
