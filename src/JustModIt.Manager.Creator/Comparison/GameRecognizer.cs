using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>A detected game an author's folder may be made for.</summary>
/// <param name="Key">The game's store and id there.</param>
/// <param name="Name">As its store names it.</param>
/// <param name="Directory">Its folder.</param>
public sealed record RecognitionCandidate(GameKey Key, string Name, string Directory);

/// <summary>How well an author's folder fits a game.</summary>
/// <param name="Key">The game.</param>
/// <param name="Score">Two for each sampled path the game has as a file, one for each whose folder alone it has.</param>
/// <param name="NameMatches">The folder's name holds the game's.</param>
public sealed record RecognizedGame(GameKey Key, int Score, bool NameMatches);

/// <summary>What an author's folder tells of the game it is made for.</summary>
/// <param name="FileCount">The files in the folder.</param>
/// <param name="Games">The games its files or its name fit, best first.</param>
/// <param name="Winner">
/// The game the folder clearly fits: twice the score of any other title, or the only title its name holds; null when none.
/// </param>
public sealed record Recognition(int FileCount, IReadOnlyList<RecognizedGame> Games, RecognizedGame? Winner);

/// <summary>
/// Tells which detected game an author's folder is made for by a sample of its paths, looked up in each game that has one of
/// the sample's top-level names. No file is opened and nothing is hashed, so it takes a moment however large the games are.
/// </summary>
public static class GameRecognizer
{
    private const int SampleSize = 32;

    private static readonly EnumerationOptions TopLevel = new() { IgnoreInaccessible = true };

    /// <summary>Recognises the game an author's folder is made for, off the calling thread.</summary>
    /// <param name="folder">The author's folder, laid out like the game.</param>
    /// <param name="candidates">The detected games.</param>
    /// <param name="access">Gives up on a folder whose drive does not answer, so one game's drive holds up only that game.</param>
    /// <param name="fileSystem">Lists the folders and looks the paths up.</param>
    /// <param name="cancellationToken">Stops the recognition, or the wait for a drive.</param>
    /// <exception cref="TimeoutException">The author's folder's drive does not answer.</exception>
    public static Task<Recognition> RecognizeAsync(string folder, IReadOnlyList<RecognitionCandidate> candidates, LibraryAccess access,
        IFileSystem fileSystem, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        // Only opening the folder has a time limit, since a large listing may well outlast it.
        if (!await access.ReadAsync(folder, _ => Task.FromResult(fileSystem.DirectoryExists(folder)), cancellationToken).ConfigureAwait(false))
        {
            return new Recognition(0, [], null);
        }

        var paths = FileChanges.List(fileSystem, folder, [], cancellationToken).Select(entry => PackagePath.FromFullPath(folder, entry.FullPath)).ToList();
        // The deepest paths tell games apart best, and each top-level folder gives its share, so files of a folder the game
        // lacks, e.g. a loader's, cannot fill the sample alone.
        var sample = paths.GroupBy(PackagePath.FirstSegment, PackagePath.Comparer)
            .SelectMany(group => group.OrderByDescending(Depth).ThenBy(path => path, StringComparer.Ordinal).Select((path, rank) => (Path: path, Rank: rank)))
            .OrderBy(item => item.Rank).ThenByDescending(item => Depth(item.Path)).ThenBy(item => item.Path, StringComparer.Ordinal)
            .Take(SampleSize).Select(item => item.Path).ToList();
        var tops = sample.Select(PackagePath.FirstSegment).ToHashSet(PackagePath.Comparer);
        var name = SearchText.Fold(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));

        // A game's own folder cannot be recognised by its own files. Each game is read through its drive's turn, so a drive
        // that does not answer holds up only its games.
        var scored = await Task.WhenAll(candidates.Where(game => !PathComparison.Overlaps(game.Directory, folder)).Select(async game =>
        {
            var score = 0;
            try
            {
                score = await access.ReadAsync(game.Directory, _ => Task.FromResult(Score(fileSystem, game.Directory, sample, tops)), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
            {
            }

            return (Game: game, Score: score, Title: SearchText.Fold(game.Name));
        })).ConfigureAwait(false);

        var ranked = scored.Select(each => (each.Title, Game: new RecognizedGame(each.Game.Key, each.Score, name.Contains(each.Title, StringComparison.Ordinal))))
            .Where(each => each.Game.Score > 0 || each.Game.NameMatches)
            .OrderByDescending(each => each.Game.Score).ThenByDescending(each => each.Game.NameMatches).ToList();
        RecognizedGame? winner = null;
        if (ranked.Count > 0)
        {
            // Copies of one title from several stores score alike, so only another title is a rival.
            var (title, best) = ranked[0];
            var rivals = ranked.Where(each => each.Title != title).Select(each => each.Game).ToList();
            winner = (best.Score > 0 && (rivals.Count == 0 || best.Score >= 2 * rivals[0].Score)) || (best.NameMatches && !rivals.Exists(rival => rival.NameMatches))
                ? best
                : null;
        }

        return new Recognition(paths.Count, [.. ranked.Select(each => each.Game)], winner);
    }, cancellationToken).WaitAsync(cancellationToken);

    // A game that has none of the sample's top-level names costs one listing of its top level and nothing more.
    private static int Score(IFileSystem fileSystem, string directory, List<string> sample, HashSet<string> tops)
    {
        if (!fileSystem.Enumerate(directory, "*", TopLevel).Any(entry => tops.Contains(Path.GetFileName(entry.FullPath))))
        {
            return 0;
        }

        var score = 0;
        Dictionary<string, bool> folders = new(PackagePath.Comparer);
        foreach (var path in sample)
        {
            if (fileSystem.FileExists(PackagePath.ToFullPath(directory, path)))
            {
                score += 2;
            }
            else if (GameIndex.DirectoryOf(path) is { Length: > 0 } parent)
            {
                if (!folders.TryGetValue(parent, out var exists))
                {
                    folders[parent] = exists = fileSystem.DirectoryExists(PackagePath.ToFullPath(directory, parent));
                }

                score += exists ? 1 : 0;
            }
        }

        return score;
    }

    private static int Depth(string path) => path.Count(character => character == PackagePath.Separator);
}
