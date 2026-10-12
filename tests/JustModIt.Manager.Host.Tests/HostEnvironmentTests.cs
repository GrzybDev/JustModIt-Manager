using System.Reflection;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Host.Tests;

public sealed class HostEnvironmentTests
{
    private static readonly InstallLocation Build = new(@"C:\Work\ExampleApp\bin\ExampleApp.exe", @"C:\Work\ExampleApp\bin", IsInstalled: false);

    private static readonly InstallLocation Installed = new(@"C:\Users\Example\AppData\Local\ExampleApp\current\ExampleApp.exe",
        @"C:\Users\Example\AppData\Local\ExampleApp", IsInstalled: true);

    private static readonly ProductSettings Product = ProductConfiguration.Settings with
    {
        SiteUrl = "https://example.com/",
        ApiKey = "test-key",
        IsDevelopmentBuild = true,
    };

    [Fact]
    public void The_agent_gets_the_apps_identity_site_and_names()
    {
        var options = HostEnvironment.Create(Product, Build).AgentOptions();

        options.Identity.InstanceName.Should().Be("ExampleApp.Dev");
        options.ProductName.Should().Be(ProductConfiguration.ProductName);
        options.Names.Should().Be(InstanceNames.ForCurrentUser("ExampleApp.Dev"));
        options.Api.ApiAddress.Should().Be(new Uri("https://api.example.com/"));
        options.Api.SiteAddress.Should().Be(new Uri("https://example.com/"));
        options.Api.ApiKey.Should().Be("test-key");
        options.TargetLanguage.Code.Should().Be("de");
        options.Locations.DataDirectory.Should().EndWith(Path.Combine("ExampleVendor", "ExampleApp.Dev"));
    }

    [Fact]
    public void A_development_build_handles_links_of_its_own()
    {
        HostEnvironment.Create(Product, Build).Links.Scheme.Should().Be("example-app-dev");
        HostEnvironment.Create(Product with { IsDevelopmentBuild = false }, Installed).Links.Scheme.Should().Be("example-app");
    }

    [Fact]
    public void A_development_build_leaves_the_system_alone_unless_asked()
    {
        HostEnvironment.Create(Product, Build).AgentOptions().IntegratesWithShell.Should().BeFalse();
        HostEnvironment.Create(Product with { DevelopmentShellIntegration = true }, Build).AgentOptions().IntegratesWithShell.Should().BeTrue();
        HostEnvironment.Create(Product with { IsDevelopmentBuild = false }, Build).AgentOptions().IntegratesWithShell.Should().BeFalse();
    }

    [Fact]
    public void An_installed_copy_registers_itself_through_the_updaters_stable_executable()
    {
        var options = HostEnvironment.Create(Product with { IsDevelopmentBuild = false }, Installed).AgentOptions();

        options.IntegratesWithShell.Should().BeTrue();
        options.ExecutablePath.Should().Be(Installed.ExecutablePath);
        options.InstallDirectory.Should().Be(Installed.Directory);
    }

    [Fact]
    public void Updates_follow_the_apps_feed_and_are_off_without_one()
    {
        HostEnvironment.Create(Product, Build).AgentOptions().Updates.Should().BeNull();

        HostEnvironment.Create(Product with { UpdateUrl = "https://github.com/example/example-app", UpdatePrereleases = true }, Build)
            .AgentOptions().Updates.Should().Be(new UpdateFeed("https://github.com/example/example-app", null, IncludesPrereleases: true));
        HostEnvironment.Create(Product with { UpdateUrl = "https://github.com/example/example-app", UpdateChannel = "beta" }, Build)
            .AgentOptions().Updates!.Channel.Should().Be("beta");
    }

    [Fact]
    public void The_agent_hears_that_the_updater_restarted_the_app() =>
        (HostEnvironment.Create(Product, Installed) with { RestartedByUpdater = true }).AgentOptions().RestartedByUpdater.Should().BeTrue();

    [Fact]
    public void Packages_and_projects_take_the_apps_extensions()
    {
        var environment = HostEnvironment.Create(Product, Build);
        var creator = environment.CreatorOptions();

        environment.AgentOptions().PackageExtension.Should().Be(".examplemod");
        (creator.PackageExtension, creator.ProjectExtension).Should().Be((".examplemod", ".exampleproj"));
    }

    [Fact]
    public void The_window_gets_the_apps_accent_its_names_its_site_without_the_key_the_systems_decoders_and_motion_setting_and_its_own_composition()
    {
        Action<UiComposition> compose = _ => { };
        var options = new AppOptions { Product = Product, App = typeof(HostEnvironmentTests).Assembly, ConfigureUi = compose };
        var environment = HostEnvironment.Create(Product with
        {
            AccentColor = "#3B82F6",
            ImageHosts = "images.example.com; posters.example.net/vi/",
            SourceUrl = "https://code.example.com/example-app",
        }, Build);

        var context = environment.WindowContext(options, new LaunchArguments(ProcessRole.Ui, Background: false, Target: null),
            NullLoggerFactory.Instance);

        context.AccentColor.Should().Be("#3B82F6");
        context.Compose.Should().BeSameAs(compose);
        context.ProductName.Should().Be(ProductConfiguration.ProductName);
        context.PipeName.Should().Be(InstanceNames.ForCurrentUser("ExampleApp.Dev").PipeName);
        context.Site.SiteAddress.Should().Be(new Uri("https://example.com/"));
        context.Site.ApiKey.Should().BeNull("only the agent talks to the API");
        context.ImageHosts.Should().Equal("images.example.com", "posters.example.net/vi/");
        context.SourceAddress.Should().Be(new Uri("https://code.example.com/example-app"));
        context.IssuesAddress.Should().BeNull("an app without a place for reports offers none");
        context.ImageCacheDirectory.Should().Be(Path.Combine(environment.Locations.CacheDirectory, "images"));
        context.Codecs.Should().Match(codecs => !OperatingSystem.IsWindows() || codecs is WindowsMediaCodecs, "the site's AVIF stills need them");
        context.SystemMotion.Should().Match(motion => !OperatingSystem.IsWindows() || motion is WindowsSystemMotion);
    }

    [Fact]
    public void The_version_and_the_commit_the_build_appends_to_it_come_apart()
    {
        var assembly = typeof(HostEnvironmentTests).Assembly;
        var built = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+');

        HostEnvironment.VersionOf(assembly).Should().Be(built[0]).And.NotContain("+");
        HostEnvironment.RevisionOf(assembly).Should().Be(built.ElementAtOrDefault(1));
    }
}
