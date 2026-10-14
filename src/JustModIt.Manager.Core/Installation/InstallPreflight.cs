using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Core.Installation;

/// <summary>What the check before an install found out, for the install to reuse.</summary>
/// <param name="Paths">The full path of every game-relative path the steps and declarations use.</param>
/// <param name="Hashes">Hashes the check took, which the install reuses for files that did not change since.</param>
/// <param name="MovesOriginals">The backup folder is on the game's volume, so originals are moved there, not copied.</param>
/// <param name="BytesTotal">Bytes the install reads and writes, for progress.</param>
/// <param name="Runtime">The files the package's software changes while the game runs.</param>
internal sealed record InstallPlan(
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyDictionary<string, DiskHash> Hashes,
    bool MovesOriginals,
    long BytesTotal,
    RuntimePlan Runtime);

/// <summary>The files the package's software changes while the game runs, as the check placed them.</summary>
/// <param name="Files">The declared files, one per file.</param>
/// <param name="IsTargetBuild">
/// The game is the very build a manifest target names, so its store's files are the originals the package names.
/// </param>
/// <param name="Inherited">Entries of kept-files records the install takes over, by full path.</param>
internal sealed record RuntimePlan(IReadOnlyList<DeclaredFile> Files, bool IsTargetBuild,
    IReadOnlyDictionary<string, InheritedFile> Inherited);

/// <summary>Everything an install needs to know before its first change, gathered so every problem is reported at once.</summary>
/// <param name="locks">Tells which files are in use, and by whom.</param>
/// <param name="volumes">Tells whether originals move or are copied, and how much space is free.</param>
/// <param name="slowCopyBytes">Copying this much to another drive takes long enough to tell the player why.</param>
internal sealed class InstallPreflight(FileLockProbe locks, IVolumes volumes, long slowCopyBytes)
{
    /// <summary>Room for the journal, the record and file system overhead on top of the files themselves.</summary>
    internal const long SpaceMargin = 64L << 20;

    /// <summary>Checks a package against the game without changing anything.</summary>
    /// <param name="package">The opened package.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="installation">The game as its store reports it.</param>
    /// <param name="backup">Where the install would keep the originals.</param>
    /// <param name="owners">Records of other installs on this game, by the full path of each file they own.</param>
    /// <param name="keptRecords">The game's kept-files records, which the install takes over, is refused by or drops.</param>
    /// <param name="knownIssues">Problems the caller found already.</param>
    /// <param name="baseline">What a path holds before the install, when that is not the file on disk; null for the disk.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>Every problem and warning, and the plan the install follows when it can go ahead.</returns>
    public async Task<(PreflightReport Report, InstallPlan Plan)> CheckAsync(IPackage package, GameFolder game,
        InstalledGame installation, BackupFolder backup, IReadOnlyDictionary<string, Guid> owners,
        IReadOnlyList<KeptFilesRecord> keptRecords, IEnumerable<InstallIssue> knownIssues, Func<string, SimulatedFile?>? baseline,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var tracker = new ProgressTracker(progress, InstallStage.Checking, package.Steps.Count,
            InstallSimulation.BytesToHash(package.Steps));
        var simulation = new InstallSimulation(game, tracker, baseline);
        await simulation.RunAsync(package.Steps, cancellationToken).ConfigureAwait(false);
        simulation.Declare(package.RuntimeFiles);
        var kept = await KeptFiles.CheckAsync(keptRecords, game, simulation, cancellationToken).ConfigureAwait(false);

        var issues = knownIssues.ToList();
        issues.AddRange(simulation.Issues);
        issues.AddRange(simulation.Touched
            .Where(touched => owners.ContainsKey(touched.Value))
            .Select(touched => new FileOwnedByOtherInstallIssue(touched.Key, owners[touched.Value])));
        issues.AddRange(kept.Issues);
        issues.AddRange(AccessIssues(locks.Probe([.. simulation.Originals.Keys, .. simulation.RuntimeOriginals.Keys], cancellationToken),
            [game.Root, .. simulation.TargetDirectories], game));

        var movesOriginals = volumes.AreOnSameVolume(game.Root, backup.Directory);
        var newBytes = simulation.CopiedBytes + simulation.PatchedBytes;
        var originalBytes = simulation.Originals.Values.Sum();
        // Originals of declared files are copied even where originals move, since the software goes on using them.
        var runtimeBytes = simulation.RuntimeOriginals.Values.Sum() + kept.CopiedBytes;
        var warnings = new List<InstallWarning>();
        // A renamed original reaches its new name as a copy, since the original itself moves to the backup folder.
        CheckSpace(game.Root, newBytes + (movesOriginals ? simulation.RenamedOriginalBytes + runtimeBytes : 0), issues);
        if (!movesOriginals && originalBytes + runtimeBytes > 0)
        {
            CheckSpace(backup.Directory, originalBytes + runtimeBytes, issues);
            if (originalBytes + runtimeBytes >= slowCopyBytes)
            {
                warnings.Add(new SlowBackupCopyWarning(originalBytes + runtimeBytes));
            }
        }

        CheckTargets(package.Manifest, installation, issues, warnings);
        // Moved originals are hashed unless the check already did, and renamed ones are copied to their new name;
        // copied originals are written and read back. New content is written once.
        var hashed = simulation.Originals.Where(original => simulation.Hashes.ContainsKey(original.Key))
            .Sum(original => original.Value);
        var bytesTotal = newBytes + 2 * runtimeBytes + (movesOriginals
            ? originalBytes - hashed + simulation.RenamedOriginalBytes
            : 2 * originalBytes);
        var runtime = new RuntimePlan(simulation.Declared, IsTargetBuild(package.Manifest, installation), kept.Inherited);
        return (new PreflightReport([.. issues], [.. warnings]),
            new InstallPlan(simulation.Paths, simulation.Hashes, movesOriginals, bytesTotal, runtime));
    }

