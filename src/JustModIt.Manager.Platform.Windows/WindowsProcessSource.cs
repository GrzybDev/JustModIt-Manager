using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Safety;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace JustModIt.Manager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessSource : IProcessSource
{
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
    internal static string? TryGetImagePath(int processId)
    {
        // The limited right is granted for elevated and most protected processes, unlike PROCESS_QUERY_INFORMATION.
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false,
            (uint)processId);
        if (process.IsInvalid)
        {
            return null;
        }

        Span<char> small = stackalloc char[512];
        return TryQuery(process, small) ?? TryQuery(process, new char[PInvoke.UNICODE_STRING_MAX_CHARS]);
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

    private static string? TryQuery(SafeHandle process, Span<char> buffer)
    {
        var length = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref length)
            ? new string(buffer[..(int)length])
            : null;
    }

    private static int GetCurrentSessionId()
    {
        using var current = Process.GetCurrentProcess();
        return current.SessionId;
    }
}
