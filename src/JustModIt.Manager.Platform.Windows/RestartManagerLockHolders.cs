using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Names the programs holding files open through the Restart Manager, which, unlike enumerating handles, works without
/// administrator rights; all files share one session, so a game holding hundreds of files costs one query.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class RestartManagerLockHolders : ILockHolderSource
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int SessionKeyLength = 32;
    private const int MaxAppNameLength = 255;
    private const int MaxServiceNameLength = 63;
    private const int MaxAttempts = 3;

    public static RestartManagerLockHolders Instance { get; } = new();

    public unsafe IReadOnlyList<string> FindHolders(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return [];
        }

        var key = stackalloc char[SessionKeyLength + 1];
        if (RmStartSession(out var session, 0, key) != ErrorSuccess)
        {
            return [];
        }

        try
        {
            string[] files = [.. paths.Select(Path.GetFullPath)];
            return RmRegisterResources(session, (uint)files.Length, files, 0, 0, 0, 0) == ErrorSuccess
                ? [.. GetHolders(session).Select(Describe).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)]
                : [];
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    private static unsafe List<(int ProcessId, string ApplicationName)> GetHolders(uint session)
    {
        uint capacity = 0;
        RmProcessInfo[] infos = [];
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            int result;
            var count = capacity;
            fixed (RmProcessInfo* buffer = infos)
            {
                result = RmGetList(session, out var needed, ref count, buffer, out _);
                capacity = needed;
            }

            if (result == ErrorSuccess)
            {
                return [.. infos.Take((int)count).Select(info => ((int)info.Process.ProcessId, new string(info.AppName)))];
            }

            // The list of holders can grow between the sizing call and the real one.
            if (result != ErrorMoreData)
            {
                return [];
            }

            infos = new RmProcessInfo[capacity];
        }

        return [];
    }

    // The Restart Manager's application name is often a window title; the executable name is what players recognise.
    private static string? Describe((int ProcessId, string ApplicationName) holder) =>
        WindowsProcessSource.TryGetImagePath(holder.ProcessId) is { } image
            ? Path.GetFileName(image)
            : string.IsNullOrWhiteSpace(holder.ApplicationName) ? null : holder.ApplicationName;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public uint ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        public fixed char AppName[MaxAppNameLength + 1];
        public fixed char ServiceShortName[MaxServiceNameLength + 1];
        public int ApplicationType;
        public uint AppStatus;
        public uint TerminalServicesSessionId;
        public int Restartable;
    }

    [LibraryImport("rstrtmgr.dll")]
    private static unsafe partial int RmStartSession(out uint sessionHandle, uint sessionFlags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames,
        uint applicationCount, nint applications, uint serviceCount, nint serviceNames);

    [LibraryImport("rstrtmgr.dll")]
    private static unsafe partial int RmGetList(uint sessionHandle, out uint processInfoNeeded, ref uint processInfoCount,
        RmProcessInfo* affectedApps, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmEndSession(uint sessionHandle);
}
