using JustModIt.Manager.Agent.Updates;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Agent;

/// <summary>
/// A check the player asks for: the stores are scanned, the catalogue is read again and the app looks for its own
/// update, whatever their schedules.
/// </summary>
/// <param name="library">Scans the stores.</param>
/// <param name="catalogue">Reads the catalogue again.</param>
/// <param name="updates">Looks for the app's own update.</param>
/// <param name="status">Shows that a check runs.</param>
public sealed class Checks(ILibraryService library, CatalogueRefreshLoop catalogue, UpdateService updates, AgentStatus status) : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private int _running;

    /// <summary>
    /// Runs a check, returning once the scan and the update check end; the catalogue's refresh is only asked for.
    /// </summary>
    public async Task CheckNowAsync()
    {
        SetRunning(+1);
        try
        {
            catalogue.RequestRefresh();
            await Task.WhenAll(library.CheckNowAsync(_stopping.Token), updates.CheckNowAsync())
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally
        {
            SetRunning(-1);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    // Under a lock, so overlapping checks cannot leave the flag off while one still runs.
    private void SetRunning(int change)
    {
        lock (_gate)
        {
            _running += change;
            var checking = _running > 0;
            status.ChangeActivity(activity => activity with { IsChecking = checking });
        }
    }
}
