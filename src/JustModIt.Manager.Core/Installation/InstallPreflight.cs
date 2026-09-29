using System.Security.Cryptography;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Core.Installation;

/// <summary>What the check before an install found out, for the install to reuse.</summary>
/// <param name="Paths">The full path of every game-relative path the steps use.</param>
/// <param name="Hashes">Hashes the check took, which the install reuses for files that did not change since.</param>
/// <param name="MovesOriginals">The backup folder is on the game's volume, so originals are moved there, not copied.</param>
/// <param name="BytesTotal">Bytes the install reads and writes, for progress.</param>
internal sealed record InstallPlan(
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyDictionary<string, DiskHash> Hashes,
    bool MovesOriginals,
    long BytesTotal);

/// <summary>Everything an install needs to know before its first change, gathered so every problem is reported at once.</summary>
/// <param name="locks">Tells which files are in use, and by whom.</param>
/// <param name="volumes">Tells whether originals move or are copied, and how much space is free.</param>
/// <param name="slowCopyBytes">Copying this much to another drive takes long enough to tell the player why.</param>
internal sealed class InstallPreflight(IFileLockProbe locks, IVolumes volumes, long slowCopyBytes)
{
    /// <summary>Room for the journal, the record and file system overhead on top of the files themselves.</summary>
    internal const long SpaceMargin = 64L << 20;

    /// <summary>Checks a package against the game without changing anything.</summary>
    /// <param name="package">The opened package.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="installation">The game as its store reports it.</param>
    /// <param name="backup">Where the install would keep the originals.</param>
    /// <param name="owners">Records of other installs on this game, by the full path of each file they own.</param>
    /// <param name="knownIssues">Problems the caller found already.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <param name="baseline">What a path holds before the install, when that is not the file on disk.</param>
    /// <returns>Every problem and warning, and the plan the install follows when it can go ahead.</returns>
    public async Task<(PreflightReport Report, InstallPlan Plan)> CheckAsync(IPackage package, GameFolder game,
        InstalledGame installation, BackupFolder backup, IReadOnlyDictionary<string, Guid> owners,
        IEnumerable<InstallIssue> knownIssues, IProgress<InstallProgress>? progress, CancellationToken cancellationToken,
        Func<string, SimulatedFile?>? baseline = null)
    {
        var tracker = new ProgressTracker(progress, InstallStage.Checking, package.Steps.Count,
            InstallSimulation.BytesToHash(package.Steps));
        var simulation = new InstallSimulation(game, tracker, baseline);
        await simulation.RunAsync(package.Steps, cancellationToken).ConfigureAwait(false);

        var issues = knownIssues.ToList();
        issues.AddRange(simulation.Issues);
        issues.AddRange(simulation.Touched
            .Where(touched => owners.ContainsKey(touched.Value))
            .Select(touched => new FileOwnedByOtherInstallIssue(touched.Key, owners[touched.Value])));
        issues.AddRange(AccessIssues(locks.Probe(simulation.Originals.Keys, cancellationToken),
            [game.Root, .. simulation.TargetDirectories], game));

        var movesOriginals = volumes.AreOnSameVolume(game.Root, backup.Directory);
        var newBytes = simulation.CopiedBytes + simulation.PatchedBytes;
        var originalBytes = simulation.Originals.Values.Sum(original => original.Size);
        var warnings = new List<InstallWarning>();
        // A renamed original reaches its new name as a copy, since the original itself moves to the backup folder.
        CheckSpace(game.Root, newBytes + (movesOriginals ? simulation.RenamedOriginalBytes : 0), issues);
        if (!movesOriginals && originalBytes > 0)
        {
            CheckSpace(backup.Directory, originalBytes, issues);
            if (originalBytes >= slowCopyBytes)
            {
                warnings.Add(new SlowBackupCopyWarning(originalBytes));
            }
        }

        warnings.AddRange(TargetWarnings(package.Manifest, installation));
        // Moved originals are hashed unless the check already did, and renamed ones are copied to their new name;
        // copied originals are written and read back. New content is written once.
        var hashed = simulation.Originals.Where(original => simulation.Hashes.ContainsKey(original.Key))
            .Sum(original => original.Value.Size);
        var bytesTotal = newBytes + (movesOriginals
            ? originalBytes - hashed + simulation.RenamedOriginalBytes
            : 2 * originalBytes);
        return (new PreflightReport([.. issues], [.. warnings]),
            new InstallPlan(simulation.Paths, simulation.Hashes, movesOriginals, bytesTotal));
    }

    public static IEnumerable<InstallWarning> TargetWarnings(PackageManifest? manifest, InstalledGame game)
    {
        if (manifest is null || manifest.Targets.Count == 0)
        {
            yield break;
        }

        var target = manifest.Targets.FirstOrDefault(target => target.Store == game.Key.Store && target.Id == game.Key.StoreGameId);
        if (target is null)
        {
            yield return new OtherGameWarning([.. manifest.Targets]);
        }
        else if (target.BuildId is { } build && build != game.BuildId)
        {
            yield return new OtherGameBuildWarning(build, game.BuildId);
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

    private static bool CanCreateFilesIn(string directory)
    {
        var probe = Path.Join(directory, $".{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.probe");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            // A missing folder or a full disk is reported by the checks made for them.
            return true;
        }
    }

    private void CheckSpace(string location, long bytes, List<InstallIssue> issues)
    {
        var required = bytes + SpaceMargin;
        if (volumes.GetAvailableBytes(location) is { } available && available < required)
        {
            issues.Add(new NotEnoughSpaceIssue(location, required, available));
        }
    }
}
