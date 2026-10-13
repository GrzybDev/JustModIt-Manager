using Avalonia.Threading;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// Hands the newest report of work on the thread pool to the UI thread, coalescing the reports that come in between, so
/// hashing thousands of small files never floods the dispatcher.
/// </summary>
/// <param name="show">Shows a report, on the UI thread.</param>
/// <typeparam name="T">The report's type.</typeparam>
public sealed class LatestProgress<T>(Action<T> show) : IProgress<T>
    where T : struct
{
    private readonly Lock _gate = new();
    private T? _latest;

    public void Report(T value)
    {
        lock (_gate)
        {
            var posted = _latest is not null;
            _latest = value;
            if (posted)
            {
                return;
            }
        }

        Dispatcher.UIThread.Post(Show, DispatcherPriority.Background);
    }

    private void Show()
    {
        T? value;
        lock (_gate)
        {
            (value, _latest) = (_latest, null);
        }

        if (value is { } report)
        {
            show(report);
        }
    }
}
