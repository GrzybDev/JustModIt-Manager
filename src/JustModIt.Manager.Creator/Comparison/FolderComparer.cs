using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>
/// Sets the author's folder, laid out like the game, against a clean game folder: size first, SHA-256 only for files of
/// the same size, one listing of the game for everything.
/// </summary>
/// <param name="options">The app's folder names in games.</param>
/// <param name="fileSystem">Lists and reads the folders; null for the real file system.</param>
public sealed class FolderComparer(CreatorOptions options, IFileSystem? fileSystem = null)
{
    // The default skips hidden and system files, which in an author's folder are .git, Thumbs.db and desktop.ini.
    private static readonly EnumerationOptions AuthorFiles = new() { RecurseSubdirectories = true, IgnoreInaccessible = false };

    private readonly IFileSystem _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;

    /// <summary>Compares the author's folder with the game folder, off the calling thread.</summary>
    /// <param name="request">The folders, and the installs the app keeps records of.</param>
    /// <param name="progress">Receives the comparison's progress; null for none.</param>
    /// <param name="cancellationToken">Cancels the comparison.</param>
    /// <exception cref="CreatorBlockedException">
    /// A folder is missing, they overlap, the game holds an install, or another app manages files of it.
    /// </exception>
    public Task<FolderComparison> CompareAsync(ComparisonRequest request, IProgress<CompareProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => CompareCoreAsync(request, progress, cancellationToken), cancellationToken);
    }

    private async Task<FolderComparison> CompareCoreAsync(ComparisonRequest request, IProgress<CompareProgress>? progress,
        CancellationToken cancellationToken)
    {
        var (gameDirectory, authorDirectory) = OpenFolders(request);
        progress?.Report(new CompareProgress(ComparisonStage.ListingFiles, null, 0, 0, 0, 0));
        var game = GameIndex.Build(_fileSystem, gameDirectory, options.HiddenFolderName, cancellationToken);
        var candidates = ListAuthorFiles(game, authorDirectory, cancellationToken);
        var hashes = new ContentHashes(_fileSystem);
        var copySearch = CopySearch.Plan(game, candidates);
        var bytesTotal = candidates.Where(candidate => candidate.SameSize).Sum(candidate => 2 * candidate.Stamp.Size)
            + copySearch.BytesToHash(candidates);
        var tracker = new Tracker(progress, candidates.Count, bytesTotal);

        var files = new List<ComparedFile>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Start(ComparisonStage.Comparing, candidate.Path, files.Count);
            files.Add(await ClassifyAsync(candidate, hashes, tracker.Bytes, cancellationToken).ConfigureAwait(false));
        }

        var missing = RemovedFiles(game, files);
        var copies = await copySearch.RunAsync(files, missing, hashes, tracker, cancellationToken).ConfigureAwait(false);
        tracker.Finish();
        ValueList<ComparedFile> compared =
        [
            .. files.Select(file => Misplaced(copies.TryGetValue(file.Path, out var found)
                    ? file with { CopyOf = found.CopyOf, RenameFrom = found.RenameFrom }
                    : file, game))
                .Concat(missing)
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase),
        ];
        return new FolderComparison(gameDirectory, authorDirectory, compared, game);
    }

    /// <summary>
    /// Whether the author's files, or the game files they were compared with, changed since the comparison, so it has
    /// to run again before its plan can be built.
    /// </summary>
    /// <param name="comparison">A comparison this comparer made.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public Task<bool> HasChangedAsync(FolderComparison comparison, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return Task.Run(() =>
        {
            var stamps = comparison.Files.Where(file => file.AuthorPath is not null)
                .ToDictionary(file => file.AuthorPath!, file => file.Author!.Value, PackagePath.Comparer);
            try
            {
                var listed = 0;
                foreach (var entry in _fileSystem.Enumerate(comparison.AuthorDirectory, "*", AuthorFiles))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.IsDirectory || entry.IsLink)
                    {
                        continue;
                    }

                    if (!stamps.TryGetValue(PackagePath.FromFullPath(comparison.AuthorDirectory, entry.FullPath), out var stamp)
                        || stamp != FileStamp.Of(entry))
                    {
                        return true;
                    }

                    listed++;
                }

                return listed != stamps.Count
                    || comparison.Files.Select(file => file.Game).OfType<GameFile>()
                        .Any(game => _fileSystem.GetEntry(game.FullPath) is not { IsDirectory: false } entry || FileStamp.Of(entry) != game.Stamp);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }, cancellationToken);
    }

    private (string Game, string Author) OpenFolders(ComparisonRequest request)
    {
        var blocks = new List<CreatorBlock>();
        if (!_fileSystem.DirectoryExists(request.GameDirectory))
        {
            blocks.Add(new CreatorBlock(CreatorBlockReason.GameFolderMissing, request.GameDirectory));
        }

        if (!_fileSystem.DirectoryExists(request.AuthorDirectory))
        {
            blocks.Add(new CreatorBlock(CreatorBlockReason.AuthorFolderMissing, request.AuthorDirectory));
        }

        if (blocks.Count > 0)
        {
            throw new CreatorBlockedException(blocks);
        }

        var game = _fileSystem.GetCanonicalPath(request.GameDirectory);
        var author = _fileSystem.GetCanonicalPath(request.AuthorDirectory);
        if (PathComparison.Overlaps(game, author))
        {
            throw new CreatorBlockedException([new CreatorBlock(CreatorBlockReason.FoldersOverlap, author)]);
        }

        CreatorBlockedException.ThrowIfManaged(game, request.Installs, _fileSystem, options);
        return (game, author);
    }

    private List<Candidate> ListAuthorFiles(GameIndex game, string authorDirectory, CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();
        foreach (var entry in _fileSystem.Enumerate(authorDirectory, "*", AuthorFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory || entry.IsLink)
            {
                continue;
            }

            var authorPath = PackagePath.FromFullPath(authorDirectory, entry.FullPath);
            var path = game.Spell(authorPath);
            candidates.Add(new Candidate(path, authorPath, entry.FullPath, FileStamp.Of(entry),
                game.TryGetFile(path, out var gameFile) ? gameFile : null));
        }

        candidates.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return candidates;
    }

    private static async Task<ComparedFile> ClassifyAsync(Candidate candidate, ContentHashes hashes, IProgress<long> bytes,
        CancellationToken cancellationToken)
    {
        var file = new ComparedFile
        {
            Path = candidate.Path,
            AuthorPath = candidate.AuthorPath,
            Author = candidate.Stamp,
            Game = candidate.Game,
            Difference = FileDifference.New,
        };
        if (candidate.Game is not { } game)
        {
            return file;
        }

        if (!candidate.SameSize)
        {
            return file with { Difference = FileDifference.Changed };
        }

        var author = await hashes.HashAsync(candidate.FullPath, bytes, cancellationToken).ConfigureAwait(false);
        var original = await hashes.HashAsync(game.FullPath, bytes, cancellationToken).ConfigureAwait(false);
        return FileHash.Matches(author, original)
            ? file with { Difference = FileDifference.Identical, CopyOf = game.Path }
            : file with { Difference = FileDifference.Changed };
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
    /// <param name="Path">Game-relative, spelled like the game wherever the game has the file or its folders.</param>
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
