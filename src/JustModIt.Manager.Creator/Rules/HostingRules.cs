using System.Globalization;
using System.IO.Enumeration;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Creator.Rules;

/// <summary>
/// Checks a plan against what the site hosts: the team's own work, never game files, store libraries or ways around
/// copy protection, so game files change only through patches, renames and deletions, which carry none of their content.
/// </summary>
internal static class HostingRules
{
    private const string ExecutableExtension = ".exe";

    // A plugin of the ASI loaders, which load it as a library.
    private const string LoaderPluginExtension = ".asi";

    /// <summary>What the rules find in a plan's operations.</summary>
    /// <param name="comparison">The comparison the plan was made from.</param>
    /// <param name="operations">The resolved operations, in order.</param>
    /// <param name="rules">The rules to apply.</param>
    public static IEnumerable<PlanFinding> Check(FolderComparison comparison, IReadOnlyList<PlannedOperation> operations,
        HostingRuleSet rules)
    {
        var tools = new Dictionary<string, PlanFinding>(StringComparer.Ordinal);
        for (var i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            foreach (var finding in CheckOperation(comparison, operation, rules))
            {
                yield return finding with { OperationIndex = i, StepIndex = operation.StepIndex };
            }

            if (operation.Kind == OperationKind.Copy && ToolOf(operation.Path, rules) is { } tool && !tools.ContainsKey(tool.Id))
            {
                tools[tool.Id] = new PlanFinding(FindingKind.ThirdPartyTool, operation.Path)
                {
                    ToolId = tool.Id,
                    OperationIndex = i,
                    StepIndex = operation.StepIndex,
                    AcknowledgementKey = AcknowledgementKeys.For(FindingKind.ThirdPartyTool, tool.Id),
                };
            }
        }

        foreach (var finding in tools.Values)
        {
            yield return finding;
        }
    }

    /// <summary>What the rules find in the files the package's software writes while the game runs.</summary>
    /// <param name="files">The declared files, in the plan's order.</param>
    /// <param name="operations">The resolved operations, which may write some of the files too.</param>
    /// <param name="rules">The rules to apply.</param>
    public static IEnumerable<PlanFinding> CheckRuntime(IReadOnlyList<PlannedRuntimeFile> files, IReadOnlyList<PlannedOperation> operations,
        HostingRuleSet rules)
    {
        for (var i = 0; i < files.Count; i++)
        {
            if (!PackagePath.IsValid(files[i].Path))
            {
                continue;
            }

            foreach (var finding in CheckRuntimeFile(files[i], operations, rules))
            {
                yield return finding with { RuntimeIndex = i };
            }
        }
    }

    /// <summary>Whether the package carries a program, the only thing that can change files while the game runs.</summary>
    /// <param name="operations">The resolved operations.</param>
    /// <param name="rules">Name the extensions of libraries.</param>
    public static bool CarriesCode(IReadOnlyList<PlannedOperation> operations, HostingRuleSet rules) =>
        operations.Any(operation => operation.Kind is OperationKind.Copy or OperationKind.Rename && IsCode(operation.Path, rules));

    /// <summary>Whether a patch carries most of its file, as one of an encrypted or recompressed archive does.</summary>
    /// <param name="rules">Hold the thresholds.</param>
    /// <param name="patchSize">In bytes.</param>
    /// <param name="targetSize">The patched file's size, in bytes.</param>
    public static bool IsLargePatch(HostingRuleSet rules, long patchSize, long targetSize) =>
        targetSize >= rules.LargePatchMinimumBytes && patchSize > targetSize * rules.LargePatchRatio;

