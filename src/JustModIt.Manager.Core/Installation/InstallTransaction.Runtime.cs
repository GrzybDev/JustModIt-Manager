using System.Globalization;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

internal sealed partial class InstallTransaction
{
    private readonly Dictionary<string, RuntimeOrigin> _origins = new(PathComparison.Comparer);

    // Paths brought back to the state a kept-files record gives them, whose hashes from the check no longer apply.
    private readonly HashSet<string> _brought = new(PathComparison.Comparer);

    // Copies first, since a file the software writes meanwhile stops the install before any change; then every taken-over
    // path a step writes, declared or not, gets the state its record gives it, so the step's first touch records that state
    // as the original.
    private async Task PrepareRuntimeAsync(ProgressTracker progress, CancellationToken cancellationToken)
    {
        var declared = _plan.Runtime.Files.ToDictionary(file => file.FullPath, PathComparison.Comparer);
        foreach (var file in _plan.Runtime.Files.Where(file => !file.TouchedByStep))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyDeclaredAsync(file, progress, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (path, inherited) in _plan.Runtime.Inherited)
        {
            if (!declared.TryGetValue(path, out var file) || file.TouchedByStep)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await BringBackAsync(path, _game.RelativePathOf(path), inherited, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        ApplyBatch();
    }

    private async Task CopyDeclaredAsync(DeclaredFile file, ProgressTracker progress, CancellationToken cancellationToken)
    {
        var (path, relative) = (file.FullPath, file.RelativePath);
        var inherited = _plan.Runtime.Inherited.GetValueOrDefault(path);
        if (inherited is { Entry.Original: null })
        {
            // Absent before the install that left it, so it goes at uninstall whatever it holds then.
            _origins[path] = new RuntimeOrigin(inherited, KeepsOriginal: true);
            return;
        }

        var name = _touched.Count.ToString(CultureInfo.InvariantCulture);
        var backup = Path.Join(BackupDirectory, name);
        FileSnapshot original;
        FileAttributes? attributes;
        if (inherited is not null && await CopyInheritedAsync(inherited, backup, relative, progress, cancellationToken).ConfigureAwait(false))
        {
            (original, attributes) = (inherited.Entry.Original!, inherited.Entry.OriginalAttributes);
            _origins[path] = new RuntimeOrigin(inherited, KeepsOriginal: true);
        }
        else if (File.Exists(path))
        {
            attributes = FileOperations.PreservedAttributesOf(path);
            original = await CopyFoundAsync(path, backup, relative, progress, cancellationToken).ConfigureAwait(false);
            if (inherited is not null)
            {
                _origins[path] = new RuntimeOrigin(inherited, KeepsOriginal: false);
            }
        }
        else
        {
            // The folders the software would create for it are the install's, so the uninstall removes them once empty.
            PlanDirectories(path);
            return;
        }

        _journal.Add(new FileBackedUp(relative, name, original, Moved: false, attributes));
        _touched.Add(path, new TouchedFile(path, relative)
        {
            Original = original,
            Backup = name,
            Attributes = attributes,
            Current = original,
            CopiedOnly = true,
        });
    }

    // False when the copy is gone or no longer holds the original, which leaves the original to what the path holds now.
    private async Task<bool> CopyInheritedAsync(InheritedFile inherited, string backup, string relative, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        if (inherited.CopyPath is not { } copy || !File.Exists(copy)
            || !await FileOperations.CopyCheckedAsync(copy, backup, inherited.Entry.Original!, progress.Bytes, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        await ReadBackAsync(backup, inherited.Entry.Original!, relative, progress, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<FileSnapshot> CopyFoundAsync(string path, string backup, string relative, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        FileSnapshot original;
        try
        {
            original = await FileOperations.CopySharedAsync(path, backup, progress.Bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (FileChangedWhileReadException exception)
        {
            throw new InstallBlockedException([new FileLockedIssue(relative, [])], exception);
        }

        await ReadBackAsync(backup, original, relative, progress, cancellationToken).ConfigureAwait(false);
        return original;
    }

    private async Task ReadBackAsync(string backup, FileSnapshot original, string relative, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        _hook.Before(ChangeKind.ReadBack, relative);
        var copy = await FileOperations.HashAsync(backup, progress.Bytes, cancellationToken).ConfigureAwait(false);
        if (!copy.HasSameContent(original))
        {
            throw new IOException($"The copy of '{relative}' does not match the original.");
        }
    }

    // What the software left goes aside, and the original the record kept takes its place, or nothing for a file that was
    // absent before; an original whose copy is gone or damaged stays what the path holds.
    private async Task BringBackAsync(string path, string relative, InheritedFile inherited, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        if (inherited.Entry.Original is not { } original)
        {
            if (File.Exists(path))
            {
                Displace(path, relative);
            }

            _brought.Add(path);
            _origins[path] = new RuntimeOrigin(inherited, KeepsOriginal: true);
            return;
        }

        var staged = NextStagingPath();
        if (inherited.CopyPath is not { } copy || !File.Exists(copy)
            || !await FileOperations.CopyCheckedAsync(copy, staged, original, progress.Bytes, cancellationToken).ConfigureAwait(false))
        {
            _origins[path] = new RuntimeOrigin(inherited, KeepsOriginal: false);
            return;
        }

        if (File.Exists(path))
        {
            Displace(path, relative);
        }

        var attributes = inherited.Entry.OriginalAttributes;
        _journal.Add(new FileRestored(relative, Path.GetFileName(staged), Moved: false, original));
        _actions.Add(() =>
        {
            _hook.Before(ChangeKind.Restore, relative);
            File.Move(staged, path);
            FileOperations.RestoreAttributes(path, attributes);
        });
        _brought.Add(path);
        _origins[path] = new RuntimeOrigin(inherited, KeepsOriginal: true);
    }

    private void Displace(string path, string relative)
    {
        var aside = NextStagingPath();
        _journal.Add(new FileDisplaced(relative, Path.GetFileName(aside)));
        _actions.Add(() =>
        {
            _hook.Before(ChangeKind.Delete, relative);
            File.Move(path, aside);
        });
    }

    private InstalledFile Declared(InstalledFile entry, DeclaredFile file)
    {
        var origin = _origins.GetValueOrDefault(file.FullPath);
        bool? inheritedUncertainty = origin is null ? null : !origin.KeepsOriginal || origin.From.Entry.OriginalUncertain;
        return entry with
        {
            Runtime = true,
            OriginalUncertain = entry.Original is { } original
                && RuntimeOriginals.IsUncertain(file.Declaration, original, inheritedUncertainty, file.IsTargetBuild),
            Taken = origin is { KeepsOriginal: true } && entry.Original is not null ? origin.From.Taken : null,
            InheritedFrom = origin?.From.From.Record.Id,
        };
    }

    // Folders an earlier install created above paths this one took over go once they are empty, as its own do.
    private List<string> CreatedDirectories()
    {
        List<string> created = [.. _createdDirectories.Where(Directory.Exists).Select(_game.RelativePathOf)];
        var inherited = _plan.Runtime.Inherited.Values.Select(file => file.From).Distinct()
            .SelectMany(record => record.Files.CreatedDirectories)
            .Where(directory => _game.TryResolve(directory) is { } path && Directory.Exists(path))
            .ToList();
        return inherited.Count == 0
            ? created
            : [.. inherited.Concat(created).Distinct(PackagePath.Comparer).OrderBy(directory => directory.Count(c => c == PackagePath.Separator))];
    }

    /// <summary>Where an inherited entry's original comes from.</summary>
    /// <param name="From">The kept-files entry taken over.</param>
    /// <param name="KeepsOriginal">
    /// The entry's original and the version it was taken from carry over; otherwise its copy was gone or damaged, so the
    /// original is what the path held, which may not be the store's.
    /// </param>
    private sealed record RuntimeOrigin(InheritedFile From, bool KeepsOriginal);
}

/// <summary>Tells whether an original of a declared file can be vouched for as its store's.</summary>
internal static class RuntimeOriginals
{
    /// <summary>
    /// Whether a declared file's original may not be its store's: one the package names is proof; otherwise an inherited
    /// original keeps its flag, and on the package's own build any other content is the software's or a hand install's.
    /// </summary>
    /// <param name="declaration">The manifest's declaration of the file.</param>
    /// <param name="original">The original the install recorded.</param>
    /// <param name="inheritedUncertainty">The flag an inherited original carries; null for one the install found itself.</param>
    /// <param name="isTargetBuild">The game is the build a manifest target names.</param>
    public static bool IsUncertain(RuntimeFile declaration, FileSnapshot original, bool? inheritedUncertainty,
        bool isTargetBuild) =>
        !declaration.IsOriginal(original.Size, original.Sha256)
        && (inheritedUncertainty ?? (isTargetBuild && declaration.Originals is { Count: > 0 }));
}
