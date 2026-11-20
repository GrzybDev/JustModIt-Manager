namespace JustModIt.Manager.Core.Tests.TestSupport;

// The errors a full disk or a held file give can only be built, with the code the running system's .NET puts in them.
internal static class SystemErrors
{
    public static IOException IOException(string message, int windowsError, int linuxErrno) =>
        new(message, OperatingSystem.IsWindows() ? unchecked((int)0x80070000) | windowsError : linuxErrno);
}
