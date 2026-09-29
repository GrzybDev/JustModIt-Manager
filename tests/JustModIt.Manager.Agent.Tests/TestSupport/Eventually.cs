namespace JustModIt.Manager.Agent.Tests.TestSupport;

internal static class Eventually
{
    /// <summary>Waits until <paramref name="condition"/> holds, for agent work on a thread of its own with nothing to wait on.</summary>
    /// <param name="condition">Checked every 50 ms; the test fails once ten seconds pass.</param>
    public static async Task HoldsAsync(Func<bool> condition)
    {
        for (var attempt = 0; !condition(); attempt++)
        {
            attempt.Should().BeLessThan(200, "the condition should hold within ten seconds");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
