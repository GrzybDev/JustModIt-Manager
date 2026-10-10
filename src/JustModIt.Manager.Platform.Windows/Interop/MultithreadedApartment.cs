using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace JustModIt.Manager.Platform.Windows.Interop;

/// <summary>
/// The process's multithreaded COM apartment, kept for the process's whole life once asked for: thread pool threads join
/// no apartment of their own, so they use the system's COM objects in this one, and the collector, which releases those
/// objects, runs on a thread that joined none either.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MultithreadedApartment
{
    private static readonly Lock Gate = new();
    private static CoDecrementMTAUsageSafeHandle? _usage;

    /// <summary>Keeps the apartment from now on; calling it again does nothing.</summary>
    /// <returns>A failure when the system would not keep it.</returns>
    public static HRESULT Keep()
    {
        lock (Gate)
        {
            if (_usage is not null)
            {
                return HRESULT.S_OK;
            }

            var result = PInvoke.CoIncrementMTAUsage(out var usage);
            if (result.Succeeded)
            {
                _usage = usage;
            }

            return result;
        }
    }
}