    private static IEnumerable<PlanFinding> CheckOperation(FolderComparison comparison, PlannedOperation operation,
        HostingRuleSet rules)
    {
        if (new[] { operation.From, operation.Path }.OfType<string>()
                .FirstOrDefault(path => Matches(rules.StoreLibraries, GameIndex.NameOf(path))) is { } library)
        {
            yield return new PlanFinding(FindingKind.StoreLibrary, library);
        }

        if (operation.Kind is OperationKind.Copy or OperationKind.Rename && IsCircumvention(operation.Path, rules))
        {
            yield return new PlanFinding(FindingKind.CircumventionFile, operation.Path);
        }

        if (operation.Kind != OperationKind.Copy)
        {
            yield break;
        }

        var game = comparison.Game.TryGetFile(operation.Path, out var found) ? found : null;
        var size = operation.SourceFile?.Stamp.Size ?? operation.Replaces?.Stamp.Size ?? 0;
        if (game is not null && game.Path.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase))
        {
            yield return new PlanFinding(FindingKind.WholeGameExecutable, operation.Path);
        }
        else if (operation.Source is { } source && comparison.FindAuthorFile(source)?.CopyOf is { } copyOf)
        {
            yield return new PlanFinding(FindingKind.CopyOfGameFile, operation.Path) { RelatedPath = copyOf };
        }
        else if (operation.Replaces is { } replaced)
        {
            yield return WholeGameFile(IsArchive(replaced.Path, replaced.Stamp.Size, rules), operation.Path, size);
        }
        // With its original renamed away, the file is the author's own, like a proxy library, unless it is an archive or
        // a large file: then most likely the game's, changed, with the original kept under another name by the author.
        else if (game is not null && IsArchive(game.Path, size, rules))
        {
            yield return WholeGameFile(isArchive: true, operation.Path, size);
        }
        else if (IsUnknownLibrary(operation.Path, rules))
        {
            yield return new PlanFinding(FindingKind.AddedLibrary, operation.Path);
        }
    }

    private static IEnumerable<PlanFinding> CheckRuntimeFile(PlannedRuntimeFile file, IReadOnlyList<PlannedOperation> operations,
        HostingRuleSet rules)
    {
        var name = GameIndex.NameOf(file.Path);
        if (Matches(rules.StoreLibraries, name))
        {
            yield return new PlanFinding(FindingKind.StoreLibrary, file.Path);
        }

        if (IsCircumvention(file.Path, rules))
        {
            yield return new PlanFinding(FindingKind.CircumventionFile, file.Path);
        }

        if (Matches(rules.SaveNames, name) || FoldersOf(file.Path).Any(folder => Matches(rules.SaveNames, folder)))
        {
            yield return new PlanFinding(FindingKind.RuntimeSave, file.Path);
        }
        else if (IsCode(file.Path, rules))
        {
            yield return new PlanFinding(FindingKind.RuntimeCode, file.Path)
            {
                AcknowledgementKey = AcknowledgementKeys.For(FindingKind.RuntimeCode, file.Path),
            };
        }
        // A settings file the package writes is the package's own, and goes back as the package left it anyway.
        else if (file.GameFile is { } game && rules.SettingsExtensions.Contains(Path.GetExtension(file.Path), StringComparer.OrdinalIgnoreCase)
            && !operations.Any(operation => operation.Kind is OperationKind.Copy or OperationKind.Patch or OperationKind.Rename
                && PackagePath.Comparer.Equals(operation.Path, file.Path)))
        {
            yield return new PlanFinding(FindingKind.RuntimeSettings, file.Path)
            {
                Size = game.Stamp.Size,
                AcknowledgementKey = AcknowledgementKeys.For(FindingKind.RuntimeSettings, file.Path,
                    game.Stamp.Size.ToString(CultureInfo.InvariantCulture)),
            };
        }
    }

    private static bool IsCode(string path, HostingRuleSet rules)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(ExecutableExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(LoaderPluginExtension, StringComparison.OrdinalIgnoreCase)
            || rules.LibraryExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static PlanFinding WholeGameFile(bool isArchive, string path, long size)
    {
        var kind = isArchive ? FindingKind.WholeGameArchive : FindingKind.WholeGameFile;
        return new PlanFinding(kind, path)
        {
            Size = size,
            AcknowledgementKey = AcknowledgementKeys.For(kind, path, size.ToString(CultureInfo.InvariantCulture)),
        };
    }

    private static bool IsCircumvention(string path, HostingRuleSet rules) =>
        Matches(rules.CircumventionFiles, GameIndex.NameOf(path))
        || FoldersOf(path).Any(folder => Matches(rules.CircumventionFolders, folder));

    private static bool IsUnknownLibrary(string path, HostingRuleSet rules) =>
        PackagePath.IsValid(path) && rules.LibraryExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && !Matches(rules.StoreLibraries, GameIndex.NameOf(path)) && !IsCircumvention(path, rules) && ToolOf(path, rules) is null;

    private static bool IsArchive(string path, long size, HostingRuleSet rules) =>
        size >= rules.LargeFileBytes || rules.ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ThirdPartyTool? ToolOf(string path, HostingRuleSet rules)
    {
        var folders = FoldersOf(path);
        return rules.ThirdPartyTools.FirstOrDefault(tool =>
            folders.Any(folder => tool.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            || Matches(tool.Files, GameIndex.NameOf(path)));
    }

    private static bool Matches(IEnumerable<string> patterns, string name) =>
        patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));

    private static string[] FoldersOf(string path) => path.Split(PackagePath.Separator)[..^1];
}
