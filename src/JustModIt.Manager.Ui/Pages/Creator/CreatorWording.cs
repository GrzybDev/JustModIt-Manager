using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;

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

    // The creator reports the game's own folder as an overlap too, but it is the likelier slip and gets words of its own.
    private static string Reason(CreatorBlock block, string gameFolder) =>
        block.Reason == CreatorBlockReason.FoldersOverlap && block.Path is { } path && PathComparison.AreSame(path, gameFolder)
            ? "GameFolder"
            : block.Reason.ToString();
}
