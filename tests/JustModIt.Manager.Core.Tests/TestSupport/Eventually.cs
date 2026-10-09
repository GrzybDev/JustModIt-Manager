using System.Diagnostics;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal static class Eventually
{
    /// <summary>Waits for work running on the thread pool to reach a state the test can observe.</summary>
    /// <param name="condition">Checked every few milliseconds, for at most thirty seconds.</param>
    /// <param name="because">What the test waits for, as the timeout's message says it.</param>
    /// <param name="nudge">Runs between checks, e.g. to move a fake clock for work that started waiting late.</param>
    public static async Task HoldsAsync(Func<bool> condition, string because, Action? nudge = null)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
            nudge?.Invoke();
        }
    }

    /// <summary>Lets background work that is already running reach its next wait.</summary>
    public static Task SettleAsync() => Task.Delay(50, TestContext.Current.CancellationToken);
}
