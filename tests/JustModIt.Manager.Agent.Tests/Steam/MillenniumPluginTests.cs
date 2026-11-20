using System.Text.Json;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class MillenniumPluginTests : IDisposable
{
    private const string Name = "example-app";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"), "home");
    private readonly MillenniumFolders _millennium;
    private readonly MillenniumPlugin _plugin;

    public MillenniumPluginTests()
    {
        // The Linux layout, which is the user's own whatever the system, with one XDG folder set and one left to its default.
        _millennium = MillenniumFolders.For(Path.Combine(_home, ".local", "share", "Steam"),
            new LibraryEnvironment { Platform = AppPlatform.Linux, HomeDirectory = _home, XdgConfigHome = Path.Combine(_home, "settings") });
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        _plugin = new MillenniumPlugin(_millennium, Name);
    }

    private string ConfigFile => _millennium.ConfigFile;

    [Fact]
    public void On_linux_millennium_keeps_its_plugins_and_settings_in_the_user_s_folders_and_counts_as_installed_only_with_its_files()
    {
        (_millennium.Plugins, _millennium.ConfigFile).Should().Be((Path.Combine(_home, ".local", "share", "millennium", "plugins"),
            Path.Combine(_home, "settings", "millennium", "config.json")));
        Config("""{"plugins": {"enabledPlugins": ["another-plugin"]}}""");
        var library = Path.Combine(_home, "millennium-files");

        (_millennium with { Library = library }).IsInstalled.Should().BeFalse("Millennium's settings outlive it in the user's folders");
        Directory.CreateDirectory(library);
        (_millennium with { Library = library }).IsInstalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_plugin_the_player_turned_off_in_millennium_stays_off_when_the_app_updates_it()
    {
        Config("""{"plugins": {"enabledPlugins": ["another-plugin"]}}""");
        await _plugin.InstallAsync(Files("1.4.0"), AgentHarness.Token);
        Enabled().Should().Equal("another-plugin", Name);

        Config("""{"plugins": {"enabledPlugins": ["another-plugin"]}}""");
        _plugin.IsCurrent(Files("1.4.0")).Should().BeTrue("the player's choice in Millennium is no change of the files");
        await _plugin.InstallAsync(Files("1.5.0"), AgentHarness.Token);

        (_plugin.InstalledVersion(), _plugin.IsEnabled()).Should().Be(("1.5.0", false));
        Enabled().Should().Equal("another-plugin");
    }

    [Theory]
    [InlineData("""{"plugins": []}""")]
    [InlineData("""{"plugins": {"enabledPlugins": {"another-plugin": true}}}""")]
    public async Task Millennium_s_settings_of_another_shape_are_left_as_they_are(string config)
    {
        Config(config);

        var install = () => _plugin.InstallAsync(Files("1.4.0"), AgentHarness.Token);

        await install.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllTextAsync(ConfigFile, AgentHarness.Token)).Should().Be(config);
        _plugin.InstalledVersion().Should().BeNull();
    }

    [Fact]
    public async Task Removing_takes_the_plugin_s_folder_away_also_when_millennium_s_settings_cannot_be_read()
    {
        Config("""{"plugins": {"enabledPlugins": []}}""");
        await _plugin.InstallAsync(Files("1.4.0"), AgentHarness.Token);
        Config("{ half written");

        var remove = () => _plugin.RemoveAsync(AgentHarness.Token);

        await remove.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(_plugin.Directory).Should().BeFalse();
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_home)!, recursive: true);

    private static IReadOnlyDictionary<string, byte[]> Files(string version) =>
        MillenniumPlugin.Files(Name, version, "Achievement texts", "Shows translated achievements.");

    private void Config(string content) => File.WriteAllText(ConfigFile, content);

    private List<string?> Enabled()
    {
        using var config = JsonDocument.Parse(File.ReadAllBytes(ConfigFile));
        return [.. config.RootElement.GetProperty("plugins").GetProperty("enabledPlugins").EnumerateArray().Select(name => name.GetString())];
    }
}
