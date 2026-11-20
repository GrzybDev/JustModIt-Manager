namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Tells apart the system errors that an <see cref="IOException"/> carries in its HResult: a Windows error code on
/// Windows, Linux's errno elsewhere.
/// </summary>
internal static class IOErrors
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorWriteProtect = 19;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;

    // .NET locks every file it opens with flock, so another .NET program's exclusive open makes it EWOULDBLOCK.
    private const int WouldBlock = 11;
    private const int TextFileBusy = 26;
    private const int NoSpace = 28;
    private const int ReadOnlyFileSystem = 30;
    private const int QuotaExceeded = 122;

    /// <summary>Whether another program has the file open, or a lock on part of it.</summary>
    /// <param name="exception">What a file operation threw.</param>
    public static bool IsSharingViolation(IOException exception) => OperatingSystem.IsWindows()
        ? WindowsCode(exception) is ErrorSharingViolation or ErrorLockViolation
        : exception.HResult is WouldBlock or TextFileBusy;

    /// <summary>Whether the system refused access, which some operations report as an <see cref="IOException"/>.</summary>
    /// <param name="exception">What a file operation threw.</param>
    public static bool IsAccessDenied(IOException exception) => OperatingSystem.IsWindows()
        ? WindowsCode(exception) is ErrorAccessDenied or ErrorWriteProtect
        : exception.HResult == ReadOnlyFileSystem;

    /// <summary>Whether the disk, or the user's share of it, is full.</summary>
    /// <param name="exception">What a file operation threw.</param>
    public static bool IsDiskFull(IOException exception) => OperatingSystem.IsWindows()
        ? WindowsCode(exception) is ErrorDiskFull or ErrorHandleDiskFull
        : exception.HResult is NoSpace or QuotaExceeded;

    private static int WindowsCode(IOException exception) => exception.HResult & 0xFFFF;
}
