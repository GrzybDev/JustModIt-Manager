using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Installation;

/// <summary>Whether the copy of an original can put it back.</summary>
internal enum CopyState
{
    Sound,
    Missing,
    Damaged,
}

/// <summary>A file the package's software changes, as an uninstall found it.</summary>
/// <param name="Inspection">What its path holds now.</param>
/// <param name="Copy">Whether the copy of its original can put it back; sound when nothing needs putting back.</param>
/// <param name="SameVersion">The game is the version the original was taken from.</param>
internal sealed record RuntimeCheck(RuntimeInspection Inspection, CopyState Copy, bool SameVersion);

/// <summary>What the uninstall does with the files the package's software changes.</summary>
/// <param name="Steps">What it changes.</param>
/// <param name="Kept">The files it leaves as they are, or puts back with advice to check them, and why.</param>
/// <param name="Left">The files the kept-files record holds, with the reason each is left.</param>
internal sealed record RuntimeDecision(IReadOnlyList<UndoStep> Steps, IReadOnlyList<KeptFile> Kept,
    IReadOnlyList<(RuntimeCheck Check, KeptReason Reason)> Left);

/// <summary>An uninstall's plan for the files the package's software changes, and for the paths it hands over.</summary>
/// <param name="Steps">What it changes.</param>
/// <param name="Kept">The files it leaves as they are, or puts back with advice to check them, and why.</param>
/// <param name="Left">The entries of the kept-files record: files left for the player and notes of handed-over paths.</param>
internal sealed record RuntimeUndoPlan(IReadOnlyList<UndoStep> Steps, IReadOnlyList<KeptFile> Kept, IReadOnlyList<InstalledFile> Left);

/// <summary>
/// Undoes what the package's software changed while the game ran: each path goes back to its state before the install, a
/// file the game did not have always goes, and originals come back only all together, from sound copies, on the game's
/// version they were taken from; otherwise they stay and their copies are kept for the player.
/// </summary>
internal static class RuntimeUndo
{
    /// <summary>
    /// Reads the declared paths, which the caller does only once the game is known not to run, and decides; paths the
    /// replacing package declares are handed over as they are instead.
    /// </summary>
    /// <param name="record">The install.</param>
    /// <param name="files">The install's files.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="handedOver">The full paths the replacing package declares; empty for a plain uninstall.</param>
    /// <param name="current">The game as its store reports it now; null when the store no longer lists it.</param>
    /// <param name="progress">Reports the check of the files.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <exception cref="InstallBlockedException">A file changed while it was read.</exception>
    public static async Task<RuntimeUndoPlan> PlanAsync(InstallRecord record, InstalledFiles files, GameFolder game,
        IReadOnlySet<string> handedOver, InstalledGame? current, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        List<InstalledFile> declared = [.. files.Files.Where(file => file.Runtime && !handedOver.Contains(game.Locate(file.Path)))];
        var tracker = new ProgressTracker(progress, InstallStage.Checking, declared.Count, declared.Sum(file => file.Original?.Size ?? 0));
        var checks = new List<RuntimeCheck>();
        foreach (var inspection in await FileInspector.InspectRuntimeAsync(declared, game, tracker.Bytes, cancellationToken).ConfigureAwait(false))
        {
            checks.Add(new RuntimeCheck(inspection, await CopyStateAsync(record, inspection, cancellationToken).ConfigureAwait(false),
                IsSameVersion(inspection.File, record, current)));
        }

        var decision = Decide(checks);
        var left = new List<InstalledFile>();
        foreach (var (check, reason) in decision.Left)
        {
            left.Add(await LeftAsync(record, check, reason, cancellationToken).ConfigureAwait(false));
        }

        left.AddRange(await HandOverAsync(record, files, game, handedOver, cancellationToken).ConfigureAwait(false));
        return new RuntimeUndoPlan(decision.Steps, decision.Kept, left);
    }

    /// <summary>
    /// Whether the game is the version an entry's original was taken from: the store lists the game in the install's folder,
    /// and its version matches the entry's, or the install's.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="record">The install.</param>
    /// <param name="current">The game as its store reports it now; null when the store no longer lists it.</param>
    public static bool IsSameVersion(InstalledFile entry, InstallRecord record, InstalledGame? current) =>
        current is not null && PathComparison.AreSame(current.InstallDirectory, record.InstallDirectory)
        && (entry.Taken ?? record.GameVersion)?.Matches(GameVersion.Of(current)) == true;

