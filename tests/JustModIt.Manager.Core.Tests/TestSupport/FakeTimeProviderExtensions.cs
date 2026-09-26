using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal static class FakeTimeProviderExtensions
{
    /// <summary>Moves the clock in steps until <paramref name="task"/> completes and returns the simulated time it took.</summary>
    public static async Task<TimeSpan> RunUntilCompletedAsync(this FakeTimeProvider time, Task task, TimeSpan? step = null)
    {
        var increment = step ?? TimeSpan.FromMilliseconds(50);
        var elapsed = TimeSpan.Zero;
        while (!task.IsCompleted)
        {
            if (elapsed > TimeSpan.FromHours(1))
            {
                throw new TimeoutException("The task did not complete within an hour of simulated time.");
            }

            await Task.Yield();
            time.Advance(increment);
            elapsed += increment;
        }

        await task;
        return elapsed;
    }
}
