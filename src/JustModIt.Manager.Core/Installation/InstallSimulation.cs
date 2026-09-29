using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

/// <summary>The content a path holds at some point of an install: known by its hash, or still the file on disk.</summary>
/// <param name="Size">The content's size in bytes.</param>
/// <param name="Sha256">The content's hash; null for a file on disk until it is needed.</param>
/// <param name="DiskPath">The file on disk that holds the content; null for content the install writes.</param>
internal sealed record SimulatedFile(long Size, string? Sha256, string? DiskPath)
{
    /// <summary>The file on disk at a path; null when there is none.</summary>
    /// <param name="path">A full path.</param>
    public static SimulatedFile? OnDisk(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new SimulatedFile(file.Length, Sha256: null, path) : null;
    }
}

/// <summary>A hash taken of a file on disk, reusable while its size and time are unchanged.</summary>
/// <param name="Size">The file's size when it was hashed.</param>
/// <param name="LastWriteTimeUtc">The file's time before it was hashed.</param>
/// <param name="Sha256">The hash.</param>
internal sealed record DiskHash(long Size, DateTime LastWriteTimeUtc, string Sha256)
{
    /// <summary>The hash as a snapshot of the file; null once the file changed.</summary>
    /// <param name="path">The file the hash was taken of.</param>
    public FileSnapshot? ForUnchanged(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == Size && file.LastWriteTimeUtc == LastWriteTimeUtc ? new FileSnapshot(Size, Sha256) : null;
    }
}

/// <summary>
/// Plays a package's steps over the game folder without changing it, only hashing patch sources, so a wrong game version
/// is found before any change together with every other problem.
/// </summary>
/// <param name="game">The game folder.</param>
/// <param name="progress">Counts steps and hashed bytes.</param>
/// <param name="baseline">What a path holds before the install; the file on disk unless told otherwise.</param>
internal sealed class InstallSimulation(GameFolder game, ProgressTracker progress, Func<string, SimulatedFile?>? baseline = null)
{
    private readonly Func<string, SimulatedFile?> _baseline = baseline ?? SimulatedFile.OnDisk;

    // A path missing here is as the baseline has it; a null value means the install has removed it by then.
    private readonly Dictionary<string, SimulatedFile?> _overlay = new(PathComparison.Comparer);
    private readonly HashSet<string> _createdDirectories = new(PathComparison.Comparer);

    public List<InstallIssue> Issues { get; } = [];

    /// <summary>The full path of every game-relative path the steps use.</summary>
    public Dictionary<string, string> Paths { get; } = new(PackagePath.Comparer);

    /// <summary>Existing files the install changes, which get backed up and must not be locked, by full path.</summary>
    public Dictionary<string, (string RelativePath, long Size)> Originals { get; } = new(PathComparison.Comparer);

    /// <summary>Every full path the install touches, by game-relative path.</summary>
    public Dictionary<string, string> Touched { get; } = new(PathComparison.Comparer);

    /// <summary>Existing folders that receive new files or folders, which must be writable.</summary>
    public HashSet<string> TargetDirectories { get; } = new(PathComparison.Comparer);

    /// <summary>Hashes taken of files on disk, which the install reuses while the files are unchanged.</summary>
    public Dictionary<string, DiskHash> Hashes { get; } = new(PathComparison.Comparer);

    public long CopiedBytes { get; private set; }

    public long PatchedBytes { get; private set; }

    /// <summary>Originals a rename takes away, whose content reaches the new name as a copy when originals are moved.</summary>
    public long RenamedOriginalBytes { get; private set; }

    /// <summary>Bytes a patch source must be hashed for, which is the bulk of a check.</summary>
    /// <param name="steps">The package's steps.</param>
    public static long BytesToHash(IEnumerable<PackageStep> steps) => steps.OfType<PatchStep>().Sum(step => step.SourceSize);