    /// <summary>Decides what to do with each declared file, from what was found alone.</summary>
    /// <param name="checks">The declared files as found.</param>
    public static RuntimeDecision Decide(IReadOnlyList<RuntimeCheck> checks)
    {
        var steps = new List<UndoStep>();
        var kept = new List<KeptFile>();
        var left = new List<(RuntimeCheck, KeptReason)>();
        var candidates = new List<RuntimeCheck>();
        var changed = false;
        foreach (var check in checks)
        {
            var (inspection, file) = (check.Inspection, check.Inspection.File);
            if (inspection.Found is RuntimeFound.Unresolvable or RuntimeFound.Folder)
            {
                // Never written through, and it keeps the others from coming back half.
                changed = true;
                kept.Add(new KeptFile(file.Path, KeptReason.ChangedSinceInstall));
                if (file.Backup is not null && check.Copy == CopyState.Sound)
                {
                    left.Add((check, KeptReason.ChangedSinceInstall));
                }
            }
            else if (file.Original is null)
            {
                // A file the store never shipped can always go.
                if (inspection.Found == RuntimeFound.File)
                {
                    steps.Add(new UndoStep(file, inspection.Path!, UndoAction.Delete));
                }
            }
            else if (inspection.Holds(file.Original))
            {
                Advise(check);
            }
            else
            {
                candidates.Add(check);
            }
        }

        // The software's files belong together, so its originals come back all together or not at all.
        KeptReason? failing = candidates.Count == 0 ? null
            : candidates.Exists(check => !check.SameVersion) ? KeptReason.GameUpdatedSinceInstall
            : candidates.Exists(check => check.Copy == CopyState.Damaged) ? KeptReason.BackupDamaged
            : candidates.Exists(check => check.Copy == CopyState.Missing) ? KeptReason.BackupMissing
            : changed ? KeptReason.ChangedSinceInstall
            : null;
        foreach (var check in candidates)
        {
            if (failing is { } reason)
            {
                kept.Add(new KeptFile(check.Inspection.File.Path, reason));
                left.Add((check, reason));
            }
            else
            {
                steps.Add(new UndoStep(check.Inspection.File, check.Inspection.Path!, UndoAction.Restore));
                Advise(check);
            }
        }

        return new RuntimeDecision(steps, kept, left);

        void Advise(RuntimeCheck check)
        {
            if (check.Inspection.File.OriginalUncertain)
            {
                kept.Add(new KeptFile(check.Inspection.File.Path, KeptReason.OriginalUnknown));
                left.Add((check, KeptReason.OriginalUnknown));
            }
        }
    }

    // Only an original that has to come back is worth hashing its copy for.
    private static async Task<CopyState> CopyStateAsync(InstallRecord record, RuntimeInspection inspection,
        CancellationToken cancellationToken)
    {
        if (inspection.File.Original is not { } original || inspection.Holds(original))
        {
            return CopyState.Sound;
        }

        var copy = inspection.File.Backup is { } name ? new FileInfo(Path.Join(record.Backup.Directory, name)) : null;
        if (copy is not { Exists: true })
        {
            return CopyState.Missing;
        }

        return copy.Length == original.Size
            && (await FileOperations.HashAsync(copy.FullName, progress: null, cancellationToken).ConfigureAwait(false)).HasSameContent(original)
                ? CopyState.Sound
                : CopyState.Damaged;
    }

    // A marker holds the original that was put back, or needed nothing; any other entry what the software left, and the
    // copy while it is sound.
    private static async Task<InstalledFile> LeftAsync(InstallRecord record, RuntimeCheck check, KeptReason reason,
        CancellationToken cancellationToken)
    {
        var file = check.Inspection.File;
        if (reason == KeptReason.OriginalUnknown)
        {
            return new InstalledFile(file.Path, FileChange.Unchanged, file.Original, file.Original, OriginalAttributes: file.OriginalAttributes)
            {
                Runtime = true,
                OriginalUncertain = true,
                Kept = reason,
            };
        }

        return new InstalledFile(file.Path, FileChange.Unchanged, file.Original,
            await ContentAsync(check.Inspection, cancellationToken).ConfigureAwait(false),
            check.Copy == CopyState.Sound ? file.Backup : null, file.OriginalAttributes)
        {
            Runtime = true,
            OriginalUncertain = file.OriginalUncertain,
            Taken = file.Taken ?? record.GameVersion ?? GameVersion.Unrecorded,
            Kept = reason,
        };
    }

    private static async Task<FileSnapshot?> ContentAsync(RuntimeInspection inspection, CancellationToken cancellationToken) =>
        inspection.Found != RuntimeFound.File ? null
        : inspection.Sha256 is { } sha256 ? new FileSnapshot(inspection.Size, sha256)
        : await FileInspector.HashSharedAsync(inspection.Path!, inspection.File.Path, progress: null, cancellationToken).ConfigureAwait(false);

    // Each path the replacing package declares goes over as it is, with its state from before this install: absent for a
    // file this install added, otherwise its original and copy; one this install never listed may hold anything.
    private static async Task<List<InstalledFile>> HandOverAsync(InstallRecord record, InstalledFiles files, GameFolder game,
        IReadOnlySet<string> handedOver, CancellationToken cancellationToken)
    {
        var notes = new List<InstalledFile>();
        if (handedOver.Count == 0)
        {
            return notes;
        }

        var listed = new HashSet<string>(PathComparison.Comparer);
        foreach (var file in files.Files)
        {
            var path = game.Locate(file.Path);
            listed.Add(path);
            if (!handedOver.Contains(path))
            {
                continue;
            }

            var installed = File.Exists(path)
                ? await FileInspector.HashSharedAsync(path, file.Path, progress: null, cancellationToken).ConfigureAwait(false)
                : null;
            notes.Add(file.Original is null
                ? new InstalledFile(file.Path, FileChange.Unchanged, Installed: installed) { Runtime = true }
                : new InstalledFile(file.Path, FileChange.Unchanged, file.Original, installed,
                    file.Backup is { } name && File.Exists(Path.Join(record.Backup.Directory, name)) ? name : null, file.OriginalAttributes)
                {
                    Runtime = true,
                    OriginalUncertain = file.OriginalUncertain,
                    Taken = file.Taken ?? record.GameVersion ?? GameVersion.Unrecorded,
                });
        }

        foreach (var path in handedOver.Where(path => !listed.Contains(path) && File.Exists(path)))
        {
            // The release may have carried the same software without declaring it.
            var relative = game.RelativePathOf(path);
            var content = await FileInspector.HashSharedAsync(path, relative, progress: null, cancellationToken).ConfigureAwait(false);
            notes.Add(new InstalledFile(relative, FileChange.Unchanged, content, content) { Runtime = true, OriginalUncertain = true });
        }

        return notes;
    }
}
