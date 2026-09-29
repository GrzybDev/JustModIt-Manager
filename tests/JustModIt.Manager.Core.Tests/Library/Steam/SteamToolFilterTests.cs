using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamToolFilterTests
{
    [Theory]
    [InlineData(228980u, "Steamworks Common Redistributables", "Steamworks Shared")]
    [InlineData(1493710u, "Proton Experimental", "Proton - Experimental")]
    [InlineData(999999001u, "Proton 10.0", "Proton 10.0")]
    [InlineData(999999002u, "Proton", "Proton")]
    [InlineData(999999003u, "Steam Linux Runtime 4.0", "Runtime")]
    [InlineData(999999004u, "Some Runtime", "SteamLinuxRuntime_quartz")]
    public void Tools_and_runtimes_are_recognised(uint appId, string name, string installDir) =>
        SteamToolFilter.IsTool(Manifest(appId, name, installDir)).Should().BeTrue();

    [Theory]
    [InlineData("Protonaut", "Protonaut")]
    [InlineData("My Steamworks Common Game", "Game")]
    [InlineData("Example Quest", "Example Quest")]
    public void Games_are_not_tools(string name, string installDir) =>
        SteamToolFilter.IsTool(Manifest(10, name, installDir)).Should().BeFalse();

    private static SteamAppManifest Manifest(uint appId, string name, string installDir) =>
        new() { AppId = appId, Name = name, InstallDir = installDir };
}
