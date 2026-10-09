using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal static class FolderAccess
{
    /// <summary>Denies the current user creating files and folders in a folder, as a folder of another user would.</summary>
    /// <param name="directory">The folder to deny.</param>
    /// <returns>Gives the access back when disposed.</returns>
    [SupportedOSPlatform("windows")]
    public static IDisposable DenyCreatingFiles(DirectoryInfo directory)
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

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
