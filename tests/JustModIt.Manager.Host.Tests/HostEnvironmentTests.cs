using System.Reflection;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Host.Tests;

public sealed class HostEnvironmentTests : IDisposable
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

    private readonly string? _runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

    public HostEnvironmentTests()
    {
        // Tests in a container run without a session, which gives every process of the app its runtime folder.
        if (!OperatingSystem.IsWindows() && _runtimeDirectory is null)
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", Path.GetTempPath());
        }
    }

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
    public async Task The_system_gives_every_service_the_agents_parts_take_and_the_app_still_replaces_one()
    {
        Assert.SkipUnless(AgentPlatform.IsSupported, "Only Windows has the system services the agent takes.");
        var data = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
        var options = HostEnvironment.Create(Product, Build).AgentOptions() with
        {
            Locations = new AppDataLocations(data, Path.Combine(data, "cache"), Path.Combine(data, "logs")),
        };
        var handoff = new NoHandoff();
        try
        {
            await using var settings = await SettingsService.LoadAsync(options.Locations, TimeProvider.System, NullLoggerFactory.Instance,
                TestContext.Current.CancellationToken);
            var services = new ServiceCollection().AddAgent(options, settings, NullLoggerFactory.Instance);
            AgentPlatform.Compose(services, options, app => app.AddSingleton<IForegroundHandoff>(handoff));

            // Building checks what every constructor takes, but not what a factory resolves, as the stores' providers do.
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            provider.GetServices<ILibraryProvider>().Should().HaveCount(3);
            provider.GetRequiredService<IForegroundHandoff>().Should().BeSameAs(handoff, "the app's registrations come last");
        }
        finally
        {
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
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

        HostEnvironment.Create(Product with { UpdateUrl = "https://github.com/example/example-app" }, Build)
            .AgentOptions().Updates.Should().Be(new UpdateFeed("https://github.com/example/example-app", null));
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
        context.CreatorDirectory.Should().Be(environment.Locations.CreatorDirectory);
        context.Codecs.Should().Match(codecs => !OperatingSystem.IsWindows() || codecs is WindowsMediaCodecs, "the site's AVIF stills need them");
        context.SystemMotion.Should().Match(motion => !OperatingSystem.IsWindows() || motion is WindowsSystemMotion);
        context.DesktopId.Should().Be("com.example.ExampleApp.Dev", "a development build's windows never group with an installed copy's");
    }

    [Fact]
    public void The_window_draws_its_own_frame_on_windows_leaves_it_to_linux_desktops_and_fills_gamescopes_screen()
    {
        string?[] desktops = ["KDE", "ubuntu:GNOME", "gamescope", null];

        desktops.Select(HostEnvironment.FrameFor).Should().Equal(OperatingSystem.IsWindows()
            ? [WindowFrame.Own, WindowFrame.Own, WindowFrame.Own, WindowFrame.Own]
            : [WindowFrame.System, WindowFrame.System, WindowFrame.Screen, WindowFrame.System]);
    }

    [Fact]
    public void The_version_and_the_commit_the_build_appends_to_it_come_apart()
    {
        var assembly = typeof(HostEnvironmentTests).Assembly;
        var built = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+');

        HostEnvironment.VersionOf(assembly).Should().Be(built[0]).And.NotContain("+");
        HostEnvironment.RevisionOf(assembly).Should().Be(built.ElementAtOrDefault(1));
    }

    public void Dispose() => Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", _runtimeDirectory);

    private sealed class NoHandoff : IForegroundHandoff
    {
        public void Allow(int processId)
        {
        }
    }
}
