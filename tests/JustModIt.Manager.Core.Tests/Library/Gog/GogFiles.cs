using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

internal static class GogFiles
{
    public static readonly DateTimeOffset WrittenAt = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Writes <c>goggame-&lt;id&gt;.info</c> as GOG Galaxy does and returns its path.</summary>
    /// <param name="files">The file system to write to.</param>
    /// <param name="folder">The game folder.</param>
    /// <param name="gameId">The id the file is named after and holds.</param>
    /// <param name="name">The game's name.</param>
    /// <param name="rootGameId">The base game's id for DLC; the game's own id when null.</param>
    /// <param name="buildId">The build id; left out when null.</param>
    /// <param name="playTasks">Whether the file names a primary play task, as a base game's does.</param>
    /// <param name="languages">The items of the languages list, written as given (quoted JSON strings).</param>
    /// <param name="writtenAt">When the file was written; <see cref="WrittenAt"/> when null.</param>
    public static string AddInfo(this InMemoryFileSystem files, string folder, string gameId, string name, string? rootGameId = null,
        string? buildId = "100", bool playTasks = true, string languages = "\"en-US\"", DateTimeOffset? writtenAt = null)
    {
        var tasks = playTasks
            ? """[ { "category": "game", "isPrimary": true, "type": "FileTask", "path": "game.exe" } ]"""
            : "[]";
        var json = $$"""
            {
                "version": 1,
                "gameId": "{{gameId}}",
                "rootGameId": "{{rootGameId ?? gameId}}",
                {{(buildId is null ? "" : $"\"buildId\": \"{buildId}\",")}}
                "name": "{{name}}",
                "language": "English",
                "languages": [ {{languages}} ],
                "playTasks": {{tasks}}
            }
            """;
        return files.AddFile(Path.Join(folder, $"goggame-{gameId}.info"), json, writtenAt ?? WrittenAt);
    }
}
