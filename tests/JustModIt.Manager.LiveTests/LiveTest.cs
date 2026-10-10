using JustModIt.Manager.Core.Games;

// The tests share this machine's games and the site's download counter, so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JustModIt.Manager.LiveTests;

/// <summary>Live tests run against the real site and this machine's stores, so they run only when asked for.</summary>
internal static class LiveTest
{
    private const string Switch = "MANAGER_LIVE_TESTS";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static void SkipUnlessEnabled() =>
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Switch) == "1", $"Set {Switch}=1 to run tests against the real site.");

    public static void SkipUnlessEnabledOnWindows()
    {
        SkipUnlessEnabled();
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The engine's live tests need the Windows stores.");
    }
}

/// <summary>Games the owner installed on their machine for live testing.</summary>
internal static class OwnerGames
{
    public static readonly GameKey Dispatch = new(StoreId.Steam, "2592160");

    public static readonly GameKey QuantumBreak = new(StoreId.Steam, "474960");

    public static readonly GameKey ManOfMedan = new(StoreId.Steam, "939850");

    /// <summary>Its project is only announced, with no release yet.</summary>
    public static readonly GameKey WhiteWhisper = new(StoreId.Steam, "3994800");
}