    /// <summary>Plays the steps in order, collecting every problem in <see cref="Issues"/>.</summary>
    /// <param name="steps">The package's steps.</param>
    /// <param name="cancellationToken">Stops the simulation.</param>
    public async Task RunAsync(IEnumerable<PackageStep> steps, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (step)
            {
                case CopyStep copy:
                    Copy(copy);
                    break;
                case PatchStep patch:
                    await PatchAsync(patch, cancellationToken).ConfigureAwait(false);
                    break;
                case RenameStep rename:
                    Rename(rename);
                    break;
                case DeleteStep delete:
                    Delete(delete);
                    break;
            }

            progress.FinishFile();
        }
    }

    private void Copy(CopyStep step)
    {
        if (Resolve(step.Path) is not { } target || !CanHoldFile(target, step.Path))
        {
            return;
        }

        Touch(target, step.Path);
        PlanParents(target);
        _overlay[target] = new SimulatedFile(step.Size, step.Sha256, DiskPath: null);
        CopiedBytes += step.Size;
    }

    private async Task PatchAsync(PatchStep step, CancellationToken cancellationToken)
    {
        if (Resolve(step.Path) is not { } target)
        {
            return;
        }

        progress.StartFile(step.Path);
        if (Touch(target, step.Path) is not { } current)
        {
            Issues.Add(new MissingFileIssue(step.Path));
            return;
        }

        if (!await HasContentAsync(current, step.SourceSize, step.SourceSha256, cancellationToken).ConfigureAwait(false))
        {
            var alreadyPatched = await HasContentAsync(current, step.TargetSize, step.TargetSha256, cancellationToken)
                .ConfigureAwait(false);
            Issues.Add(new FileMismatchIssue(step.Path, alreadyPatched));
            return;
        }

        _overlay[target] = new SimulatedFile(step.TargetSize, step.TargetSha256, DiskPath: null);
        PatchedBytes += step.TargetSize;
    }

    private void Rename(RenameStep step)
    {
        if (Resolve(step.From) is not { } source || Resolve(step.To) is not { } target)
        {
            return;
        }

        if (StateOf(source) is null)
        {
            Issues.Add(new MissingFileIssue(step.From));
            return;
        }

        // Only the letter case differs and the file system ignores it: the file stays where it is.
        if (PathComparison.AreSame(source, target) || !CanHoldFile(target, step.To))
        {
            return;
        }

        var isOriginal = !_overlay.ContainsKey(source);
        var moved = Touch(source, step.From);
        if (isOriginal && moved is not null)
        {
            RenamedOriginalBytes += moved.Size;
        }

        Touch(target, step.To);
        PlanParents(target);
        _overlay[target] = moved;
        _overlay[source] = null;
    }

    private void Delete(DeleteStep step)
    {
        if (Resolve(step.Path) is not { } target)
        {
            return;
        }

        if (IsDirectory(target))
        {
            Issues.Add(new PathConflictIssue(step.Path));
            return;
        }

        Touch(target, step.Path);
        _overlay[target] = null;
    }

    private string? Resolve(string path)
    {
        if (Paths.TryGetValue(path, out var resolved))
        {
            return resolved;
        }

        if (game.TryResolve(path) is not { } full)
        {
            Issues.Add(new UnsafePathIssue(path));
            return null;
        }

        Paths[path] = full;
        return full;
    }

    private SimulatedFile? StateOf(string path) => _overlay.TryGetValue(path, out var current) ? current : _baseline(path);

    // The first touch records what was there as an original.
    private SimulatedFile? Touch(string path, string relativePath)
    {
        var current = StateOf(path);
        if (_overlay.TryAdd(path, current))
        {
            Touched[relativePath] = path;
            if (current is not null)
            {
                Originals[path] = (relativePath, current.Size);
            }
        }

        return current;
    }

    // Folders an earlier step creates count as folders, so a later file cannot take their place.
    private void PlanParents(string path)
    {
        for (var directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(directory) || _createdDirectories.Contains(directory))
            {
                if (Directory.Exists(directory))
                {
                    TargetDirectories.Add(directory);
                }

                return;
            }

            _createdDirectories.Add(directory);
        }
    }

    private bool IsDirectory(string path) =>
        _createdDirectories.Contains(path) || (!_overlay.ContainsKey(path) && Directory.Exists(path));

    private bool CanHoldFile(string path, string relativePath)
    {
        var blocked = IsDirectory(path) || AncestorIsFile(path);
        if (blocked)
        {
            Issues.Add(new PathConflictIssue(relativePath));
        }

        return !blocked;
    }

    private bool AncestorIsFile(string path)
    {
        for (var directory = Path.GetDirectoryName(path);
             directory is not null && !PathComparison.AreSame(directory, game.Root);
             directory = Path.GetDirectoryName(directory))
        {
            if (StateOf(directory) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> HasContentAsync(SimulatedFile file, long size, string sha256, CancellationToken cancellationToken)
    {
        if (file.Size != size)
        {
            return false;
        }

        var actual = file.Sha256
            ?? (file.DiskPath is { } disk ? await HashOnDiskAsync(disk, cancellationToken).ConfigureAwait(false) : null);
        return actual is not null && FileHash.Matches(actual, sha256);
    }

    private async Task<string> HashOnDiskAsync(string path, CancellationToken cancellationToken)
    {
        if (!Hashes.TryGetValue(path, out var hash))
        {
            var before = File.GetLastWriteTimeUtc(path);
            var snapshot = await FileOperations.HashAsync(path, progress.Bytes, cancellationToken).ConfigureAwait(false);
            hash = new DiskHash(snapshot.Size, before, snapshot.Sha256);
            Hashes[path] = hash;
        }

        return hash.Sha256;
    }
}
