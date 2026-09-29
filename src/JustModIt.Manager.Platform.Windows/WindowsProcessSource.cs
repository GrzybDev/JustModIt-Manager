using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Safety;
using Microsoft.Win32.SafeHandles;

namespace JustModIt.Manager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessSource : IProcessSource
{
    // The limited right is granted for elevated and most protected processes, unlike PROCESS_QUERY_INFORMATION.
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaxLongPath = 32767;

    public static WindowsProcessSource Instance { get; } = new();

    public int CurrentSessionId { get; } = GetCurrentSessionId();

    public IReadOnlyList<ProcessEntry> Snapshot()
    {
        var processes = Process.GetProcesses();
        var entries = new List<ProcessEntry>(processes.Length);
        foreach (var process in processes)
        {
            using (process)
            {
                if (TryDescribe(process) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    public async Task<bool> WaitForExitAsync(int processId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true;
        }

        using (process)
        {
            try
            {
                if (!process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            // Waiting needs a handle that other users' and protected processes do not give out.
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>The full path of a process's executable; null when it cannot be read.</summary>
    /// <param name="processId">The process; elevated and most protected ones are read too.</param>
    internal static unsafe string? TryGetImagePath(int processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        if (process.IsInvalid)
        {
            return null;
        }

        Span<char> small = stackalloc char[512];
        return TryQuery(process, small) ?? TryQuery(process, new char[MaxLongPath]);
    }

    private static ProcessEntry? TryDescribe(Process process)
    {
        try
        {
            return new ProcessEntry(process.Id, process.ProcessName, process.SessionId, TryGetImagePath(process.Id));
        }
        // The process exited while the snapshot was being described.
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static unsafe string? TryQuery(SafeProcessHandle process, Span<char> buffer)
    {
        var length = (uint)buffer.Length;
        fixed (char* chars = buffer)
        {
            if (!QueryFullProcessImageName(process, 0, chars, ref length))
            {
                return null;
            }
        }

        return new string(buffer[..(int)length]);
    }

    private static int GetCurrentSessionId()
    {
        using var current = Process.GetCurrentProcess();
        return current.SessionId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* exeName, ref uint size);
}
