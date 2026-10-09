namespace JustModIt.Manager.Core.Installation;

internal enum ChangeKind
{
    /// <summary>The journal is about to be made durable.</summary>
    Commit,

    CreateDirectory,

    /// <summary>An original is about to be moved into the backup folder.</summary>
    Backup,

    /// <summary>An original copied to a backup folder on another volume is about to be read back.</summary>
    ReadBack,

    /// <summary>New content is about to be renamed into place.</summary>
    Write,

    Delete,
    Restore,

    /// <summary>A record is about to be written or removed, which commits an operation.</summary>
    Record,

    /// <summary>A backup is about to be moved or copied to another backup folder.</summary>
    Relocate,

    /// <summary>A committed operation is about to remove what it no longer needs.</summary>
    Tidy,
}

/// <summary>Called right before every change to game files, backups and records, so tests can stop the world at each one.</summary>
/// <param name="before">What to call; null outside tests.</param>
internal sealed class ChangeHook(Action<ChangeKind, string?>? before)
{
    public static ChangeHook None { get; } = new(null);

    /// <summary>Announces a change about to happen.</summary>
    /// <param name="kind">What is about to change.</param>
    /// <param name="path">The file or folder that changes, if there is one.</param>
    public void Before(ChangeKind kind, string? path = null) => before?.Invoke(kind, path);
}
