namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// Follows Steam's process from the one Steam recorded of itself, which stays recorded when Steam was stopped from outside,
/// and reports its exit. One caller at a time follows it.
/// </summary>
/// <param name="steam">Tells whether a process is Steam's and when it exits.</param>
/// <param name="time">Dates when the process was first seen.</param>
/// <param name="exited">Called, on any thread, once the followed process exits.</param>
internal sealed class SteamProcess(ISteamClient steam, TimeProvider time, Action exited)
{
    private int? _process;
    private int _exitedProcess;
    private int _staleProcess;

    /// <summary>The process followed now; null while Steam does not run.</summary>
    public int? Id => _process;

    /// <summary>When the followed process started and was first seen; null before any.</summary>
    public SteamRun? Run { get; private set; }

    /// <summary>Follows the process Steam recorded now.</summary>
    /// <param name="processId">The process Steam recorded; null when it recorded none.</param>
    /// <param name="stopping">Stops waiting for the process's exit.</param>
    /// <returns>Whether Steam runs.</returns>
    public bool Follow(int? processId, CancellationToken stopping)
    {
        if (processId is not { } id || id == Volatile.Read(ref _exitedProcess) || id == _staleProcess)
        {
            _process = null;
            return false;
        }

        if (id == _process)
        {
            return true;
        }

        // Ids are reused, so a recorded process may be another program's by now.
        if (!steam.IsRunning(id))
        {
            _staleProcess = id;
            _process = null;
            return false;
        }

        _process = id;
        Run = new SteamRun(steam.StartedAt(id), time.GetUtcNow());
        _ = WaitForExitAsync(id, stopping);
        return true;
    }

    /// <summary>Takes a process for gone, once someone else saw it exit.</summary>
    /// <param name="processId">The process that exited.</param>
    public void Exited(int processId) => Volatile.Write(ref _exitedProcess, processId);

    private async Task WaitForExitAsync(int processId, CancellationToken stopping)
    {
        try
        {
            if (await steam.WaitForExitAsync(processId, stopping).ConfigureAwait(false))
            {
                Exited(processId);
                exited();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
