using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal static class FolderAccess
{
    /// <summary>
    /// Denies the current user creating files and folders in a folder, as a folder of another user would, or skips the test
    /// where the user is root, whom no permission stops.
    /// </summary>
    /// <param name="directory">The folder to deny.</param>
    /// <returns>Gives the access back when disposed.</returns>
    public static IDisposable DenyCreatingFiles(DirectoryInfo directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return DenyByAccessRule(directory);
        }

        Assert.SkipWhen(Environment.IsPrivilegedProcess, "Root writes into any folder.");
        return DenyByMode(directory);
    }

    [SupportedOSPlatform("windows")]
    private static Restore DenyByAccessRule(DirectoryInfo directory)
    {
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
        var security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
        return new Restore(() =>
        {
            var restored = directory.GetAccessControl();
            restored.RemoveAccessRule(rule);
            directory.SetAccessControl(restored);
        });
    }

    [UnsupportedOSPlatform("windows")]
    private static Restore DenyByMode(DirectoryInfo directory)
    {
        var mode = directory.UnixFileMode;
        directory.UnixFileMode = mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
        return new Restore(() => directory.UnixFileMode = mode);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
