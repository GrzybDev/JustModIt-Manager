using System.Runtime.Versioning;
using JustModIt.Manager.Core.Safety;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RestartManager;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Names the programs holding files open through the Restart Manager, which, unlike enumerating handles, works without
/// administrator rights; all files share one session, so a game holding hundreds of files costs one query.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestartManagerLockHolders : ILockHolderSource
{
    private const int MaxAttempts = 3;

    public static RestartManagerLockHolders Instance { get; } = new();

    public IReadOnlyList<string> FindHolders(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return [];
        }

        Span<char> key = stackalloc char[(int)PInvoke.CCH_RM_SESSION_KEY + 1];
        if (PInvoke.RmStartSession(out var session, key) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return [];
        }

        try
        {
            string[] files = [.. paths.Select(Path.GetFullPath)];
            return PInvoke.RmRegisterResources(session, files, [], []) == WIN32_ERROR.ERROR_SUCCESS
                ? [.. GetHolders(session).Select(Describe).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)]
                : [];
        }
        finally
        {
            _ = PInvoke.RmEndSession(session);
        }
    }

    private static List<(int ProcessId, string ApplicationName)> GetHolders(uint session)
    {
        uint capacity = 0;
        RM_PROCESS_INFO[] infos = [];
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var count = capacity;
            var result = PInvoke.RmGetList(session, out var needed, ref count, infos, out _);
            capacity = needed;
            if (result == WIN32_ERROR.ERROR_SUCCESS)
            {
                return [.. infos.Take((int)count).Select(info => ((int)info.Process.dwProcessId, info.strAppName.ToString()))];
            }

            // The list of holders can grow between the sizing call and the real one.
            if (result != WIN32_ERROR.ERROR_MORE_DATA)
            {
                return [];
            }

            infos = new RM_PROCESS_INFO[capacity];
        }

        return [];
    }

    // The Restart Manager's application name is often a window title; the executable name is what players recognise.
    private static string? Describe((int ProcessId, string ApplicationName) holder) =>
        WindowsProcessSource.TryGetImagePath(holder.ProcessId) is { } image
            ? Path.GetFileName(image)
            : string.IsNullOrWhiteSpace(holder.ApplicationName) ? null : holder.ApplicationName;
}
