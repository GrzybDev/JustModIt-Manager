using System.Reflection;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Protocol.Activation;

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
    public void The_version_leaves_out_the_commit_the_build_appends() =>
        HostEnvironment.VersionOf(typeof(HostEnvironmentTests).Assembly).Should()
            .Be(typeof(HostEnvironmentTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion
                .Split('+')[0]).And.NotContain("+");
}
