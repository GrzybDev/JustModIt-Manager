namespace JustModIt.Manager.Creator.Planning;

/// <summary>
/// Keys under which projects store what the author acknowledged, naming what the warning is about, so a warning about
/// something else, or about a file that changed, asks again.
/// </summary>
public static class AcknowledgementKeys
{
    /// <summary>The key of a warning; its parts are separated by ':', which paths never hold.</summary>
    /// <param name="kind">A warning that needs an acknowledgement.</param>
    /// <param name="parts">What the warning is about, e.g. its path and the file's size or hash.</param>
    internal static string For(FindingKind kind, params ReadOnlySpan<string> parts) =>
        string.Join(':', [IdOf(kind), .. parts]);

    /// <summary>The project path of the patch an accepted large patch's key names; null for a key of another warning.</summary>
    /// <param name="key">An acknowledged key, which holds the patch's path and its file's hash.</param>
    public static string? LargePatchPath(string key) =>
        key.Split(':') is [var id, var path, _] && id == IdOf(FindingKind.LargePatch) ? path : null;

    // Stored in project files, so the ids stay when the enum changes.
    private static string IdOf(FindingKind kind) => kind switch
    {
        FindingKind.RenamedFileNotReplaced => "renamed-file",
        FindingKind.WholeGameFile or FindingKind.WholeGameArchive => "whole-game-file",
        FindingKind.LargePatch => "large-patch",
        FindingKind.ThirdPartyTool => "third-party",
        FindingKind.AddedLibrary => "third-party-library",
        FindingKind.RuntimeCode => "runtime-code",
        FindingKind.RuntimeSettings => "runtime-settings",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The finding needs no acknowledgement."),
    };
}
