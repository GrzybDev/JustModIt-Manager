using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>
/// Sets the author's folder, laid out like the game, against a clean game folder: size first, SHA-256 only for files of
/// the same size, one listing of the game for everything. Each file is hashed once however many comparisons need it, so
/// a comparer lasts for one pass over the folders.
/// </summary>
/// <param name="options">The app's folder names in games.</param>
/// <param name="fileSystem">Lists and reads the folders; null for the real file system.</param>
/// <param name="access">Gives up on opening a folder whose drive does not answer; null waits for it.</param>
public sealed class FolderComparer(CreatorOptions options, IFileSystem? fileSystem = null, LibraryAccess? access = null)
{
    // The default skips hidden and system files, which in an author's folder are .git, Thumbs.db and desktop.ini.
    private static readonly EnumerationOptions AuthorFiles = new() { RecurseSubdirectories = true, IgnoreInaccessible = false };

    private readonly IFileSystem _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
    private readonly LibraryAccess _access = access ?? LibraryAccess.Unlimited;
    private readonly ContentHashes _hashes = new(fileSystem ?? PhysicalFileSystem.Instance);

    /// <summary>Lists a game folder off the calling thread, once however many packages are compared with it.</summary>
    /// <param name="gameDirectory">The game folder.</param>
    /// <param name="installs">Every install the app keeps a record of; one on the game refuses the listing.</param>
    /// <param name="cancellationToken">Stops the listing, or the wait for one that a drive holds up.</param>
    /// <exception cref="CreatorBlockedException">
    /// The folder is missing, the game holds an install, or another app manages files of it.
    /// </exception>
    /// <exception cref="TimeoutException">The folder's drive does not answer.</exception>
    public Task<GameIndex> ListGameAsync(string gameDirectory, IReadOnlyList<ExistingInstall> installs, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            if (!await FolderExistsAsync(gameDirectory, cancellationToken).ConfigureAwait(false))
            {
                throw new CreatorBlockedException([new CreatorBlock(CreatorBlockReason.GameFolderMissing, gameDirectory)]);
            }

            var game = _fileSystem.GetCanonicalPath(gameDirectory);
            CreatorBlockedException.ThrowIfManaged(game, installs, _fileSystem, options);
            return GameIndex.Build(_fileSystem, game, options.HiddenFolderName, cancellationToken);
        }, cancellationToken).WaitAsync(cancellationToken);

    /// <summary>Compares the author's folder with the game as listed, off the calling thread.</summary>
    /// <param name="request">The game, the author's folder and what to leave out of it.</param>
    /// <param name="progress">Receives the comparison's progress; null for none.</param>
    /// <param name="cancellationToken">Cancels the comparison, or the wait for one that a drive holds up.</param>
    /// <exception cref="CreatorBlockedException">The folder is missing or overlaps the game's.</exception>
    /// <exception cref="TimeoutException">The folder's drive does not answer.</exception>
    public Task<FolderComparison> CompareAsync(ComparisonRequest request, IProgress<CompareProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => CompareCoreAsync(request, progress, cancellationToken), cancellationToken).WaitAsync(cancellationToken);
    }

    // Only opening a folder has a time limit, since a large listing may well outlast it; a stop ends the wait for one.
    private Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken) =>
        _access.ReadAsync(folder, _ => Task.FromResult(_fileSystem.DirectoryExists(folder)), cancellationToken);

    private async Task<FolderComparison> CompareCoreAsync(ComparisonRequest request, IProgress<CompareProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!await FolderExistsAsync(request.AuthorDirectory, cancellationToken).ConfigureAwait(false))
        {
            throw new CreatorBlockedException([new CreatorBlock(CreatorBlockReason.AuthorFolderMissing, request.AuthorDirectory)]);
        }

        var game = request.Game;
        var authorDirectory = _fileSystem.GetCanonicalPath(request.AuthorDirectory);
        if (PathComparison.Overlaps(game.Root, authorDirectory))
        {
            throw new CreatorBlockedException([new CreatorBlock(CreatorBlockReason.FoldersOverlap, authorDirectory)]);
        }

        progress?.Report(new CompareProgress(ComparisonStage.ListingFiles, null, 0, 0, 0, 0));
        var candidates = ListAuthorFiles(request, authorDirectory, cancellationToken);
        var copySearch = CopySearch.Plan(game, request.OtherVersions, candidates);
        var bytesTotal = candidates.Where(candidate => candidate.SameSize)
                .Sum(candidate => _hashes.BytesToRead(candidate.FullPath, candidate.Stamp.Size) + _hashes.BytesToRead(candidate.Game!.FullPath, candidate.Stamp.Size))
            + copySearch.BytesToHash(candidates, _hashes);
        var tracker = new Tracker(progress, candidates.Count, bytesTotal);

        var files = new List<ComparedFile>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Start(ComparisonStage.Comparing, candidate.Path, files.Count);
            files.Add(await ClassifyAsync(candidate, request.OtherVersions, tracker.Bytes, cancellationToken).ConfigureAwait(false));
        }

        var missing = RemovedFiles(game, files);
        var copies = await copySearch.RunAsync(files, missing, _hashes, tracker, cancellationToken).ConfigureAwait(false);
        tracker.Finish();
        ValueList<ComparedFile> compared =
        [
            .. files.Select(file => Misplaced(copies.TryGetValue(file.Path, out var found)
                    ? file with { CopyOf = found.CopyOf, RenameFrom = found.RenameFrom }
                    : file, game))
                .Concat(missing)
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase),
        ];
        return new FolderComparison(game.Root, authorDirectory, compared, game);
    }

    private List<Candidate> ListAuthorFiles(ComparisonRequest request, string authorDirectory, CancellationToken cancellationToken)
    {
        var game = request.Game;
        var skipped = request.SkippedFolders.Select(_fileSystem.GetCanonicalPath).ToList();
        var candidates = new List<Candidate>();
        foreach (var entry in AuthorFilesIn(authorDirectory, cancellationToken)
            .Where(entry => !skipped.Exists(folder => PathComparison.IsWithin(folder, entry.FullPath))))
        {
            var authorPath = PackagePath.FromFullPath(authorDirectory, entry.FullPath);
            // A version's own folder may lie like that version; a path in the project's layout stays as it is.
            var path = game.Spell(game.Layout.ToPackage(authorPath));
            candidates.Add(new Candidate(path, authorPath, entry.FullPath, FileStamp.Of(entry),
                game.TryGetFile(path, out var gameFile) ? gameFile : null));
        }

        candidates.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return candidates;
    }

    private IEnumerable<FileEntry> AuthorFilesIn(string authorDirectory, CancellationToken cancellationToken)
    {
        foreach (var entry in _fileSystem.Enumerate(authorDirectory, "*", AuthorFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsDirectory && !entry.IsLink)
            {
                yield return entry;
            }
        }
    }

    private async Task<ComparedFile> ClassifyAsync(Candidate candidate, IReadOnlyList<GameIndex> others, IProgress<long> bytes,
        CancellationToken cancellationToken)
    {
        var file = new ComparedFile
        {
            Path = candidate.Path,
            AuthorPath = candidate.AuthorPath,
            Author = candidate.Stamp,
            Game = candidate.Game,
            Difference = candidate.Game is null ? FileDifference.New : FileDifference.Changed,
        };
        if (candidate.SameSize
            && FileHash.Matches(await _hashes.HashAsync(candidate.FullPath, bytes, cancellationToken).ConfigureAwait(false),
                await _hashes.HashAsync(candidate.Game!.FullPath, bytes, cancellationToken).ConfigureAwait(false)))
        {
            return file with { Difference = FileDifference.Identical, CopyOf = candidate.Game.Path };
        }

        // Off the progress: it only takes what differs from this version, and the other version's comparison mostly hashed it already.
        foreach (var other in others)
        {
            if (other.TryGetFile(candidate.Path, out var theirs) && theirs.Stamp.Size == candidate.Stamp.Size
                && await _hashes.TryHashAsync(candidate.FullPath, candidate.Stamp.Size, null, cancellationToken).ConfigureAwait(false) is { } author
                && await _hashes.TryHashAsync(theirs.FullPath, theirs.Stamp.Size, null, cancellationToken).ConfigureAwait(false) is { } original
                && FileHash.Matches(author, original))
            {
                return file with { Difference = FileDifference.Identical, CopyOf = candidate.Path };
            }
        }

        return file;
    }

    // A new file at the top of the author's folder whose name the game keeps in one folder only was most likely meant
    // for that folder, so it is no rename even when its content is the game's.
    private static ComparedFile Misplaced(ComparedFile file, GameIndex game) =>
        file.Difference == FileDifference.New && !file.AuthorPath!.Contains(PackagePath.Separator, StringComparison.Ordinal)
        && game.FoldersWith(file.Path).Distinct(PackagePath.Comparer).ToList() is [var folder]
            ? file with { ExpectedFolder = folder, RenameFrom = null }
            : file;

    // A folder the author copied whole shows which of its files the author deleted. A folder holding some of the game's
    // files says nothing about the others, so only folders where the author kept three quarters of them count.
    private static List<ComparedFile> RemovedFiles(GameIndex game, List<ComparedFile> files)
    {
        var present = files.Where(file => file.Game is not null).Select(file => file.Path).ToHashSet(PackagePath.Comparer);
        return
        [
            .. files.Select(file => GameIndex.DirectoryOf(file.Path))
                .Distinct(PackagePath.Comparer)
                .Select(game.FilesIn)
                .Where(inFolder => inFolder.Count(file => present.Contains(file.Path)) is var kept && kept >= 2
                    && kept * 4 >= inFolder.Count * 3)
                .SelectMany(inFolder => inFolder.Where(file => !present.Contains(file.Path)))
                .Select(file => new ComparedFile { Path = file.Path, Difference = FileDifference.Missing, Game = file }),
        ];
    }

    /// <summary>An author's file before it is classified.</summary>
    /// <param name="Path">In the project's layout, spelled like the game wherever the game has the file or its folders.</param>
    /// <param name="AuthorPath">Relative to the author's folder, as the author spells it.</param>
    /// <param name="FullPath">The author's file on disk.</param>
    /// <param name="Stamp">The author's file as listed.</param>
    /// <param name="Game">The game's file at <paramref name="Path"/>; null when the game has none.</param>
    internal sealed record Candidate(string Path, string AuthorPath, string FullPath, FileStamp Stamp, GameFile? Game)
    {
        public bool SameSize => Game?.Stamp.Size == Stamp.Size;
    }

    /// <summary>Reports a comparison's progress, with the bytes hashed counted across its stages.</summary>
    /// <param name="sink">Receives the progress; null for none.</param>
    /// <param name="fileCount">The author's files in all.</param>
    /// <param name="bytesTotal">Bytes to hash in all.</param>
    internal sealed class Tracker(IProgress<CompareProgress>? sink, int fileCount, long bytesTotal)
    {
        private ComparisonStage _stage;
        private string? _file;
        private int _filesDone;
        private long _bytesDone;

        /// <summary>Receives the bytes hashed.</summary>
        public IProgress<long> Bytes => field ??= new InlineProgress<long>(AddBytes);

        /// <summary>Reports the stage and the file being worked on.</summary>
        /// <param name="stage">What the comparison is doing now.</param>
        /// <param name="file">The game path of the author's file; null for none.</param>
        /// <param name="filesDone">The author's files compared so far.</param>
        public void Start(ComparisonStage stage, string? file, int filesDone)
        {
            (_stage, _file, _filesDone) = (stage, file, filesDone);
            Report();
        }

        /// <summary>Reports the copy search at an author's file; every file is compared by then.</summary>
        /// <param name="file">The game path of the author's file.</param>
        public void Searching(string file) => Start(ComparisonStage.FindingCopies, file, fileCount);

        /// <summary>
        /// Reports the end with every byte done, since an author's file that could not be read leaves the game files of
        /// its size unhashed.
        /// </summary>
        public void Finish()
        {
            _bytesDone = bytesTotal;
            Start(ComparisonStage.FindingCopies, null, fileCount);
        }

        private void AddBytes(long bytes)
        {
            _bytesDone += bytes;
            Report();
        }

        private void Report() =>
            sink?.Report(new CompareProgress(_stage, _file, _filesDone, fileCount, Math.Min(_bytesDone, bytesTotal), bytesTotal));
    }
}
