namespace JustModIt.Manager.Core.IO;

/// <summary>Tells apart the Windows errors that an <see cref="IOException"/> carries in its HResult.</summary>
internal static class IOErrors
{
    private const int AccessDenied = 5;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private const int HandleDiskFull = 39;
    private const int DiskFull = 112;

    /// <summary>Whether another program has the file open, or a lock on part of it.</summary>
    /// <param name="exception">What a file operation threw.</param>
    public static bool IsSharingViolation(IOException exception) => Code(exception) is SharingViolation or LockViolation;

    /// <summary>Whether the system refused access, which some operations report as an <see cref="IOException"/>.</summary>
    /// <param name="exception">What a file operation threw.</param>
    public static bool IsAccessDenied(IOException exception) => Code(exception) == AccessDenied;

    /// <summary>Whether the disk is full.</summary>
    /// <param name="exception">What a file operation threw.</param>
    public static bool IsDiskFull(IOException exception) => Code(exception) is DiskFull or HandleDiskFull;

    private static int Code(IOException exception) => exception.HResult & 0xFFFF;
}
