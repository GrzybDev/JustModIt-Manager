using Avalonia.Media;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Words the creator's typed results in the window's texts.</summary>
internal static class CreatorWording
{
    /// <summary>What keeps the creator from comparing, in a few words.</summary>
    /// <param name="block">The reason the creator refused.</param>
    /// <param name="gameFolder">The game's folder, which tells the game's own folder from one overlapping it.</param>
    public static string BlockTitle(CreatorBlock block, string gameFolder) =>
        Text.Format(Strings.Creator_BlockTitle, ("reason", Reason(block, gameFolder)));

    /// <summary>What keeps the creator from comparing, and what to do about it.</summary>
    /// <param name="block">The reason the creator refused.</param>
    /// <param name="gameFolder">The game's folder, which tells the game's own folder from one overlapping it.</param>
    public static string Block(CreatorBlock block, string gameFolder) =>
        Text.Format(Strings.Creator_Block, ("reason", Reason(block, gameFolder)), ("path", block.Path ?? ""), ("app", block.App ?? ""));

    /// <summary>An operation's kind as its chip says it.</summary>
    /// <param name="kind">The operation's kind.</param>
    public static (string Text, Geometry Icon, RowTone Tone) Kind(OperationKind kind) => kind switch
    {
        OperationKind.Copy => (Strings.Creator_KindCopy, Icons.Copy, RowTone.Neutral),
        OperationKind.Patch => (Strings.Creator_KindPatch, Icons.FileDiff, RowTone.Neutral),
        OperationKind.Rename => (Strings.Creator_KindRename, Icons.FilePen, RowTone.Neutral),
        _ => (Strings.Creator_KindDelete, Icons.FileX, RowTone.Danger),
    };

    /// <summary>How a pill names a choice for a file.</summary>
    /// <param name="choice">The choice.</param>
    /// <param name="difference">How the file differs from the game's.</param>
    public static string Choice(FileChoice choice, FileDifference difference) =>
        Text.Format(Strings.Creator_Choice, ("choice", choice.ToString()), ("difference", difference.ToString()));

    /// <summary>Why the package holds an operation, as the review says it.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="number">The number of the operation that renamed its original away; 0 for none.</param>
    public static string Why(PlannedOperation operation, int number) =>
        Text.Format(Strings.Creator_Why, ("reason", operation.Reason.ToString()), ("name", GameIndexName(operation.Path)),
            ("from", GameIndexName(operation.From ?? "")), ("number", number));

    /// <summary>A finding as a sentence beside what it is about.</summary>
    /// <param name="finding">The finding.</param>
    /// <param name="tool">The tool's name, for a third-party tool.</param>
    public static string Finding(PlanFinding finding, string tool = "")
    {
        var sentence = Text.Format(Strings.Creator_Finding, Arguments(finding, tool));
        return finding.SuggestedPath is { } suggested
            ? Text.Format(Strings.Creator_FindingSuggestion, ("finding", sentence), ("path", suggested))
            : sentence;
    }

    /// <summary>A finding of the hosting rules as the title of its row.</summary>
    /// <param name="finding">The finding.</param>
    /// <param name="tool">The tool's name, for a third-party tool.</param>
    public static string RuleTitle(PlanFinding finding, string tool = "") =>
        Text.Format(Strings.Creator_RuleTitle, Arguments(finding, tool));

    /// <summary>What the site's hosting rules ask about a finding, under its title.</summary>
    /// <param name="finding">The finding.</param>
    public static string RuleText(PlanFinding finding) =>
        Text.Format(Strings.Creator_RuleText, ("kind", finding.Kind.ToString()));

    /// <summary>The counts of a list of operations or steps, e.g. "8 operations · 1 rename · 6 copies".</summary>
    /// <param name="operations">The operations, in order.</param>
    /// <param name="advanced">Whether they are an advanced project's steps.</param>
    /// <param name="excluded">The files the author left out.</param>
    public static string Counts(IReadOnlyList<PlannedOperation> operations, bool advanced, int excluded) =>
        GameWording.Join(
        [
            Text.Format(advanced ? Strings.Creator_CountSteps : Strings.Creator_CountOperations, ("count", operations.Count)),
            Count(operations, OperationKind.Rename, Strings.Creator_CountRenames),
            Count(operations, OperationKind.Patch, Strings.Creator_CountPatches),
            Count(operations, OperationKind.Copy, Strings.Creator_CountCopies),
            Count(operations, OperationKind.Delete, Strings.Creator_CountDeletes),
            excluded > 0 ? Text.Format(Strings.Creator_CountExcluded, ("count", excluded)) : "",
        ]);

    /// <summary>Items as a list within a sentence, e.g. "2, 3 and 5".</summary>
    /// <param name="items">At least one, in the order the sentence names them.</param>
    public static string List(IReadOnlyList<string> items)
    {
        var list = items[0];
        for (var i = 1; i < items.Count; i++)
        {
            list = Text.Format(i == items.Count - 1 ? Strings.Creator_ListLast : Strings.Creator_ListNext, ("list", list), ("item", items[i]));
        }

        return list;
    }

    /// <summary>A game-relative path's folder with its separator, as the review shows it before the name.</summary>
    /// <param name="path">Game-relative, '/'-separated.</param>
    public static string FolderOf(string path) => path[..(path.LastIndexOf('/') + 1)];

    /// <summary>A game-relative path's file name.</summary>
    /// <param name="path">Game-relative, '/'-separated.</param>
    public static string GameIndexName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string Count(IReadOnlyList<PlannedOperation> operations, OperationKind kind, string pattern) =>
        operations.Count(operation => operation.Kind == kind) is > 0 and var count ? Text.Format(pattern, ("count", count)) : "";

    private static (string, object?)[] Arguments(PlanFinding finding, string tool) =>
    [
        ("kind", finding.Kind.ToString()),
        ("path", finding.Path ?? ""),
        ("related", finding.RelatedPath ?? ""),
        ("size", finding.Size is { } size ? Formats.FileSize(size) : ""),
        ("tool", tool),
        ("files", RuntimeFile.MaxPerPackage),
        ("notices", ThirdPartyNotice.MaxPerPackage),
    ];

    // The creator reports the game's own folder as an overlap too, but it is the likelier slip and gets words of its own.
    private static string Reason(CreatorBlock block, string gameFolder) =>
        block.Reason == CreatorBlockReason.FoldersOverlap && block.Path is { } path && PathComparison.AreSame(path, gameFolder)
            ? "GameFolder"
            : block.Reason.ToString();
}
