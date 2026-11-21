namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// Follows Steam's process from the one Steam recorded of itself, which stays recorded when Steam was stopped from outside,
/// and reports its exit. The services that look at Steam share it, so each process's exit is awaited once.
/// </summary>
/// <param name="steam">Tells whether a process is Steam's and when it exits.</param>
/// <param name="time">Dates when the process was first seen.</param>
internal sealed class SteamProcess(ISteamClient steam, TimeProvider time)
{
    private readonly Lock _gate = new();
    private int? _process;
    private int? _awaited;
    private int _exitedProcess;
    private int _staleProcess;
    private SteamRun? _run;

    /// <summary>Raised, on any thread, once the followed process exits.</summary>
    public event EventHandler? Exited;

    /// <summary>The process the latest look found; null while Steam does not run.</summary>
    public int? Id
    {
        get
        {
            lock (_gate)
            {
                return _process;
            }
        }
    }

    /// <summary>When the followed process started and was first seen; null before any.</summary>
    public SteamRun? Run
    {
        get
        {
            lock (_gate)
            {
                return _run;
            }
        }
    }

    /// <summary>Follows the process Steam recorded now.</summary>
    /// <param name="processId">The process Steam recorded; null when it recorded none.</param>
    /// <param name="stopping">Stops waiting for the process's exit.</param>
    /// <returns>Whether Steam runs.</returns>
    public bool Follow(int? processId, CancellationToken stopping)
    {
        lock (_gate)
        {
            if (processId is not { } id || id == _exitedProcess || id == _staleProcess)
            {
                _process = null;
                return false;
            }

            if (id == _awaited)
            {
                _process = id;
                return true;
            }

            // Ids are reused, so a recorded process may be another program's by now.
            if (!steam.IsRunning(id))
            {
                _staleProcess = id;
                _process = null;
                return false;
            }

            _process = _awaited = id;
            _run = new SteamRun(steam.StartedAt(id), time.GetUtcNow());
        }

        _ = WaitForExitAsync(processId.Value, stopping);
        return true;
    }

    /// <summary>Takes a process for gone, once someone else saw it exit.</summary>
    /// <param name="processId">The process that exited.</param>
    public void MarkExited(int processId)
    {
        lock (_gate)
        {
            _exitedProcess = processId;
        }
    }

    private async Task WaitForExitAsync(int processId, CancellationToken stopping)
    {
        try
        {
            if (await steam.WaitForExitAsync(processId, stopping).ConfigureAwait(false))
            {
                MarkExited(processId);
                Exited?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
