using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Tests.Shell;

public sealed class ShellIntegrationTests : IAsyncDisposable
{
    private readonly AgentHarness _agent = new();

    public ShellIntegrationTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    private string Command => $"\"{_agent.Options.ExecutablePath}\" --background";

    private AppIdentity Development => new(_agent.Options.Identity.AppId, _agent.Options.Identity.VendorId, "example-app",
        isDevelopmentBuild: true);

    [Fact]
    public async Task An_installed_app_registers_its_links_and_starts_with_the_system_by_default()
    {
        _agent.Options = _agent.Options with { IntegratesWithShell = true };
        _agent.Start();

        await Eventually.HoldsAsync(() => _agent.Registration.Command == Command);
        _agent.Registration.Schemes.Should().ContainKey("example-app")
            .WhoseValue.Should().Be(("Example App", _agent.Options.ExecutablePath));
    }

    [Fact]
    public async Task Autostart_follows_the_setting()
    {
        _agent.Options = _agent.Options with { IntegratesWithShell = true };
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var settings = (await window.StateAsync(state => state.IsLoaded)).Settings;
        await Eventually.HoldsAsync(() => _agent.Registration.State == AutostartState.Enabled);

        await window.RequestAsync(new ChangeSettings(settings with { StartWithSystem = false }));
        await Eventually.HoldsAsync(() => _agent.Registration.State == AutostartState.Disabled);

        await window.RequestAsync(new ChangeSettings(settings with { StartWithSystem = true }));
        await Eventually.HoldsAsync(() => _agent.Registration.State == AutostartState.Enabled);
    }

    [Fact]
    public async Task Turning_autostart_off_in_the_system_turns_the_setting_off()
    {
        _agent.Options = _agent.Options with { IntegratesWithShell = true };
        _agent.Registration.State = AutostartState.DisabledBySystem;
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        await window.StateAsync(state => state.IsLoaded && !state.Settings.StartWithSystem);
        await Eventually.HoldsAsync(() => _agent.Registration.State == AutostartState.Disabled);
    }

    [Fact]
    public async Task A_development_build_leaves_the_system_alone()
    {
        _agent.Options = _agent.Options with { Identity = Development };
        await _agent.SaveSettingsAsync(AppSettings.Default with { StartWithSystem = true });
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.IsLoaded);

        _agent.Registration.Command.Should().BeNull();
        _agent.Registration.Schemes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_development_build_asked_to_integrate_registers_links_of_its_own()
    {
        _agent.Options = _agent.Options with { Identity = Development, IntegratesWithShell = true };
        _agent.Start();

        await Eventually.HoldsAsync(() => _agent.Registration.Schemes.ContainsKey("example-app-dev"));
        _agent.Registration.Schemes.Keys.Should().Equal("example-app-dev");
    }

    [Fact]
    public void Registering_again_after_an_update_keeps_autostart_that_the_system_turned_off()
    {
        _agent.Registration.State = AutostartState.DisabledBySystem;

        Register();

        _agent.Registration.State.Should().Be(AutostartState.DisabledBySystem);
        _agent.Registration.Schemes.Should().ContainKey("example-app");
    }

    [Fact]
    public void Uninstalling_removes_the_links_and_autostart()
    {
        Register();

        ShellRegistration.Unregister(_agent.Registration, _agent.Registration, _agent.Options.Identity.Slug);

        _agent.Registration.State.Should().Be(AutostartState.Disabled);
        _agent.Registration.Schemes.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private void Register() => ShellRegistration.Register(_agent.Registration, _agent.Registration, _agent.Options.Identity.Slug,
        _agent.Options.ProductName, _agent.Options.ExecutablePath, startWithSystem: true);
}
