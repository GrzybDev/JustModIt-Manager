using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace JustModIt.Manager.Core.Interop;

[SupportedOSPlatform("linux")]
internal static partial class UnixFileApi
{
    // O_RDONLY | O_CLOEXEC, the same on x64 and arm64, unlike O_DIRECTORY.
    private const int OpenToFlush = 0x80000;

    /// <summary>Flushes a folder's entries, such as files renamed into it, to the disk; a folder that cannot be opened is skipped.</summary>
    /// <param name="folder">The folder.</param>
    /// <exception cref="IOException">The disk reported an error.</exception>
    public static void FlushFolder(string folder)
    {
        // .NET opens no handle to a folder.
        using var handle = Open(folder, OpenToFlush);
        if (!handle.IsInvalid && Fsync(handle) != 0)
        {
            throw new IOException($"Flushing the folder '{folder}' failed.", Marshal.GetLastPInvokeError());
        }
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial SafeFileHandle Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(SafeFileHandle handle);
}