    // On the very build the package was made on, the store's files are known, so a declared file unlike them was changed.
    private static bool IsTargetBuild(PackageManifest? manifest, InstalledGame game) =>
        manifest?.Targets.Any(target => target.Store == game.Key.Store && target.Id == game.Key.StoreGameId
            && target.BuildId is { } build && build == game.BuildId) == true;

    // Only another id in the game's own store is certainly another game; another store's version or another build only
    // warns, since patches check every file they change anyway.
    private static void CheckTargets(PackageManifest? manifest, InstalledGame game, List<InstallIssue> issues,
        List<InstallWarning> warnings)
    {
        if (manifest is null || manifest.Targets.Count == 0)
        {
            return;
        }

        var sameStore = manifest.Targets.Where(target => target.Store == game.Key.Store).ToList();
        if (sameStore.Count == 0)
        {
            warnings.Add(new OtherStoreWarning([.. manifest.Targets]));
        }
        else if (sameStore.FirstOrDefault(target => target.Id == game.Key.StoreGameId) is not { } target)
        {
            issues.Add(new OtherGameIssue([.. manifest.Targets]));
        }
        else if (target.BuildId is { } build && build != game.BuildId)
        {
            warnings.Add(new OtherGameBuildWarning(build, game.BuildId));
        }
    }

    /// <summary>
    /// Files other programs hold or the user may not write, and existing folders that will receive new files but take
    /// none; permissions are never reported as a lock, since closing programs does not help.
    /// </summary>
    /// <param name="files">What the lock probe found for the files to change.</param>
    /// <param name="directories">Existing folders that will receive new files.</param>
    /// <param name="game">The game folder, which the issues' paths are relative to.</param>
    public static IEnumerable<InstallIssue> AccessIssues(FileLockReport files, IEnumerable<string> directories, GameFolder game)
    {
        foreach (var path in files.InUse)
        {
            yield return new FileLockedIssue(game.RelativePathOf(path), files.Holders);
        }

        foreach (var path in files.AccessDenied)
        {
            yield return new PermissionRequiredIssue(game.RelativePathOf(path));
        }

        foreach (var directory in directories.Distinct(PathComparison.Comparer).Where(directory => !CanCreateFilesIn(directory)))
        {
            yield return new PermissionRequiredIssue(PathComparison.AreSame(directory, game.Root) ? "." : game.RelativePathOf(directory));
        }
    }

    // A missing folder or a full disk is reported by the checks made for them.
    private static bool CanCreateFilesIn(string directory) => FileOperations.TryCreateProbe(directory) != false;

    /// <summary>Whether a volume lacks room for some bytes and <see cref="SpaceMargin"/>.</summary>
    /// <param name="volumes">Tells the free space.</param>
    /// <param name="location">A full path on the volume.</param>
    /// <param name="bytes">The bytes to write there.</param>
    /// <returns>Null when the volume has room for the bytes, or when its free space is unknown.</returns>
    public static NotEnoughSpaceIssue? SpaceIssue(IVolumes volumes, string location, long bytes)
    {
        var required = bytes + SpaceMargin;
        return volumes.GetAvailableBytes(location) is { } available && available < required
            ? new NotEnoughSpaceIssue(location, required, available)
            : null;
    }

    private void CheckSpace(string location, long bytes, List<InstallIssue> issues)
    {
        if (SpaceIssue(volumes, location, bytes) is { } issue)
        {
            issues.Add(issue);
        }
    }
}
