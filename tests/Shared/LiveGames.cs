using System.Text.Json;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Tests;

/// <summary>The games installed on this machine that the live tests use, named in a local file the repository leaves out.</summary>
/// <param name="TargetLanguage">A Steam game whose release installs and whose store lists the app's target language.</param>
/// <param name="AddsFiles">A Steam game whose release only adds files and whose store does not list the target language.</param>
/// <param name="ReplacesFiles">A Steam game whose release replaces one of the game's files.</param>
/// <param name="Announced">A Steam game whose project the site only announces, with no release yet.</param>
/// <param name="Creator">The package creator's case, a release with an add-on whose software changes game files.</param>
/// <param name="Epic">An Epic game that Heroic's Legendary installed into a Steam library's folder, if there is one.</param>
internal sealed record LiveGames(GameKey TargetLanguage, GameKey AddsFiles, GameKey ReplacesFiles, GameKey Announced,
    LiveCreatorCase Creator, LiveEpicGame? Epic)
{
    /// <summary>The file's name next to the tests, copied there from the tests' folder.</summary>
    public const string FileName = "live-games.json";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly Lazy<LiveGames?> Local = new(Read);

    /// <summary>The ids of the Steam games, each once.</summary>
    public IReadOnlyList<string> SteamGames =>
        [.. new[] { TargetLanguage, AddsFiles, ReplacesFiles, Announced, Creator.Game }.Select(key => key.StoreGameId).Distinct()];

    /// <summary>Reads the games, or skips the test when this machine names none.</summary>
    public static LiveGames Load()
    {
        var games = TryLoad();
        Assert.SkipWhen(games is null, $"Copy tests/live-games.example.json to tests/{FileName} and name games installed on this machine.");
        return games;
    }

    /// <summary>Reads the games; null when this machine names none.</summary>
    public static LiveGames? TryLoad() => Local.Value;

    private static LiveGames? Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path), Options);
        var root = document.RootElement;
        var steam = Child(root, "steam");
        var creator = Child(root, "creator");
        var epic = root.TryGetProperty("epic", out var found) && found.ValueKind == JsonValueKind.Object ? found : (JsonElement?)null;
        return new LiveGames(
            Steam(Text(steam, "targetLanguage")),
            Steam(Text(steam, "addsFiles")),
            Steam(Text(steam, "replacesFiles")),
            Steam(Text(steam, "announced")),
            new LiveCreatorCase(
                Steam(Text(creator, "game")),
                Text(creator, "renamedFile"),
                Text(creator, "renamedTo"),
                Text(creator, "rewrittenFile"),
                Text(creator, "createdFile"),
                [.. Child(creator, "addOnFolders").EnumerateArray().Select(folder => folder.GetString()!)]),
            epic is { } game ? new LiveEpicGame(new GameKey(StoreId.Epic, Text(game, "namespace")), Text(game, "appName")) : null);
    }

    private static GameKey Steam(string id) => new(StoreId.Steam, id);

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var child) ? child : throw new InvalidDataException($"{FileName} has no \"{name}\".");

    private static string Text(JsonElement parent, string name) =>
        Child(parent, name).GetString() is { Length: > 0 } text ? text : throw new InvalidDataException($"{FileName} has no \"{name}\".");
}

/// <summary>A release whose add-on puts a library in place of one of the game's and whose software rewrites a game file.</summary>
/// <param name="Game">The Steam game, installed as its store left it.</param>
/// <param name="RenamedFile">The game's file that the release renames away, as the package lays it out.</param>
/// <param name="RenamedTo">The name it is renamed to.</param>
/// <param name="RewrittenFile">The game's file that the add-on's software rewrites while the game runs.</param>
/// <param name="CreatedFile">The file the software creates beside it, keeping the file as the store ships it.</param>
/// <param name="AddOnFolders">The folders whose files make the add-on, each ending with a slash; the rest makes the base.</param>
internal sealed record LiveCreatorCase(GameKey Game, string RenamedFile, string RenamedTo, string RewrittenFile, string CreatedFile,
    IReadOnlyList<string> AddOnFolders);

/// <summary>An Epic game the live tests look for.</summary>
/// <param name="Key">The game, by its catalog namespace.</param>
/// <param name="AppName">The app name that Legendary and Heroic's link use.</param>
internal sealed record LiveEpicGame(GameKey Key, string AppName);
