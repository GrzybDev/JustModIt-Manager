using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Projects;

/// <summary>
/// Opens the first creator's projects (format 2), keeping only the actions the author changed, since comparing again
/// finds the rest, and leaving out parts this version cannot use rather than refusing the whole project.
/// </summary>
internal static class PrototypeProject
{
    public const int FormatVersion = 2;

    /// <summary>Reads a project of format 2.</summary>
    /// <param name="root">The project file's root object.</param>
    public static CreatorProject Read(JsonElement root) => new()
    {
        Mode = string.Equals(Text(root, "mode"), "advanced", StringComparison.OrdinalIgnoreCase) ? CreatorMode.Advanced : CreatorMode.Simple,
        Game = Game(root),
        GameFolder = Text(root, "gameFolder") ?? string.Empty,
        AuthorFolder = Text(root, "modFolder") ?? string.Empty,
        Choices = new ValueDictionary<string, FileChoice>(Objects(root, "entries")
            .Select(entry => (Path: Text(entry, "path"), Choice: ChoiceOf(Text(entry, "difference"), Text(entry, "action"))))
            .Where(entry => entry.Path is not null && entry.Choice is not null)
            .DistinctBy(entry => entry.Path, PackagePath.Comparer)
            .ToImmutableDictionary(entry => entry.Path!, entry => entry.Choice!.Value, PackagePath.Comparer)),
        ExtraSteps = [.. Objects(root, "extraOperations").Select(Step).OfType<ProjectStep>()],
        Steps = [.. Objects(root, "steps").Select(Step).OfType<ProjectStep>()],
    };

    private static ProjectGame? Game(JsonElement root)
    {
        var target = Objects(root, "targets").FirstOrDefault();
        return target.ValueKind == JsonValueKind.Object && StoreId.TryParse(Text(target, "store"), out var store)
            && Text(target, "id") is { } id
                ? new ProjectGame(new GameKey(store, id), Text(root, "gameName") ?? id)
                : null;
    }

    // Null when the action was the one the comparison proposes anyway.
    private static FileChoice? ChoiceOf(string? difference, string? action) => (difference, action) switch
    {
        ("New", "Add") or ("Changed", "Patch") or ("Identical", "Skip") => null,
        (_, "Add" or "Replace") => FileChoice.Copy,
        (_, "Patch") => FileChoice.Patch,
        (_, "Move") => FileChoice.Rename,
        (_, "Skip") => FileChoice.Skip,
        _ => null,
    };

    private static ProjectStep? Step(JsonElement step) => Text(step, "type") switch
    {
        "copy" when Text(step, "source") is { } source && Text(step, "path") is { } path => new CopyFileStep(source, path),
        "patch" when Text(step, "source") is { } source && Text(step, "path") is { } path => new PatchFileStep(source, path),
        "rename" when Text(step, "from") is { } from && Text(step, "to") is { } to => new RenameFileStep(from, to),
        "delete" when Text(step, "path") is { } path => new DeleteFileStep(path),
        _ => null,
    };

    private static IEnumerable<JsonElement> Objects(JsonElement root, string name) =>
        root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
