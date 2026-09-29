using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal static class FakeTimeProviderExtensions
{
    /// <summary>
    /// Moves the clock in steps until <paramref name="task"/> completes, for at most an hour of simulated time, and returns
    /// the simulated time it took.
    /// </summary>
    /// <param name="time">The clock the task's work waits on.</param>
    /// <param name="task">The work to run the clock for.</param>
    /// <param name="step">How far each step moves the clock; null for 50 ms.</param>
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

            // A moment of real time lets work that a timer released on the thread pool reach its next wait before the clock
            // moves on; yielding alone can run through the whole hour before that work gets a thread.
            await Task.WhenAny(task, Task.Delay(1, TestContext.Current.CancellationToken));
            if (task.IsCompleted)
            {
                break;
            }

            time.Advance(increment);
            elapsed += increment;
        }

        await task;
        return elapsed;
    }
}
