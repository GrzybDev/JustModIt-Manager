using System.Text.Json;
using System.Text.RegularExpressions;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>One <c>goggame-&lt;id&gt;.info</c> file: GOG's own description of a game or DLC in its folder.</summary>
/// <param name="GameId">The product id the file is named after.</param>
/// <param name="RootGameId">The id of the game it belongs to, its own for a game; null in old files.</param>
/// <param name="Name">The product's name.</param>
/// <param name="BuildId">The installed build, from the file or its <c>.id</c> companion.</param>
/// <param name="HasPlayTasks">The file names ways to start the product, which some DLC files do not.</param>
/// <param name="LastWriteTime">When GOG last wrote the file.</param>
/// <param name="Languages">The installed languages as BCP 47 tags.</param>
/// <param name="PlayTask">The primary play task that starts a program; null when there is none.</param>
internal sealed record GogGameInfo(
    string GameId,
    string? RootGameId,
    string? Name,
    string? BuildId,
    bool HasPlayTasks,
    DateTimeOffset LastWriteTime,
    ValueList<string> Languages,
    LaunchProgram? PlayTask)
{
    public bool IsDlc => RootGameId is not null && RootGameId != GameId;
}

/// <summary>
/// All <c>.info</c> files of one install folder: a game's folder holds one for the game and one per installed DLC.
/// </summary>
/// <param name="Base">The game's own file; null when the folder holds only DLC or none.</param>
/// <param name="Entries">Every valid file of the folder, one per product.</param>
internal sealed record GogInfoSet(GogGameInfo? Base, IReadOnlyList<GogGameInfo> Entries)
{
    public static GogInfoSet Empty { get; } = new(null, []);

    /// <summary>Gives the file of <paramref name="gameId"/>; null when the folder has none.</summary>
    /// <param name="gameId">A GOG product id.</param>
    public GogGameInfo? Find(string gameId) => Entries.FirstOrDefault(entry => entry.GameId == gameId);

    /// <summary>Builds the set, picking the game's own file among <paramref name="entries"/>.</summary>
    /// <param name="entries">The valid files of one folder, one per product.</param>
    internal static GogInfoSet From(IReadOnlyList<GogGameInfo> entries)
    {
        // Old files omit rootGameId, and some DLC files have no play tasks, so a launchable root is preferred over a
        // root that merely names itself.
        var root = entries.FirstOrDefault(entry => !entry.IsDlc && entry.HasPlayTasks)
            ?? entries.FirstOrDefault(entry => entry.RootGameId == entry.GameId);
        return new GogInfoSet(root, entries);
    }
}

/// <summary>Reads the <c>goggame-&lt;id&gt;.info</c> files of install folders.</summary>
/// <param name="fileSystem">Reads the files.</param>
internal sealed partial class GogInfoReader(IFileSystem fileSystem)
{
    public const string InfoPattern = "goggame-*.info";

    /// <summary>Reads the <c>.info</c> files of a folder; damaged or mismatched files are left out.</summary>
    /// <param name="directory">The install folder, or a macOS app bundle.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<GogInfoSet> ReadAsync(string directory, CancellationToken cancellationToken)
    {
        var entries = new List<GogGameInfo>();
        foreach (var folder in CandidateFolders(directory))
        {
            foreach (var file in ListInfoFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await TryReadAsync(file, cancellationToken).ConfigureAwait(false) is { } info
                    && entries.All(entry => entry.GameId != info.GameId))
                {
                    entries.Add(info);
                }
            }
        }

        return GogInfoSet.From(entries);
    }

    // On macOS the files live inside the app bundle, and launchers record the bundle itself as the install folder.
    private static string[] CandidateFolders(string directory) => [directory, Path.Join(directory, "Contents", "Resources")];

    private List<FileEntry> ListInfoFiles(string folder) =>
        [.. fileSystem.TryList(folder, InfoPattern, FileListing.AnyCase).Where(file => !file.IsDirectory)
            .OrderBy(file => file.Name, StringComparer.Ordinal)];

    private async Task<GogGameInfo?> TryReadAsync(FileEntry file, CancellationToken cancellationToken)
    {
        var match = InfoFileName().Match(file.Name);
        if (!match.Success)
        {
            return null;
        }

        var gameId = match.Groups[1].Value;
        var info = await TryParseAsync(file.FullPath, LauncherJsonContext.Default.GogInfoFile, cancellationToken).ConfigureAwait(false);

        // A copied or renamed file would otherwise lend its content to another product.
        if (info is null || info.GameId != gameId)
        {
            return null;
        }

        var buildId = LauncherJson.NullIfBlank(info.BuildId)
            ?? LauncherJson.NullIfBlank((await TryParseAsync(Path.ChangeExtension(file.FullPath, ".id"),
                LauncherJsonContext.Default.GogIdFile, cancellationToken).ConfigureAwait(false))?.BuildId);
        List<string> languages = [.. (info.Languages ?? []).Select(LanguageNames.FromTag).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        if (languages.Count == 0 && LanguageNames.FromName(info.Language) is { } language)
        {
            languages.Add(language);
        }

        return new GogGameInfo(gameId, LauncherJson.NullIfBlank(info.RootGameId), LauncherJson.NullIfBlank(info.Name), buildId,
            info.PlayTasks is { Count: > 0 }, file.LastWriteTimeUtc, [.. languages], PrimaryTask(info.PlayTasks));
    }

    // A primary link task opens a web page rather than the game.
    private static LaunchProgram? PrimaryTask(IReadOnlyList<JsonElement>? tasks) =>
        tasks?.FirstOrDefault(task => task.ValueKind == JsonValueKind.Object && task.GetBooleanOrNull("isPrimary") == true) is
            { ValueKind: JsonValueKind.Object } primary
        && primary.GetTextOrNull("type") is null or "FileTask"
            ? LaunchProgram.TryCreate(primary.GetTextOrNull("path"), primary.GetTextOrNull("arguments"), primary.GetTextOrNull("workingDir"))
            : null;

    private async Task<T?> TryParseAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await LauncherJson.ReadAsync(fileSystem, path, typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (LauncherJson.IsUnreadable(exception))
        {
            return null;
        }
    }

    [GeneratedRegex("^goggame-([0-9]{1,20})\\.info$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InfoFileName();
}
