namespace JustModIt.Manager.LiveTests;

/// <summary>Live tests run against the real site and this machine's stores, so they run only when asked for.</summary>
internal static class LiveTest
{
    private const string Switch = "MANAGER_LIVE_TESTS";

    public static void SkipUnlessEnabled() =>
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Switch) == "1", $"Set {Switch}=1 to run tests against the real site.");
}
