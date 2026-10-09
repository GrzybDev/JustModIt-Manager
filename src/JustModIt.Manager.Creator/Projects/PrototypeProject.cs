using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
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
        Mode = string.Equals(root.GetTextOrNull("mode"), "advanced", StringComparison.OrdinalIgnoreCase)
            ? CreatorMode.Advanced
            : CreatorMode.Simple,
        Game = Game(root),
        GameFolder = root.GetTextOrNull("gameFolder") ?? string.Empty,
        AuthorFolder = root.GetTextOrNull("modFolder") ?? string.Empty,
        Choices = new ValueDictionary<string, FileChoice>(Objects(root, "entries")
            .Select(entry => (Path: entry.GetTextOrNull("path"),
                Choice: ChoiceOf(entry.GetTextOrNull("difference"), entry.GetTextOrNull("action"))))
            .Where(entry => entry.Path is not null && entry.Choice is not null)
            .DistinctBy(entry => entry.Path, PackagePath.Comparer)
            .ToImmutableDictionary(entry => entry.Path!, entry => entry.Choice!.Value, PackagePath.Comparer)),
        ExtraSteps = [.. Objects(root, "extraOperations").Select(Step).OfType<ProjectStep>()],
        Steps = [.. Objects(root, "steps").Select(Step).OfType<ProjectStep>()],
    };

    private static ProjectGame? Game(JsonElement root)
    {
        var target = Objects(root, "targets").FirstOrDefault();
        return target.ValueKind == JsonValueKind.Object && StoreId.TryParse(target.GetTextOrNull("store"), out var store)
            && target.GetTextOrNull("id") is { } id
                ? new ProjectGame(new GameKey(store, id), root.GetTextOrNull("gameName") ?? id)
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

    private static ProjectStep? Step(JsonElement step) => step.GetTextOrNull("type") switch
    {
        "copy" when step.GetTextOrNull("source") is { } source && step.GetTextOrNull("path") is { } path =>
            new CopyFileStep(source, path),
        "patch" when step.GetTextOrNull("source") is { } source && step.GetTextOrNull("path") is { } path =>
            new PatchFileStep(source, path),
        "rename" when step.GetTextOrNull("from") is { } from && step.GetTextOrNull("to") is { } to => new RenameFileStep(from, to),
        "delete" when step.GetTextOrNull("path") is { } path => new DeleteFileStep(path),
        _ => null,
    };

    private static IEnumerable<JsonElement> Objects(JsonElement root, string name) =>
        root.GetOrNull(name, JsonValueKind.Array) is { } items
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];
}
