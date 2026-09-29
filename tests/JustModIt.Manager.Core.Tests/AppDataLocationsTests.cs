namespace JustModIt.Manager.Core.Tests;

public sealed class AppDataLocationsTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";
    private static readonly string Home = Rooted("home", "player");

    private readonly Dictionary<string, string> _environment = new() { ["HOME"] = Home };
    private readonly Dictionary<Environment.SpecialFolder, string> _folders = new()
    {
        [Environment.SpecialFolder.LocalApplicationData] = Rooted("Users", "player", "AppData", "Local"),
        [Environment.SpecialFolder.UserProfile] = Home,
    };

    [Fact]
    public void Windows_keeps_everything_in_one_folder_under_local_app_data()
    {
        var locations = Resolve(AppPlatform.Windows);

        var data = Rooted("Users", "player", "AppData", "Local", "ExampleVendor", "ExampleApp");
        locations.DataDirectory.Should().Be(data);
        locations.SettingsFile.Should().Be(Path.Combine(data, "settings.json"));
        locations.StateDirectory.Should().Be(Path.Combine(data, "state"));
        locations.InstallsDirectory.Should().Be(Path.Combine(data, "installs"));
        locations.BackupsDirectory.Should().Be(Path.Combine(data, "backups"));
        locations.CacheDirectory.Should().Be(Path.Combine(data, "cache"));
        locations.CatalogueCacheDirectory.Should().Be(Path.Combine(data, "cache", "catalogue"));
        locations.DownloadsDirectory.Should().Be(Path.Combine(data, "cache", "downloads"));
        locations.LogsDirectory.Should().Be(Path.Combine(data, "logs"));
    }

    [Fact]
    public void Linux_follows_the_xdg_defaults()
    {
        var locations = Resolve(AppPlatform.Linux);

        locations.DataDirectory.Should().Be(Path.Combine(Home, ".local", "share", "ExampleVendor", "ExampleApp"));
        locations.CacheDirectory.Should().Be(Path.Combine(Home, ".cache", "ExampleVendor", "ExampleApp"));
        locations.LogsDirectory.Should().Be(Path.Combine(Home, ".local", "state", "ExampleVendor", "ExampleApp", "logs"));
    }

    [Fact]
    public void Linux_honours_absolute_xdg_variables_and_ignores_relative_ones()
    {
        _environment["XDG_DATA_HOME"] = Rooted("data");
        _environment["XDG_CACHE_HOME"] = "relative/cache";

        var locations = Resolve(AppPlatform.Linux);

        locations.DataDirectory.Should().Be(Path.Combine(Rooted("data"), "ExampleVendor", "ExampleApp"));
        locations.CacheDirectory.Should().Be(Path.Combine(Home, ".cache", "ExampleVendor", "ExampleApp"));
    }

    [Fact]
    public void MacOS_uses_the_library_folders()
    {
        var locations = Resolve(AppPlatform.MacOS);

        var library = Path.Combine(Home, "Library");
        locations.DataDirectory.Should().Be(Path.Combine(library, "Application Support", "ExampleVendor", "ExampleApp"));
        locations.CacheDirectory.Should().Be(Path.Combine(library, "Caches", "ExampleVendor", "ExampleApp"));
        locations.LogsDirectory.Should().Be(Path.Combine(library, "Logs", "ExampleVendor", "ExampleApp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AppData")]
    public void An_unresolved_base_folder_fails_fast(string localAppData)
    {
        _folders[Environment.SpecialFolder.LocalApplicationData] = localAppData;

        var act = () => Resolve(AppPlatform.Windows);

        act.Should().Throw<AppDataUnavailableException>();
    }

    [Fact]
    public void A_missing_home_falls_back_to_the_profile_and_then_fails_fast()
    {
        _environment.Remove("HOME");
        Resolve(AppPlatform.Linux).CacheDirectory.Should().StartWith(Home);

        _folders[Environment.SpecialFolder.UserProfile] = string.Empty;
        var act = () => Resolve(AppPlatform.Linux);

        act.Should().Throw<AppDataUnavailableException>();
    }

    [Theory]
    [InlineData(false, "ExampleApp")]
    [InlineData(true, "ExampleApp.Dev")]
    public void The_current_user_gets_a_fully_qualified_folder_named_after_the_app(bool isDevelopmentBuild, string folder)
    {
        var identity = new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild);

        var locations = AppDataLocations.ForCurrentUser(identity);

        Path.GetFileName(locations.DataDirectory).Should().Be(folder);
        Path.GetFileName(Path.GetDirectoryName(locations.DataDirectory)).Should().Be("ExampleVendor");
        Path.IsPathFullyQualified(locations.DataDirectory).Should().BeTrue();
    }

    private AppDataLocations Resolve(AppPlatform platform) =>
        AppDataLocations.Resolve(platform, _environment.GetValueOrDefault, folder => _folders.GetValueOrDefault(folder, string.Empty),
            "ExampleVendor", "ExampleApp");

    private static string Rooted(params string[] segments) => Path.Combine([Root, .. segments]);
}
