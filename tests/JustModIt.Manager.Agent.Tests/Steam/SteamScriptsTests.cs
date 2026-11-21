using JustModIt.Manager.Agent.Steam;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class SteamScriptsTests
{
    [Theory]
    [InlineData(SteamScripts.HookResource, "steam/achievement-texts.js")]
    [InlineData(SteamScripts.LaunchOptionsResource, "steam/launch-options.js")]
    [InlineData(SteamScripts.FrontendResource, "steam/millennium/frontend.js")]
    [InlineData(SteamScripts.BackendResource, "steam/millennium/backend/main.lua")]
    public void Every_script_ships_as_it_is_in_the_repository(string resource, string file) =>
        SteamScripts.Read(resource).Should().Be(File.ReadAllText(RepositoryFiles.PathOf(file.Split('/'))));

    [Fact]
    public void The_plugin_carries_both_scripts_and_its_own_name()
    {
        var frontend = SteamScripts.PluginFrontend("example-app");

        frontend.Should().Contain("const pluginName = 'example-app';").And.Contain(SteamScripts.Hook).And.Contain(SteamScripts.LaunchOptions)
            .And.NotContain("__PLUGIN_NAME__").And.NotContain("__HOOK__").And.NotContain("__LAUNCH_OPTIONS__");
        SteamScripts.PluginBackend("example-app").Should().Contain("local PLUGIN = \"example-app\"");
    }
}
