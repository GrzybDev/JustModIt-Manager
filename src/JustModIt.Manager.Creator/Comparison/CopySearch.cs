using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>
/// Finds the author's files that hold a game file's content under another path, which a package must never carry and
/// which for a new file most likely means the author renamed a game file, as the package can do instead.
/// </summary>
internal sealed class CopySearch
{
    /// <summary>Small files share content by chance (empty configs, placeholder images), and they carry no game assets.</summary>
    public const long MinSize = 1 << 10;

    private readonly GameIndex _game;
    private readonly List<FolderComparer.Candidate> _candidates;

    private CopySearch(GameIndex game, List<FolderComparer.Candidate> candidates)
    {
        _game = game;
        _candidates = candidates;
    }

    /// <summary>
    /// Picks the author's files worth searching; one of the same size as its game counterpart is left out, as being a
    /// copy of yet another game file of that very size is too unlikely to hash more for.
    /// </summary>
    /// <param name="game">The game listing to search.</param>
    /// <param name="candidates">The author's files, as listed.</param>
    public static CopySearch Plan(GameIndex game, List<FolderComparer.Candidate> candidates) =>
        new(game,
        [
            .. candidates.Where(candidate => !candidate.SameSize && candidate.Stamp.Size >= MinSize
                && game.WithSize(candidate.Stamp.Size).Any(file => !IsSame(file.Path, candidate.Path))),
        ]);

    /// <summary>The bytes the search hashes that the comparison does not hash anyway.</summary>
    /// <param name="all">Every author's file of the comparison.</param>
    public long BytesToHash(List<FolderComparer.Candidate> all)
    {
        var hashedAnyway = all.Where(candidate => candidate.SameSize).Select(candidate => candidate.Game!.FullPath)
            .ToHashSet(PathComparison.Comparer);
        var gameFiles = _candidates
            .SelectMany(candidate => _game.WithSize(candidate.Stamp.Size).Where(file => !IsSame(file.Path, candidate.Path)))
            .DistinctBy(file => file.FullPath, PathComparison.Comparer)
            .Where(file => !hashedAnyway.Contains(file.FullPath));
        return _candidates.Sum(candidate => candidate.Stamp.Size) + gameFiles.Sum(file => file.Stamp.Size);
    }

    /// <summary>Finds the copies, and proposes a rename for each new file with exactly one likely game file behind it.</summary>
    /// <param name="files">The author's files, classified.</param>
    /// <param name="missing">Game files the author probably removed.</param>
    /// <param name="hashes">The hashes the comparison already made.</param>
    /// <param name="tracker">Reports the search's progress.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>What was found, by the path of the author's file.</returns>
    public async Task<Dictionary<string, (string CopyOf, string? RenameFrom)>> RunAsync(IReadOnlyList<ComparedFile> files,
        IReadOnlyList<ComparedFile> missing, ContentHashes hashes, FolderComparer.Tracker tracker, CancellationToken cancellationToken)
    {
        var replacedOrRemoved = files.Where(file => file.Difference == FileDifference.Changed).Concat(missing)
            .Select(file => file.Path).ToHashSet(PackagePath.Comparer);
        var keptUnchanged = files.Where(file => file.Difference == FileDifference.Identical).Select(file => file.Path)
            .ToHashSet(PackagePath.Comparer);
        var found = new Dictionary<string, (string CopyOf, string? RenameFrom)>(PackagePath.Comparer);
        var proposals = new List<(string NewPath, string From)>();
        foreach (var candidate in _candidates)
        {
            tracker.Searching(candidate.Path);
            var matches = await MatchesAsync(candidate, hashes, tracker.Bytes, cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                continue;
            }

            found[candidate.Path] = (Prefer(matches, candidate.Path, replacedOrRemoved)[0], null);
            // A game file that the author's folder keeps unchanged stays where it is, so this is a duplicate. A tie that
            // the preferences cannot break is left alone too: renaming the wrong copy could break the game.
            var sources = Prefer([.. matches.Where(path => !keptUnchanged.Contains(path))], candidate.Path, replacedOrRemoved);
            if (candidate.Game is null && sources.Count == 1)
            {
                proposals.Add((candidate.Path, sources[0]));
            }
        }

        foreach (var group in proposals.GroupBy(proposal => proposal.From, PackagePath.Comparer))
        {
            var chosen = Narrow(Narrow([.. group], proposal => SameName(proposal.From, proposal.NewPath)),
                proposal => SameFolder(proposal.From, proposal.NewPath))[0];
            found[chosen.NewPath] = found[chosen.NewPath] with { RenameFrom = chosen.From };
        }

        return found;
    }

    private async Task<List<string>> MatchesAsync(FolderComparer.Candidate candidate, ContentHashes hashes, IProgress<long> bytes,
        CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        if (await hashes.TryHashAsync(candidate.FullPath, candidate.Stamp.Size, bytes, cancellationToken).ConfigureAwait(false)
            is not { } hash)
        {
            return matches;
        }

        foreach (var file in _game.WithSize(candidate.Stamp.Size).Where(file => !IsSame(file.Path, candidate.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await hashes.TryHashAsync(file.FullPath, file.Stamp.Size, bytes, cancellationToken).ConfigureAwait(false)
                    is { } gameHash
                && FileHash.Matches(gameHash, hash))
            {
                matches.Add(file.Path);
            }
        }

        return matches;
    }

    // Same name, then same folder, then a game file the author replaced or removed.
    private static List<string> Prefer(List<string> matches, string newPath, HashSet<string> replacedOrRemoved) =>
        Narrow(Narrow(Narrow(matches, path => SameName(path, newPath)), path => SameFolder(path, newPath)),
            replacedOrRemoved.Contains);

    private static List<T> Narrow<T>(List<T> items, Func<T, bool> preference)
    {
        var preferred = items.Where(preference).ToList();
        return preferred.Count > 0 ? preferred : items;
    }

    private static bool IsSame(string first, string second) => PackagePath.Comparer.Equals(first, second);

    private static bool SameName(string first, string second) => IsSame(GameIndex.NameOf(first), GameIndex.NameOf(second));

    private static bool SameFolder(string first, string second) =>
        IsSame(GameIndex.DirectoryOf(first), GameIndex.DirectoryOf(second));
}
