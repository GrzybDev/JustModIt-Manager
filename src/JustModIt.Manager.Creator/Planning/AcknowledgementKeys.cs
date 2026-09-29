namespace JustModIt.Manager.Creator.Planning;

/// <summary>
/// Keys under which projects store what the author acknowledged, naming what the warning is about, so a warning about
/// something else, or about a file that changed, asks again.
/// </summary>
internal static class AcknowledgementKeys
{
    /// <summary>The key of a warning; its parts are separated by ':', which paths never hold.</summary>
    /// <param name="kind">A warning that needs an acknowledgement.</param>
    /// <param name="parts">What the warning is about, e.g. its path and the file's size or hash.</param>
    public static string For(FindingKind kind, params ReadOnlySpan<string> parts) =>
        string.Join(':', [IdOf(kind), .. parts]);

    // Stored in project files, so the ids stay when the enum changes.
    private static string IdOf(FindingKind kind) => kind switch
    {
        FindingKind.RenamedFileNotReplaced => "renamed-file",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The finding needs no acknowledgement."),
    };
}
